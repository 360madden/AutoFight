"""AutoFight-owned, explicit Lab1 GuestReader --live start via cached VMware tools.

No VMware guest access in --preflight. --start is a controlled one-shot:
validate source hashes and exact game pin, verify inactive install-stage manifest,
copy a hash-pinned launcher to that stage, then request only GuestReader --live.
There is no RIFT chat/key input, UI reload, game restart, or VM lifecycle command.
A failed or ambiguous launch MUST NOT be automatically retried.
"""
from __future__ import annotations

import argparse
import json
from pathlib import Path
import re
import sys
import tempfile
import uuid

import lab1_guest_deploy as deploy

ROOT = Path(__file__).resolve().parents[1]
START_SCRIPT = ROOT / "scripts" / "start_lab1_guest_reader.ps1"
DEFAULT_PACKAGE = deploy.DEFAULT_PACKAGE
DEFAULT_GAME_IDENTITY = deploy.DEFAULT_GAME_IDENTITY
GUEST_READER_ROOT = r"C:\Users\mrkoo\AppData\Local\AutoFight\GuestReader"
MAX_RECEIPT_BYTES = 16 * 1024
STAGE_ID = re.compile(r"[a-f0-9]{32}", re.ASCII)


class StartBlocked(deploy.Blocked):
    """Guest outcome may be unknown; never infer that no reader was started."""

    def __init__(
        self, reason: str, stage_id: str, *,
        state: str = "unknown-reader-start",
        may_have_started: bool | None = None,
        reader_pid: int | None = None
    ) -> None:
        super().__init__(reason)
        self.stage_id = stage_id
        self.state = state
        self.may_have_started = may_have_started
        self.reader_pid = reader_pid


def validate_start_receipt(data: object, stage_id: str, run_id: str) -> dict:
    """Accept no partial, extra, duplicate, or wrong-machine launch receipts."""
    allowed = {
        "schemaVersion", "kind", "stageId", "startRunId", "machine",
        "state", "reason", "launchAttempted", "readerMayHaveStarted",
        "readerProcessId", "readerProcessStartUtcTicks", "gameInput",
        "gameReloaded", "vmLifecycleChanged",
    }
    if not isinstance(data, dict) or set(data) != allowed:
        raise deploy.Blocked("reader-start-receipt-invalid")
    if (type(data["schemaVersion"]) is not int or data["schemaVersion"] != 1
            or data["kind"] != "autofight-lab1-reader-start"
            or data["stageId"] != stage_id or data["startRunId"] != run_id
            or data["machine"] != "RIFT-LAB-1"
            or data["gameInput"] is not False
            or data["gameReloaded"] is not False
            or data["vmLifecycleChanged"] is not False
            or type(data["launchAttempted"]) is not bool
            or type(data["readerMayHaveStarted"]) is not bool
            or type(data["readerProcessId"]) is not int
            or type(data["readerProcessStartUtcTicks"]) is not int
            or not isinstance(data["reason"], str)):
        raise deploy.Blocked("reader-start-receipt-invalid")
    state = data["state"]
    if state == "reader-launched-not-bound":
        if (data["launchAttempted"] is not True or
                data["readerMayHaveStarted"] is not True or
                data["reason"] != "reader-process-started-no-input" or
                not 0 < data["readerProcessId"] <= 0xffffffff or
                data["readerProcessStartUtcTicks"] <= 0):
            raise deploy.Blocked("reader-start-receipt-inconsistent")
    elif state == "blocked-before-start":
        if (data["launchAttempted"] is not False or
                data["readerMayHaveStarted"] is not False or
                data["readerProcessId"] != 0 or
                data["readerProcessStartUtcTicks"] != 0):
            raise deploy.Blocked("reader-start-receipt-inconsistent")
    elif state == "uncertain-reader-start":
        if (data["launchAttempted"] is not True or
                data["readerMayHaveStarted"] is not True or
                data["readerProcessId"] < 0 or
                data["readerProcessStartUtcTicks"] < 0):
            raise deploy.Blocked("reader-start-receipt-inconsistent")
    else:
        raise deploy.Blocked("reader-start-receipt-invalid")
    return data


def require_safe_start_script() -> str:
    try:
        if (not START_SCRIPT.is_file() or START_SCRIPT.is_symlink() or
                START_SCRIPT.stat().st_size > 32 * 1024):
            raise ValueError()
        return deploy.digest_file(START_SCRIPT)
    except (OSError, ValueError):
        raise deploy.Blocked("reader-launcher-unavailable") from None


def _start_impl(
    package: Path, files: tuple[deploy.PackageItem, ...],
    identity: dict, stage_id: str, run_id: str
) -> dict:
    stage_root = deploy.GUEST_STAGE_PARENT + "\\" + stage_id
    guest_manifest = stage_root + r"\manifest.json"
    guest_script = stage_root + "\\start-reader-" + run_id + ".ps1"
    guest_receipt = stage_root + "\\reader-start-" + run_id + ".json"
    launcher_hash = require_safe_start_script()

    # A private temporary host destination; no token-containing config is read.
    with tempfile.TemporaryDirectory(
        prefix="reader-start-", dir=ROOT / "artifacts" / "local"
    ) as temporary:
        scratch = Path(temporary)
        manifest = scratch / "manifest.json"
        receipt_file = scratch / "reader-start-result.json"
        baseline = deploy.collect_identity()
        with deploy.CachedVmwareGuest(baseline) as guest:
            guest.run("copyFileFromGuestToHost", guest_manifest,
                      str(manifest), timeout=60)
            staged = deploy._read_json(manifest, 16 * 1024)
            deploy.validate_staged_manifest(staged, stage_id, files)
            manifest_hash = deploy.digest_file(manifest)

            # Unique script target inside verified inactive UUID stage.
            # The guest script checks its own hash before process launch.
            if deploy.digest_file(START_SCRIPT) != launcher_hash:
                raise deploy.Blocked("reader-launcher-changed")
            guest.run("copyFileFromHostToGuest", str(START_SCRIPT),
                      guest_script, timeout=60)
            if deploy.digest_file(START_SCRIPT) != launcher_hash:
                raise deploy.Blocked("reader-launcher-changed")

            # After this call fails/times out, the process may already be live.
            # Always try the unique receipt, then leave resolution to operator.
            command_returned = False
            try:
                guest.run(
                    "runProgramInGuest", deploy.POWERSHELL,
                    "-NoProfile", "-NonInteractive", "-ExecutionPolicy",
                    "Bypass", "-File", guest_script,
                    "-StageId", stage_id, "-StartRunId", run_id,
                    "-ExpectedManifestSha256", manifest_hash,
                    "-ExpectedLauncherSha256", launcher_hash,
                    "-ExpectedGameProcessId", str(identity["gameProcessId"]),
                    "-ExpectedGameProcessStartUtcTicks",
                    str(identity["gameProcessStartUtcTicks"]),
                    "-ExpectedGameExecutablePath", identity["gameExecutablePath"],
                    "-ExpectedGameExecutableSha256",
                    identity["gameExecutableSha256"],
                    timeout=150,
                )
                command_returned = True
            except deploy.Blocked:
                pass
            try:
                guest.run("copyFileFromGuestToHost", guest_receipt,
                          str(receipt_file), timeout=60)
            except deploy.Blocked:
                raise StartBlocked(
                    "reader-start-outcome-unavailable", stage_id
                ) from None

        try:
            report = validate_start_receipt(
                deploy._read_json(receipt_file, MAX_RECEIPT_BYTES),
                stage_id, run_id
            )
        except deploy.Blocked:
            raise StartBlocked("reader-start-receipt-invalid", stage_id) from None
        if not command_returned or report["state"] != "reader-launched-not-bound":
            raise StartBlocked(
                "reader-start-not-confirmed", stage_id,
                state=report["state"],
                may_have_started=report["readerMayHaveStarted"],
                reader_pid=report["readerProcessId"],
            )
        return {
            "state": "reader-launched-not-bound", "lab": "lab1",
            "stageId": stage_id, "startRunId": run_id,
            "readerProcessId": report["readerProcessId"],
            "readerProcessStartUtcTicks": report["readerProcessStartUtcTicks"],
            "defaultSnapshotPathInGuest": GUEST_READER_ROOT + r"\latest.json",
            "gameInput": False, "gameReloaded": False,
            "vmLifecycleChanged": False, "autoRetrySafe": False,
        }


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    mode = parser.add_mutually_exclusive_group(required=True)
    mode.add_argument("--preflight", action="store_true",
                      help="Host-only package/script/pin checks; no guest access")
    mode.add_argument("--start", action="store_true",
                      help="One explicit, pinned Lab1 read-only reader launch")
    parser.add_argument("--package", type=Path, default=DEFAULT_PACKAGE)
    parser.add_argument("--identity", type=Path, default=DEFAULT_GAME_IDENTITY)
    parser.add_argument("--stage-id")
    parser.add_argument("--confirm", choices=["START-LAB1"])
    opts = parser.parse_args(argv)
    if opts.start:
        if (opts.confirm != "START-LAB1" or
                not isinstance(opts.stage_id, str) or
                STAGE_ID.fullmatch(opts.stage_id) is None):
            parser.error("--start requires --confirm START-LAB1 and 32hex --stage-id")
    elif opts.confirm is not None or opts.stage_id is not None:
        parser.error("--preflight rejects --confirm and --stage-id")
    try:
        files = deploy.validate_package(opts.package)
        if len(files) != 8:
            raise deploy.Blocked("private-guest-config-required")
        identity = deploy.load_game_identity(opts.identity)
        script_hash = require_safe_start_script()
        if opts.preflight:
            result = {
                "state": "offline-reader-launch-preflight-passed", "lab": "lab1",
                "fileCount": len(files), "readerExeSha256": next(
                    item.sha256 for item in files if item.relativePath == deploy.EXE),
                "launcherSha256": script_hash,
                "vmConnected": False, "readerStarted": False, "gameInput": False,
            }
        else:
            try:
                result = _start_impl(
                    opts.package.resolve(strict=True), files,
                    identity, opts.stage_id, uuid.uuid4().hex
                )
            except StartBlocked:
                raise
            except Exception:
                # Even an unreturned VMware operation may have started the reader.
                raise StartBlocked("reader-start-outcome-unavailable",
                                   opts.stage_id) from None
        print(json.dumps(result, separators=(",", ":")))
        return 0
    except (deploy.Blocked, OSError) as error:
        reason = str(error) if isinstance(error, deploy.Blocked) else "host-io-error"
        result = {
            "state": "blocked", "lab": "lab1", "reason": reason,
            "noAutomaticRetry": True,
        }
        if isinstance(error, StartBlocked):
            result.update({
                "state": error.state, "stageId": error.stage_id,
                "readerMayHaveStarted": error.may_have_started,
                "readerProcessId": error.reader_pid,
                "manualReviewRequired": True,
            })
        print(json.dumps(result, separators=(",", ":")))
        return 2


if __name__ == "__main__":
    sys.exit(main())
