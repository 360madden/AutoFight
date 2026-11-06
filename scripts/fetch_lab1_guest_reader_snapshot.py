"""Read-only, exact-session Lab 1 GuestReader snapshot acquisition.

Requires a verified launcher-reported reader PID and process start ticks. It
checks guest process/game identities, copies one published snapshot to a unique
private host artifact, checks both identities again, and rejects stale or
mismatched observations. No guest files, game input, VM state, or services are
changed. The output is never an action permit.
"""
from __future__ import annotations

import argparse
from datetime import datetime, timezone
import json
import os
from pathlib import Path
import re
import sys
import tempfile
import uuid

import bootstrap_lab1_host_telemetry as host_bootstrap
import lab1_guest_deploy as deploy

ROOT = Path(__file__).resolve().parents[1]
PRIVATE = ROOT / "artifacts" / "local"
DEFAULT_SNAPSHOT = (
    r"C:\Users\mrkoo\AppData\Local\AutoFight\GuestReader\latest.json"
)
READER_EXE = (
    r"C:\Users\mrkoo\AppData\Local\AutoFight\GuestReader"
    + r"\Rift.Automation.GuestReader.exe"
)
SHA64 = re.compile("[a-f0-9]{64}\\Z")
NONCE32 = re.compile("[a-f0-9]{32}\\Z")


class FetchBlocked(deploy.Blocked):
    """A fail-closed, token-free diagnostic."""


def _blocked(reason: str) -> None:
    raise FetchBlocked(reason)


def require_reader_artifact() -> str:
    items = deploy.validate_package(deploy.DEFAULT_PACKAGE)
    reader = [item for item in items if item.relativePath == deploy.EXE]
    if len(reader) != 1 or SHA64.fullmatch(reader[0].sha256) is None:
        _blocked("reader-artifact-unavailable")
    return reader[0].sha256


def identity_powershell(reader_pid: int, reader_start: int,
                        game_pin: dict, expected_reader_sha: str) -> str:
    """Build a read-only PowerShell assertion. All dynamic values validated."""
    if (type(reader_pid) is not int or not 1 <= reader_pid <= 0xffffffff or
            type(reader_start) is not int or reader_start <= 0 or
            not isinstance(expected_reader_sha, str) or
            SHA64.fullmatch(expected_reader_sha) is None):
        _blocked("reader-process-pin-invalid")
    game_pid = game_pin["gameProcessId"]
    game_start = game_pin["gameProcessStartUtcTicks"]
    game_sha = game_pin["gameExecutableSha256"]
    game_path = game_pin["gameExecutablePath"]
    if (type(game_pid) is not int or game_pid < 1 or
            type(game_start) is not int or game_start < 1 or
            not isinstance(game_sha, str) or SHA64.fullmatch(game_sha) is None or
            not isinstance(game_path, str) or
            re.fullmatch(r"[A-Za-z]:\\[^'\x00\r\n]*\\rift_x64\.exe",
                         game_path, re.I) is None):
        _blocked("game-process-pin-invalid")
    # PowerShell stops at any failed check. No shell metacharacters from
    # untrusted paths, credentials, or nonce are accepted.
    return (
        "$ErrorActionPreference='Stop';"
        "$readers=@(Get-Process -Name 'Rift.Automation.GuestReader' -ErrorAction SilentlyContinue);"
        "if($readers.Count -ne 1){throw 'reader-not-unique';};"
        f"$reader=Get-Process -Id {reader_pid} -ErrorAction Stop;"
        f"if($reader.Id -ne {reader_pid} -or "
        f"$reader.StartTime.ToUniversalTime().Ticks -ne {reader_start})"
        "{throw 'reader-process-identity-mismatch';};"
        "if(-not [string]::Equals($reader.MainModule.FileName,"
        f"'{READER_EXE}',[StringComparison]::OrdinalIgnoreCase))"
        "{throw 'reader-executable-path-mismatch';};"
        "$readerHash=(Get-FileHash -LiteralPath $reader.MainModule.FileName "
        "-Algorithm SHA256).Hash.ToLowerInvariant();"
        f"if($readerHash -ne '{expected_reader_sha}')"
        "{throw 'reader-executable-hash-mismatch';};"
        "$games=@(Get-Process -Name rift_x64 -ErrorAction SilentlyContinue);"
        "if($games.Count -ne 1){throw 'game-not-unique';};"
        f"$game=Get-Process -Id {game_pid} -ErrorAction Stop;"
        f"if($game.Id -ne {game_pid} -or "
        f"$game.StartTime.ToUniversalTime().Ticks -ne {game_start})"
        "{throw 'game-identity-mismatch';};"
        "if(-not [string]::Equals($game.MainModule.FileName,"
        f"'{game_path}',[StringComparison]::OrdinalIgnoreCase))"
        "{throw 'game-path-mismatch';};"
        "$gameHash=(Get-FileHash -LiteralPath $game.MainModule.FileName "
        "-Algorithm SHA256).Hash.ToLowerInvariant();"
        f"if($gameHash -ne '{game_sha}')"
        "{throw 'game-hash-mismatch';};"
        "$other=@(Get-Process -Name 'RiftReader2.Live','RiftReader2.GuestReader' "
        "-ErrorAction SilentlyContinue);"
        "if($other.Count -gt 0){throw 'competing-rr2-reader';};"
        "exit 0"
    )


def verify_snapshot(snapshot: dict, game_pin: dict,
                    *, now: datetime | None = None) -> dict:
    if snapshot.get("schemaVersion") != 1 or snapshot.get("readerStatus") != "running":
        _blocked("reader-not-running")
    if type(snapshot.get("sequence")) is not int or snapshot["sequence"] < 1:
        _blocked("reader-sequence-invalid")
    instance = snapshot.get("instanceId")
    nonce = snapshot.get("nonce")
    try:
        if not isinstance(instance, str) or str(uuid.UUID(instance)) != instance:
            _blocked("reader-instance-invalid")
    except ValueError:
        _blocked("reader-instance-invalid")
    if not isinstance(nonce, str) or NONCE32.fullmatch(nonce) is None:
        _blocked("reader-nonce-invalid")
    stamp = snapshot.get("capturedAtUtc")
    try:
        if not isinstance(stamp, str):
            _blocked("snapshot-time-invalid")
        parsed = datetime.fromisoformat(stamp.replace("Z", "+00:00"))
        if parsed.tzinfo is None:
            _blocked("snapshot-time-invalid")
    except ValueError:
        _blocked("snapshot-time-invalid")
    reference = now or datetime.now(timezone.utc)
    if reference.tzinfo is None:
        _blocked("host-time-invalid")
    age = (reference.astimezone(timezone.utc) -
           parsed.astimezone(timezone.utc)).total_seconds()
    if not -5 <= age <= 30:
        _blocked("snapshot-stale-or-clock-skew")
    target = snapshot.get("target")
    if not isinstance(target, dict):
        _blocked("reader-target-missing")
    compare = {
        "processId": game_pin["gameProcessId"],
        "processStartUtcTicks": game_pin["gameProcessStartUtcTicks"],
        "executablePath": game_pin["gameExecutablePath"],
        "executableSha256": game_pin["gameExecutableSha256"],
        "readerSessionNonce": nonce,
    }
    for key, expected in compare.items():
        actual = target.get(key)
        if key in ("processId", "processStartUtcTicks"):
            if type(actual) is not int or actual != expected:
                _blocked("snapshot-game-identity-mismatch")
        elif (not isinstance(actual, str) or
              actual.casefold() != expected.casefold()):
            _blocked("snapshot-game-or-reader-identity-mismatch")
    return {
        "readerInstanceId": instance,
        "readerNonce": nonce,
        "sequence": snapshot["sequence"],
        "capturedAtUtc": stamp,
        "gameProcessId": game_pin["gameProcessId"],
    }


def fetch(reader_pid: int, reader_start: int,
          game_pin: dict, expected_reader_sha: str, output: Path) -> dict:
    if output.parent.resolve(strict=True) != PRIVATE.resolve(strict=True):
        _blocked("output-not-private")
    if output.exists() or output.is_symlink() or not output.name.endswith(".local.json"):
        _blocked("output-already-exists-or-not-private")
    gate = identity_powershell(reader_pid, reader_start,
                               game_pin, expected_reader_sha)
    baseline = deploy.collect_identity()
    with tempfile.TemporaryDirectory(prefix="guest-reader-snapshot-", dir=PRIVATE) as temp:
        candidate = Path(temp) / "latest.json"
        with deploy.CachedVmwareGuest(baseline) as guest:
            guest.run("runProgramInGuest", deploy.POWERSHELL,
                      "-NoProfile", "-NonInteractive", "-Command", gate,
                      timeout=90)
            guest.run("copyFileFromGuestToHost", DEFAULT_SNAPSHOT,
                      str(candidate), timeout=60)
            metadata = verify_snapshot(
                host_bootstrap.read_json(candidate, 128 * 1024), game_pin)
            guest.run("runProgramInGuest", deploy.POWERSHELL,
                      "-NoProfile", "-NonInteractive", "-Command", gate,
                      timeout=90)
        if deploy.collect_identity() != baseline:
            _blocked("vm-identity-changed")
        try:
            os.link(candidate, output)
        except FileExistsError:
            _blocked("output-already-exists")
        except OSError:
            _blocked("snapshot-output-write-failed")
        return metadata


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--reader-pid", required=True, type=int)
    parser.add_argument("--reader-start-ticks", required=True, type=int)
    parser.add_argument("--game-pin", type=Path, default=deploy.DEFAULT_GAME_IDENTITY)
    parser.add_argument("--output", type=Path)
    parser.add_argument("--fetch", action="store_true",
                        help="Read the single current Lab1 snapshot via pinned VMware.")
    opts = parser.parse_args(argv)
    if not opts.fetch:
        parser.error("Explicit --fetch required for guest snapshot access.")
    output = opts.output or (PRIVATE / ("lab1-guest-latest-" + uuid.uuid4().hex + ".local.json"))
    try:
        pin = deploy.load_game_identity(opts.game_pin)
        reader_sha = require_reader_artifact()
        result = fetch(opts.reader_pid, opts.reader_start_ticks,
                       pin, reader_sha, output)
        print(json.dumps({"status": "snapshot-verified", "lab": "lab1",
                          "output": str(output), **result},
                         separators=(",", ":")))
        return 0
    except (deploy.Blocked, host_bootstrap.Blocked, OSError) as error:
        reason = (str(error) if isinstance(error, (deploy.Blocked, host_bootstrap.Blocked))
                  else "host-snapshot-io-failed")
        print(json.dumps({"status": "blocked", "reason": reason}))
        return 2
    except BaseException:
        print(json.dumps({"status": "failed", "reason": "unexpected-error"}))
        return 1


if __name__ == "__main__":
    sys.exit(main())
