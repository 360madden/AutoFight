"""Fetch one fresh AutoFight GuestReader snapshot from exactly VMware RIFT-LAB-2.

This is observation-only. The reader must ALREADY be running, with its PID and
UTC process-start ticks independently provided by the operator. A private,
source-hash-matching Lab2 staging receipt and current game pin are mandatory.
No guest filesystem writes, reader launch, game input, UI reload or VM lifecycle
operations are implemented. A read-only PowerShell identity gate runs twice.

The raw snapshot is saved only under ignored artifacts/local using an absent-only
hard link; stdout contains redacted status metadata, never bearer tokens.
"""
from __future__ import annotations

import argparse
from dataclasses import dataclass
from datetime import datetime
import hashlib
import json
import math
import os
from pathlib import Path
import re
import sys
import tempfile
import uuid

import bootstrap_lab1_host_telemetry as host_json
import fetch_lab1_guest_reader_snapshot as common
import lab1_guest_deploy as base
import lab2_guest_reader_deploy as lab2

PRIVATE = lab2.PRIVATE_ROOT
DEFAULT_GAME_PIN = PRIVATE / "lab2-game-identity.local.json"
DEFAULT_SNAPSHOT = lab2.GUEST_READER_ROOT + r"\latest.json"
READER_EXE = lab2.GUEST_READER_ROOT + "\\" + lab2.EXE
SHA64 = re.compile(r"[0-9a-f]{64}\Z", re.ASCII)
STAGE32 = re.compile(r"[0-9a-f]{32}\Z", re.ASCII)
OUTPUT_NAME = re.compile(r"lab2-guest-latest-[0-9a-f]{32}\.local\.json\Z", re.ASCII)
MAX_SNAPSHOT_BYTES = 128 * 1024


class FetchBlocked(lab2.Blocked):
    """An intentionally bounded, token-free failure reason."""


def blocked(reason: str) -> None:
    raise FetchBlocked(reason)


@dataclass(frozen=True)
class Binding:
    stage_id: str
    reader_sha256: str
    config_sha256: str
    game_pin_sha256: str
    stage_receipt_sha256: str
    game_pin: dict
    addon_hashes: tuple[str, ...]


def load_binding(stage_id: str, game_pin_path: Path = DEFAULT_GAME_PIN) -> Binding:
    """Check the private Lab2 stage receipt against exact current host sources."""
    if type(stage_id) is not str or STAGE32.fullmatch(stage_id) is None:
        blocked("lab2-stage-id-invalid")
    try:
        private = PRIVATE.resolve(strict=True)
        path = game_pin_path.resolve(strict=True)
        if (path.parent != private or path.name != DEFAULT_GAME_PIN.name
                or game_pin_path.is_symlink() or game_pin_path.is_junction()):
            blocked("lab2-game-pin-path-invalid")
    except OSError:
        blocked("lab2-game-pin-unavailable")
    pin_bytes = lab2.regular_bytes(game_pin_path, 4096)
    game_pin = lab2.parse_json(pin_bytes, limit=4096)
    lab2.validate_game_pin(game_pin)
    _, expected_files, addons = lab2.validate_host_inputs(
        lab2.DEFAULT_CONFIG, lab2.DEFAULT_PACKAGE
    )
    if ([item["relativePath"] for item in expected_files] !=
            [lab2.EXE, lab2.GUEST_CONFIG]):
        blocked("lab2-reader-package-invalid")
    receipt_file = PRIVATE / ("lab2-stage-" + stage_id + ".local.json")
    receipt_bytes = lab2.regular_bytes(receipt_file, 16_000)
    receipt = lab2.parse_json(receipt_bytes, limit=16_000)
    keys = {
        "schemaVersion", "kind", "stageId", "manifestSha256",
        "gamePinSha256", "guestConfigSha256", "readerExeSha256", "addonFiles"
    }
    if (set(receipt) != keys or type(receipt["schemaVersion"]) is not int
            or receipt["schemaVersion"] != 1
            or receipt["kind"] != "autofight-lab2-reader-stage-receipt"
            or receipt["stageId"] != stage_id
            or type(receipt["manifestSha256"]) is not str
            or SHA64.fullmatch(receipt["manifestSha256"]) is None
            or receipt["gamePinSha256"] != hashlib.sha256(pin_bytes).hexdigest()
            or receipt["guestConfigSha256"] != expected_files[1]["sha256"]
            or receipt["readerExeSha256"] != expected_files[0]["sha256"]
            or receipt["addonFiles"] != addons):
        blocked("lab2-private-stage-or-source-drift")
    return Binding(
        stage_id=stage_id,
        reader_sha256=expected_files[0]["sha256"],
        config_sha256=expected_files[1]["sha256"],
        game_pin_sha256=hashlib.sha256(pin_bytes).hexdigest(),
        stage_receipt_sha256=hashlib.sha256(receipt_bytes).hexdigest(),
        game_pin=game_pin,
        addon_hashes=tuple(item["sha256"] for item in addons),
    )


def identity_powershell(reader_pid: int, reader_start: int,
                        binding: Binding) -> str:
    """Pre/post read-only process, machine, exact executable and hash gate."""
    if (binding.game_pin.get("lab") != "lab2"
            or binding.game_pin.get("machine") != "RIFT-LAB-2"
            or not lab2.valid_game_executable_path(
                binding.game_pin.get("gameExecutablePath"))
            or SHA64.fullmatch(binding.reader_sha256) is None):
        blocked("lab2-reader-binding-invalid")
    # The proven Lab1 builder uses the SAME installed GuestReader path; its
    # game pin is separately validated here as exact Lab2.
    checks = common.identity_powershell(
        reader_pid, reader_start, binding.game_pin, binding.reader_sha256
    )
    if READER_EXE.casefold() != common.READER_EXE.casefold():
        blocked("lab2-reader-executable-path-drift")
    return (
        "$ErrorActionPreference='Stop';"
        "if([Environment]::MachineName -cne 'RIFT-LAB-2')"
        "{throw 'lab2-machine-mismatch'};"
        + checks
    )


def verify_snapshot(snapshot: dict, binding: Binding,
                    *, now: datetime | None = None) -> dict:
    """Validate the published C# schema, current game identity and 30s TTL."""
    if type(snapshot) is not dict:
        blocked("snapshot-root-invalid")
    required = {
        "schemaVersion", "instanceId", "nonce", "sequence", "capturedAtUtc",
        "monotonicMilliseconds", "readerStatus", "readerReason", "target",
        "riftReader2", "delivery"
    }
    permitted = required | {"fastObservation", "fastObservationDiagnostic"}
    if not required <= set(snapshot) or not set(snapshot) <= permitted:
        blocked("snapshot-schema-invalid")
    elapsed = snapshot["monotonicMilliseconds"]
    if (type(elapsed) not in (int, float) or not math.isfinite(elapsed)
            or elapsed < 0):
        blocked("snapshot-monotonic-invalid")
    reason = snapshot["readerReason"]
    if reason is not None and (type(reason) is not str or len(reason) > 512):
        blocked("snapshot-reason-invalid")
    target = snapshot["target"]
    if type(target) is not dict or set(target) != {
        "processId", "processStartUtcTicks", "executablePath",
        "executableSha256", "readerSessionNonce"
    }:
        blocked("snapshot-target-schema-invalid")
    rr2 = snapshot["riftReader2"]
    if (type(rr2) is not dict or type(rr2.get("schemaVersion")) is not int
            or rr2["schemaVersion"] != 1):
        blocked("snapshot-rr2-schema-invalid")
    delivery = snapshot["delivery"]
    if (type(delivery) is not dict or set(delivery) != {
        "enabled", "status", "endpoint", "lastSuccessUtc", "error"
    } or type(delivery["enabled"]) is not bool
            or type(delivery["status"]) is not str):
        blocked("snapshot-delivery-schema-invalid")
    verified = common.verify_snapshot(snapshot, binding.game_pin, now=now)
    # A reader nonce is an ephemeral binding secret: use a redacted CLI view.
    return {
        "readerInstanceId": verified["readerInstanceId"],
        "readerNonce": "[redacted]",
        "sequence": verified["sequence"],
        "capturedAtUtc": verified["capturedAtUtc"],
        "gameProcessId": verified["gameProcessId"],
    }


def check_private_output(output: Path) -> None:
    try:
        root = PRIVATE.resolve(strict=True)
        attrs = getattr(PRIVATE.lstat(), "st_file_attributes", 0)
        if (PRIVATE.is_symlink() or PRIVATE.is_junction()
                or (attrs & lab2.FILE_ATTRIBUTE_REPARSE_POINT)):
            blocked("lab2-private-root-reparse")
        if (output.parent.resolve(strict=True) != root
                or OUTPUT_NAME.fullmatch(output.name) is None
                or output.exists() or output.is_symlink()
                or output.is_junction()):
            blocked("lab2-output-not-absent-private")
    except OSError:
        blocked("lab2-output-path-invalid")


def fetch(reader_pid: int, reader_start: int, binding: Binding,
          output: Path) -> dict:
    """Acquire exactly one guest snapshot; only publish after two identity gates."""
    check_private_output(output)
    script = identity_powershell(reader_pid, reader_start, binding)
    lab2.configure_lab2_transport()
    # Repeatedly pin the VM identity; this helper never borrows Lab1 credentials.
    baseline = base.collect_identity()
    with tempfile.TemporaryDirectory(prefix="lab2-snapshot-fetch-", dir=PRIVATE) as folder:
        candidate = Path(folder) / "latest.json"
        with base.CachedVmwareGuest(baseline) as guest:
            guest.run("runProgramInGuest", base.POWERSHELL, "-NoProfile",
                      "-NonInteractive", "-Command", script, timeout=90)
            guest.run("copyFileFromGuestToHost", DEFAULT_SNAPSHOT,
                      str(candidate), timeout=90)
            report = verify_snapshot(
                host_json.read_json(candidate, MAX_SNAPSHOT_BYTES), binding
            )
            guest.run("runProgramInGuest", base.POWERSHELL, "-NoProfile",
                      "-NonInteractive", "-Command", script, timeout=90)
        if base.collect_identity() != baseline:
            blocked("lab2-vm-identity-changed")
        # Re-read ALL source/stage pins before publication, detecting drift.
        if load_binding(binding.stage_id) != binding:
            blocked("lab2-binding-changed-during-fetch")
        # A slow second process/hash gate must not publish an expired sample.
        # Reparse the bounded private snapshot immediately before publication.
        if verify_snapshot(
            host_json.read_json(candidate, MAX_SNAPSHOT_BYTES), binding
        ) != report:
            blocked("lab2-snapshot-changed-during-fetch")
        try:
            os.link(candidate, output)
        except FileExistsError:
            blocked("lab2-output-already-exists")
        except OSError:
            blocked("lab2-output-publish-failed")
        return report


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--fetch", action="store_true",
                        help="Explicitly permit one read-only Lab2 snapshot fetch")
    parser.add_argument("--stage-id", required=True,
                        help="Exact private, verified Lab2 reader-stage ID")
    parser.add_argument("--reader-pid", required=True, type=int)
    parser.add_argument("--reader-start-ticks", required=True, type=int)
    parser.add_argument("--game-pin", type=Path, default=DEFAULT_GAME_PIN)
    parser.add_argument("--output", type=Path)
    args = parser.parse_args(argv)
    if not args.fetch:
        parser.error("Explicit --fetch required; import or --help never contacts guest.")
    output = args.output or PRIVATE / (
        "lab2-guest-latest-" + uuid.uuid4().hex + ".local.json"
    )
    try:
        check_private_output(output)
        binding = load_binding(args.stage_id, args.game_pin)
        metadata = fetch(args.reader_pid, args.reader_start_ticks,
                         binding, output)
        print(json.dumps({
            "status": "snapshot-verified", "lab": "lab2",
            "machine": "RIFT-LAB-2", "output": str(output),
            **metadata
        }, separators=(",", ":")))
        return 0
    except (FetchBlocked, lab2.Blocked, base.Blocked,
            common.FetchBlocked, host_json.Blocked, OSError):
        # Never stringify arbitrary OS, JSON or VMware exceptions that might
        # contain private config or credential values.
        code = sys.exc_info()[1]
        reason = str(code) if isinstance(code, (
            FetchBlocked, lab2.Blocked, base.Blocked,
            common.FetchBlocked, host_json.Blocked
        )) else "host-snapshot-io-failed"
        print(json.dumps({"status": "blocked", "lab": "lab2", "reason": reason}))
        return 2
    except BaseException:
        print(json.dumps({"status": "failed", "lab": "lab2",
                          "reason": "unexpected-error"}))
        return 1


if __name__ == "__main__":
    sys.exit(main())
