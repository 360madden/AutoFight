"""Explicit, read-only-first AutoFight Lab2 GuestReader launch via cached VMware GuestOps.

This is NOT a reader deployer. It assumes a separately installed and independently
verified two-file Lab2 GuestReader plus six existing addon files. An operator
must issue --start --confirm START-LAB2 with a current exact Lab2 game pin.
Only the GuestReader --live process may be started; the game, addon, VM, and
input devices are never changed. Never automatically retry an uncertain launch.
"""
from __future__ import annotations

import argparse
import json
from pathlib import Path
import re
import sys
import tempfile
import uuid

import lab2_guest_reader_deploy as deploy

ROOT = Path(__file__).resolve().parents[1]
PRIVATE = ROOT / "artifacts" / "local"
START_SCRIPT = ROOT / "scripts" / "start_lab2_guest_reader.ps1"
DEFAULT_PIN = PRIVATE / "lab2-game-identity.local.json"
START_RECEIPT_MAX = 16384
HEX32 = re.compile(r"[0-9a-f]{32}\Z", re.ASCII)


class StartBlocked(deploy.Blocked):
    """A launch may have happened even if VMware GuestOps returned failure."""

    def __init__(self, reason: str, stage_id: str,
                 *, state: str = "unknown-reader-start",
                 may_have_started: bool | None = None,
                 reader_pid: int | None = None) -> None:
        super().__init__(reason)
        self.stage_id = stage_id
        self.state = state
        self.may_have_started = may_have_started
        self.reader_pid = reader_pid


def validated_local_inputs(stage_id: str, guest_config_path: Path,
                           package: Path, game_pin_path: Path) -> tuple[dict, dict, list, list]:
    if HEX32.fullmatch(stage_id) is None:
        deploy.fail("stage-id-invalid")
    if (game_pin_path.name != DEFAULT_PIN.name or
            not deploy.same_private_root(game_pin_path)):
        deploy.fail("lab2-private-game-pin-required")
    _, files, addons = deploy.validate_host_inputs(guest_config_path, package)
    pin = deploy.parse_json(deploy.regular_bytes(game_pin_path, 4096))
    deploy.validate_game_pin(pin)
    stage_receipt = PRIVATE / ("lab2-stage-" + stage_id + ".local.json")
    record = deploy.parse_json(deploy.regular_bytes(stage_receipt, 16000), limit=16000)
    keys = {"schemaVersion", "kind", "stageId", "manifestSha256",
            "gamePinSha256", "guestConfigSha256", "readerExeSha256", "addonFiles"}
    if (set(record) != keys or
            type(record["schemaVersion"]) is not int or record["schemaVersion"] != 1 or
            record["kind"] != "autofight-lab2-reader-stage-receipt" or
            record["stageId"] != stage_id or
            type(record["manifestSha256"]) is not str or
            deploy.HEX64.fullmatch(record["manifestSha256"]) is None or
            record["gamePinSha256"] != deploy.base.digest_file(game_pin_path) or
            record["guestConfigSha256"] != files[1]["sha256"] or
            record["readerExeSha256"] != files[0]["sha256"] or
            record["addonFiles"] != addons):
        deploy.fail("lab2-stage-receipt-or-source-drift")
    return pin, record, files, addons


def require_start_script() -> str:
    data = deploy.regular_bytes(START_SCRIPT, 65536)
    return deploy.sha256(data)


def validate_start_receipt(value: object, stage_id: str, run_id: str) -> dict:
    keys = {"schemaVersion", "kind", "stageId", "startRunId", "machine",
            "state", "reason", "launchAttempted", "readerMayHaveStarted",
            "readerProcessId", "readerProcessStartUtcTicks",
            "noAddonWrite", "noGameInput", "noGameReload", "noVmLifecycleChange"}
    if type(value) is not dict or set(value) != keys:
        deploy.fail("lab2-reader-start-receipt-invalid")
    if (type(value["schemaVersion"]) is not int or value["schemaVersion"] != 1 or
            value["kind"] != "autofight-lab2-reader-start" or
            value["stageId"] != stage_id or value["startRunId"] != run_id or
            value["machine"] != "RIFT-LAB-2" or
            type(value["launchAttempted"]) is not bool or
            type(value["readerMayHaveStarted"]) is not bool or
            type(value["readerProcessId"]) is not int or
            type(value["readerProcessStartUtcTicks"]) is not int or
            type(value["reason"]) is not str or len(value["reason"]) > 128 or
            any(value[key] is not True for key in
                ("noAddonWrite", "noGameInput", "noGameReload", "noVmLifecycleChange"))):
        deploy.fail("lab2-reader-start-receipt-invalid")
    state = value["state"]
    if state == "reader-launched-not-bound":
        if (value["reason"] != "reader-process-started-no-input" or
                value["launchAttempted"] is not True or
                value["readerMayHaveStarted"] is not True or
                not 0 < value["readerProcessId"] <= 0xffffffff or
                value["readerProcessStartUtcTicks"] <= 0):
            deploy.fail("lab2-reader-start-receipt-inconsistent")
    elif state == "blocked-before-start":
        if (value["reason"] != "preflight-failed" or
                value["launchAttempted"] is not False or
                value["readerMayHaveStarted"] is not False or
                value["readerProcessId"] != 0 or
                value["readerProcessStartUtcTicks"] != 0):
            deploy.fail("lab2-reader-start-receipt-inconsistent")
    elif state == "uncertain-reader-start":
        if (value["reason"] != "start-or-postcheck-failed" or
                value["launchAttempted"] is not True or
                value["readerMayHaveStarted"] is not True or
                value["readerProcessId"] < 0 or
                value["readerProcessStartUtcTicks"] < 0):
            deploy.fail("lab2-reader-start-receipt-inconsistent")
    else:
        deploy.fail("lab2-reader-start-receipt-invalid")
    return value


def verify_installed_reader(g: deploy.base.CachedVmwareGuest,
                            files: list[dict], scratch: Path) -> None:
    # Exact two files, no generated log or any preexisting reader start.
    listing = deploy._extra(g, "listDirectoryInGuest", deploy.GUEST_READER_ROOT)
    lines = [line.strip() for line in listing.splitlines() if line.strip()]
    expected = [deploy.EXE, deploy.GUEST_CONFIG]
    if (len(lines) != 3 or lines[0] != "Directory list: 2" or
            sorted(lines[1:], key=str.casefold) != sorted(expected, key=str.casefold)):
        deploy.fail("lab2-installed-reader-directory-drift")
    for item in files:
        guest_path = deploy.GUEST_READER_ROOT + "\\" + item["relativePath"]
        host_path = scratch / ("installed-" + item["relativePath"])
        g.run("copyFileFromGuestToHost", guest_path, str(host_path), timeout=75)
        data = deploy.regular_bytes(host_path, deploy.READER_MAX)
        if len(data) != item["size"] or deploy.sha256(data) != item["sha256"]:
            deploy.fail("lab2-installed-reader-hash-drift")


def _start(stage_id: str, pin: dict, record: dict, files: list[dict],
           addons: list[dict], guest_config_path: Path, package: Path,
           game_pin_path: Path, launcher_sha: str) -> dict:
    run_id = uuid.uuid4().hex
    guest_stage = deploy.GUEST_STAGE_ROOT + "\\" + stage_id
    guest_launcher = guest_stage + "\\start-reader-" + run_id + ".ps1"
    guest_receipt = guest_stage + "\\reader-start-" + run_id + ".json"
    with tempfile.TemporaryDirectory(prefix="lab2-reader-start-", dir=PRIVATE) as temp:
        temp_path = Path(temp)
        deploy.configure_lab2_transport()
        baseline = deploy.base.collect_identity()
        with deploy.base.CachedVmwareGuest(baseline) as g:
            deploy.verify_guest_addon(g, addons)
            manifest_path = temp_path / "manifest.json"
            g.run("copyFileFromGuestToHost", guest_stage + "\\manifest.json",
                  str(manifest_path), timeout=75)
            if deploy.base.digest_file(manifest_path) != record["manifestSha256"]:
                deploy.fail("lab2-stage-manifest-drift")
            deploy.validate_manifest(
                deploy.parse_json(deploy.regular_bytes(manifest_path, 16384)),
                stage_id, files, addons)
            for item in files:
                copy_path = temp_path / ("stage-" + item["relativePath"])
                g.run("copyFileFromGuestToHost",
                      guest_stage + "\\" + item["relativePath"],
                      str(copy_path), timeout=75)
                data = deploy.regular_bytes(copy_path, deploy.READER_MAX)
                if len(data) != item["size"] or deploy.sha256(data) != item["sha256"]:
                    deploy.fail("lab2-staged-reader-drift")
            verify_installed_reader(g, files, temp_path)
            if (deploy.base.digest_file(game_pin_path) != record["gamePinSha256"] or
                    deploy.base.digest_file(package / deploy.EXE) != files[0]["sha256"] or
                    deploy.base.digest_file(guest_config_path) != files[1]["sha256"]):
                deploy.fail("lab2-reader-start-source-drift")
            if deploy.base.digest_file(START_SCRIPT) != launcher_sha:
                deploy.fail("lab2-reader-launcher-drift")
            g.run("copyFileFromHostToGuest", str(START_SCRIPT),
                  guest_launcher, timeout=75)
            local_launcher = temp_path / "copied-launcher.ps1"
            g.run("copyFileFromGuestToHost", guest_launcher,
                  str(local_launcher), timeout=75)
            if (deploy.base.digest_file(local_launcher) != launcher_sha or
                    deploy.base.digest_file(START_SCRIPT) != launcher_sha):
                deploy.fail("lab2-reader-launcher-copy-drift")
            # From this point a GuestOps failure may still mean a reader launched.
            command_returned = False
            try:
                g.run("runProgramInGuest", deploy.base.POWERSHELL,
                      "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass",
                      "-File", guest_launcher,
                      "-StageId", stage_id, "-StartRunId", run_id,
                      "-ExpectedManifestSha256", record["manifestSha256"],
                      "-ExpectedLauncherSha256", launcher_sha,
                      "-ExpectedReaderExeSha256", files[0]["sha256"],
                      "-ExpectedConfigSha256", files[1]["sha256"],
                      "-ExpectedGameProcessId", str(pin["gameProcessId"]),
                      "-ExpectedGameProcessStartUtcTicks",
                      str(pin["gameProcessStartUtcTicks"]),
                      "-ExpectedGameExecutablePath", pin["gameExecutablePath"],
                      "-ExpectedGameExecutableSha256", pin["gameExecutableSha256"],
                      timeout=180)
                command_returned = True
            except (deploy.base.Blocked, deploy.Blocked):
                pass
            local_receipt = temp_path / "start-result.json"
            try:
                g.run("copyFileFromGuestToHost", guest_receipt,
                      str(local_receipt), timeout=75)
            except (deploy.base.Blocked, deploy.Blocked):
                raise StartBlocked("reader-start-outcome-unavailable", stage_id) from None
        if deploy.base.collect_identity() != baseline:
            raise StartBlocked("lab2-vm-identity-changed-after-start", stage_id)
        try:
            receipt = validate_start_receipt(
                deploy.parse_json(deploy.regular_bytes(local_receipt, START_RECEIPT_MAX),
                                  limit=START_RECEIPT_MAX), stage_id, run_id)
        except deploy.Blocked:
            raise StartBlocked("reader-start-receipt-invalid", stage_id) from None
        if not command_returned or receipt["state"] != "reader-launched-not-bound":
            raise StartBlocked(
                "reader-start-not-confirmed", stage_id, state=receipt["state"],
                may_have_started=receipt["readerMayHaveStarted"],
                reader_pid=receipt["readerProcessId"])
    return {
        "state": "reader-launched-not-bound", "lab": "lab2",
        "stageId": stage_id, "startRunId": run_id,
        "readerProcessId": receipt["readerProcessId"],
        "readerProcessStartUtcTicks": receipt["readerProcessStartUtcTicks"],
        "defaultSnapshotPathInGuest": deploy.GUEST_READER_ROOT + "\\latest.json",
        "gameInput": False, "gameReloaded": False, "addonModified": False,
        "vmLifecycleChanged": False, "autoRetrySafe": False,
    }


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    mode = parser.add_mutually_exclusive_group(required=True)
    mode.add_argument("--preflight", action="store_true",
                      help="Validate host artifacts/pins only; zero VM access")
    mode.add_argument("--start", action="store_true",
                      help="One explicit guest reader-only launch; may run process")
    parser.add_argument("--stage-id", required=True)
    parser.add_argument("--game-pin", type=Path, required=True)
    parser.add_argument("--guest-config", type=Path, default=deploy.DEFAULT_CONFIG)
    parser.add_argument("--package", type=Path, default=deploy.DEFAULT_PACKAGE)
    parser.add_argument("--confirm", choices=["START-LAB2"])
    args = parser.parse_args(argv)
    try:
        pin, record, files, addons = validated_local_inputs(
            args.stage_id, args.guest_config, args.package, args.game_pin)
        launcher_sha = require_start_script()
        if args.preflight:
            result = {"state": "lab2-reader-start-host-ready", "lab": "lab2",
                      "stageId": args.stage_id, "launcherSha256": launcher_sha,
                      "readerExeSha256": files[0]["sha256"],
                      "sourceAddonFiles": len(addons), "guestModified": False,
                      "readerStarted": False}
        else:
            if args.confirm != "START-LAB2":
                deploy.fail("explicit-start-lab2-confirmation-required")
            result = _start(args.stage_id, pin, record, files, addons,
                            args.guest_config, args.package, args.game_pin,
                            launcher_sha)
        print(json.dumps(result, sort_keys=True, separators=(",", ":")))
        return 0
    except StartBlocked as error:
        print(json.dumps({
            "state": error.state, "reason": str(error),
            "lab": "lab2", "stageId": error.stage_id,
            "readerMayHaveStarted": error.may_have_started,
            "readerProcessId": error.reader_pid,
            "autoRetrySafe": False
        }, sort_keys=True))
        return 2
    except (deploy.Blocked, deploy.base.Blocked) as error:
        print(json.dumps({"state": "blocked", "reason": str(error),
                          "autoRetrySafe": False}, sort_keys=True))
        return 2
    except Exception:
        print(json.dumps({"state": "blocked", "reason": "unexpected-reader-start-error",
                          "autoRetrySafe": False}, sort_keys=True))
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
