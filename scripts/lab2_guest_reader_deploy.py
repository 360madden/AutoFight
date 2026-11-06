"""AutoFight-owned Lab 2 GuestReader-only deployment.

Lab 2's six addon files are already installed and must remain untouched.
This operator-invoked host tool uses cached VMware GuestOps credentials, with
pinned VM identity, to audit, stage and separately install ONLY a GuestReader
EXE plus private delivery configuration. No reader launch, RIFT UI input,
reload, game/VM lifecycle operation, or RR2 runtime dependency.
"""
from __future__ import annotations

import argparse
from dataclasses import asdict
import hashlib
import ipaddress
import json
import os
from pathlib import Path
import re
import stat
import subprocess
import sys
import tempfile
from urllib.parse import urlsplit
import uuid

import lab1_guest_deploy as base

ROOT = Path(__file__).resolve().parents[1]
PRIVATE_ROOT = ROOT / "artifacts" / "local"
DEFAULT_PACKAGE = PRIVATE_ROOT / "autofight-guest-live-20261105"
DEFAULT_CONFIG = PRIVATE_ROOT / "lab2-guest-reader.local.json"
INSTALL_SCRIPT = ROOT / "scripts" / "install_lab2_guest_reader.ps1"
VMX = Path(r"C:\rift modding\VMs\RIFT-Lab-2\RIFT-Lab.vmx")
GUEST_ROOT = r"C:\Users\mrkoo\AppData\Local\AutoFight"
GUEST_STAGE_ROOT = GUEST_ROOT + r"\staging"
GUEST_READER_ROOT = GUEST_ROOT + r"\GuestReader"
GUEST_ADDON = r"C:\Users\mrkoo\OneDrive\Documents\RIFT\Interface\AddOns\AutoFight"
EXE = "Rift.Automation.GuestReader.exe"
GUEST_CONFIG = "autofight-guest-reader.json"
ADDON_NAMES = ("RiftAddon.toc", "wire.lua", "producer.lua",
               "live.lua", "guidance.lua", "main.lua")
HEX32 = re.compile(r"[0-9a-f]{32}\Z", re.ASCII)
HEX64 = re.compile(r"[0-9a-f]{64}\Z", re.ASCII)
READER_MAX = 256 * 1024 * 1024
FILE_ATTRIBUTE_REPARSE_POINT = 0x400


class Blocked(RuntimeError):
    """Redacted, stable operator-visible error without guest secrets."""


def fail(reason: str):
    raise Blocked(reason)


def same_private_root(path: Path) -> bool:
    try:
        return path.parent.resolve(strict=True) == PRIVATE_ROOT.resolve(strict=True)
    except OSError:
        return False


def regular_bytes(path: Path, maximum: int) -> bytes:
    try:
        if path.is_symlink() or path.is_junction():
            fail("input-link-rejected")
        info = path.lstat()
        if (not stat.S_ISREG(info.st_mode) or
                info.st_size < 1 or info.st_size > maximum or
                getattr(info, "st_file_attributes", 0) & FILE_ATTRIBUTE_REPARSE_POINT):
            fail("input-file-invalid")
        with path.open("rb") as stream:
            data = stream.read(maximum + 1)
        if len(data) != info.st_size:
            fail("input-file-changed")
        return data
    except OSError:
        fail("input-file-unavailable")


def sha256(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def unique_object(pairs: list[tuple[str, object]]) -> dict:
    result = {}
    seen = set()
    for key, value in pairs:
        if key.casefold() in seen:
            fail("duplicate-json-key")
        result[key] = value
        seen.add(key.casefold())
    return result


def parse_json(data: bytes, *, limit: int = 65536) -> dict:
    if len(data) > limit:
        fail("json-too-large")
    try:
        value = json.loads(data.decode("utf-8-sig"),
                           object_pairs_hook=unique_object,
                           parse_constant=lambda _: fail("json-nonfinite-number"))
    except (ValueError, UnicodeError):
        fail("json-invalid")
    if type(value) is not dict:
        fail("json-root-invalid")
    return value


def validate_guest_config(value: dict) -> None:
    wanted = {"schemaVersion", "processName", "durationSeconds",
              "pollIntervalMilliseconds", "maxScanBytes", "maxScanSeconds",
              "outputPath", "freshnessTimeoutSeconds", "delivery"}
    if set(value) != wanted or type(value["schemaVersion"]) is not int or value["schemaVersion"] != 1:
        fail("guest-config-schema-invalid")
    if value["processName"] != "rift_x64":
        fail("guest-config-process-invalid")
    ranges = (("durationSeconds", 1, 3600),
              ("pollIntervalMilliseconds", 250, 60000),
              ("maxScanBytes", 1, 8 * 1024**3),
              ("maxScanSeconds", 1, 60),
              ("freshnessTimeoutSeconds", 1, 60))
    for key, lower, upper in ranges:
        if type(value[key]) is not int or not lower <= value[key] <= upper:
            fail("guest-config-range-invalid")
    if value["outputPath"] is not None:
        fail("guest-config-output-must-use-default")
    delivery = value["delivery"]
    if type(delivery) is not dict or set(delivery) != {"endpoint", "token"}:
        fail("guest-config-delivery-invalid")
    if delivery["endpoint"] != "http://192.168.56.1:48761/observation":
        fail("guest-config-endpoint-invalid")
    token = delivery["token"]
    if (type(token) is not str or len(token) != 64 or
            not token.isascii() or any(not 33 <= ord(char) <= 126 for char in token)):
        fail("guest-config-token-invalid")


def valid_game_executable_path(value: object) -> bool:
    """Allow ordinary absolute Windows paths, never shell syntax or device paths."""
    if type(value) is not str or not 15 <= len(value) <= 512:
        return False
    if re.fullmatch(
        r"[A-Za-z]:\\(?:[A-Za-z0-9_ ().+-]+\\)+rift_x64\.exe",
        value, re.I | re.ASCII
    ) is None:
        return False
    # No traversal, DOS device names or Windows trailing-dot/space aliases.
    reserved = {"CON", "PRN", "AUX", "NUL"}
    reserved.update(f"{prefix}{number}" for prefix in ("COM", "LPT")
                    for number in range(1, 10))
    segments = value[3:].split("\\")
    for segment in segments:
        if (segment in {".", ".."} or segment[-1] in " ." or
                segment.partition(".")[0].upper() in reserved):
            return False
    return True


def validate_game_pin(value: dict) -> None:
    if (set(value) != {"schemaVersion", "lab", "machine", "gameProcessId",
                       "gameProcessStartUtcTicks", "gameExecutablePath",
                       "gameExecutableSha256"} or
            type(value["schemaVersion"]) is not int or value["schemaVersion"] != 1 or
            value["lab"] != "lab2" or value["machine"] != "RIFT-LAB-2" or
            type(value["gameProcessId"]) is not int or
            not 0 < value["gameProcessId"] <= 0xffffffff or
            type(value["gameProcessStartUtcTicks"]) is not int or
            value["gameProcessStartUtcTicks"] <= 0 or
            not valid_game_executable_path(value["gameExecutablePath"]) or
            type(value["gameExecutableSha256"]) is not str or
            HEX64.fullmatch(value["gameExecutableSha256"]) is None):
        fail("lab2-game-pin-invalid")


def validate_host_inputs(guest_config_path: Path, package: Path) -> tuple[dict, list[dict], list[dict]]:
    if guest_config_path.name != DEFAULT_CONFIG.name or not same_private_root(guest_config_path):
        fail("guest-config-not-lab2-private")
    guest_bytes = regular_bytes(guest_config_path, 65536)
    config = parse_json(guest_bytes)
    validate_guest_config(config)
    package_dir = package.resolve(strict=True)
    if package_dir.parent != PRIVATE_ROOT.resolve(strict=True) or package.is_symlink() or package.is_junction():
        fail("package-outside-ignored-root")
    exe_bytes = regular_bytes(package_dir / EXE, READER_MAX)
    files = [
        {"relativePath": EXE, "sha256": sha256(exe_bytes), "size": len(exe_bytes)},
        {"relativePath": GUEST_CONFIG, "sha256": sha256(guest_bytes), "size": len(guest_bytes)}
    ]
    addons = []
    for name in ADDON_NAMES:
        item = regular_bytes(ROOT / "addon" / "AutoFight" / name, 1024 * 1024)
        addons.append({"relativePath": name, "sha256": sha256(item), "size": len(item)})
    return config, files, addons


def validate_manifest(obj: dict, stage_id: str, files: list[dict], addons: list[dict]) -> None:
    if (set(obj) != {"schemaVersion", "kind", "stageId", "files", "addonFiles"} or
            type(obj["schemaVersion"]) is not int or obj["schemaVersion"] != 1 or
            obj["kind"] != "autofight-lab2-reader-stage" or
            obj["stageId"] != stage_id or
            type(obj["files"]) is not list or type(obj["addonFiles"]) is not list or
            obj["files"] != files or obj["addonFiles"] != addons):
        fail("lab2-stage-manifest-invalid")


def validate_install_receipt(value: dict, stage_id: str, run_id: str) -> dict:
    keys = {"schemaVersion", "kind", "stageId", "installRunId", "machine",
            "state", "reason", "readerPromoted", "addonUntouched", "readerFinal",
            "readerPending", "noReaderStart", "noGameReload", "noGameInput",
            "noVmLifecycleChange"}
    if type(value) is not dict or set(value) != keys:
        fail("lab2-install-receipt-invalid")
    if (type(value["schemaVersion"]) is not int or value["schemaVersion"] != 1 or
            value["kind"] != "autofight-lab2-reader-install" or
            value["stageId"] != stage_id or value["installRunId"] != run_id or
            value["machine"] != "RIFT-LAB-2" or
            value["state"] not in {"installed-not-running", "blocked-no-final-install",
                                   "partial-install-needs-review"} or
            type(value["reason"]) is not str or len(value["reason"]) > 128 or
            type(value["readerPromoted"]) is not bool or
            value["addonUntouched"] is not True or
            value["readerFinal"] != GUEST_READER_ROOT or
            value["readerPending"] != GUEST_ROOT + "\\.GuestReader-" + stage_id + ".pending" or
            any(value[key] is not True for key in
                ("noReaderStart", "noGameReload", "noGameInput", "noVmLifecycleChange"))):
        fail("lab2-install-receipt-invalid")
    if (value["state"] == "installed-not-running" and
            value["reason"] != "verified-reader-installed-addon-untouched"):
        fail("lab2-install-success-reason-invalid")
    if ((value["state"] == "installed-not-running") != value["readerPromoted"]):
        if value["state"] != "partial-install-needs-review":
            fail("lab2-install-receipt-inconsistent")
    return value


def configure_lab2_transport() -> None:
    """Adapt only AutoFight-owned reusable VMware helper constants in this process."""
    base.VMX = VMX
    base.VM_CONSOLE_PORT = 5962
    base.VM_PASSWORD_TARGET = "VMware Encrypted VM: " + str(VMX)
    base.GUEST_PASSWORD_TARGET = "RiftReader2 Guest Windows: " + str(VMX)
    # Fail closed when exact Lab2 guest entry is absent, never borrow Lab1's.
    base.LEGACY_GUEST_TARGET = base.GUEST_PASSWORD_TARGET


def _extra(g: base.CachedVmwareGuest, command: str, path: str) -> str:
    if command != "listDirectoryInGuest":
        fail("vmrun-extra-command-disallowed")
    g._require_identity()
    argv = [str(base.VMRUN), "-T", "ws", "-vp", g.vm_password.value,
            "-gu", g.username.value, "-gp", g.guest_password.value,
            command, str(VMX), path]
    try:
        result = subprocess.run(argv, capture_output=True, text=True,
                                timeout=65, creationflags=subprocess.CREATE_NO_WINDOW,
                                check=False)
    except (OSError, subprocess.SubprocessError):
        fail("vmrun-extra-unavailable")
    finally:
        for index in (4, 6, 8):
            argv[index] = "[cleared]"
    g._require_identity()
    if result.returncode:
        fail("lab2-addon-list-unavailable")
    return result.stdout


def verify_guest_addon(g: base.CachedVmwareGuest, expected: list[dict]) -> None:
    listing = _extra(g, "listDirectoryInGuest", GUEST_ADDON)
    actual = [line.strip() for line in listing.splitlines() if line.strip()]
    if actual != ["Directory list: 6", *sorted(ADDON_NAMES, key=str.casefold)]:
        # Ignore vmrun's display ordering only, never unexpected entries.
        if not actual or actual[0] != "Directory list: 6" or sorted(actual[1:], key=str.casefold) != sorted(ADDON_NAMES, key=str.casefold):
            fail("lab2-addon-directory-drift")
    with tempfile.TemporaryDirectory(prefix="lab2-addon-verify-", dir=PRIVATE_ROOT) as temp:
        for item in expected:
            local = Path(temp) / item["relativePath"]
            g.run("copyFileFromGuestToHost",
                  GUEST_ADDON + "\\" + item["relativePath"], str(local), timeout=75)
            data = regular_bytes(local, 1024 * 1024)
            if len(data) != item["size"] or sha256(data) != item["sha256"]:
                fail("lab2-addon-hash-drift")


def _write_absent(path: Path, data: dict):
    if not same_private_root(path) or not path.name.endswith(".local.json") or path.exists():
        fail("private-receipt-path-invalid")
    blob = (json.dumps(data, sort_keys=True, separators=(",", ":")) + "\n").encode("utf-8")
    if len(blob) > 16000:
        fail("private-receipt-too-large")
    # x prevents overwriting an existing run/record.
    with path.open("xb") as stream:
        stream.write(blob)
        stream.flush()
        os.fsync(stream.fileno())


def _stage(guest_config_path: Path, package: Path, pin_path: Path) -> dict:
    _, files, addon_files = validate_host_inputs(guest_config_path, package)
    pin_bytes = regular_bytes(pin_path, 4096)
    pin_sha = sha256(pin_bytes)
    pin = parse_json(pin_bytes)
    validate_game_pin(pin)
    stage_id = uuid.uuid4().hex
    guest_stage = GUEST_STAGE_ROOT + "\\" + stage_id
    manifest = {"schemaVersion": 1, "kind": "autofight-lab2-reader-stage",
                "stageId": stage_id, "files": files, "addonFiles": addon_files}
    with tempfile.TemporaryDirectory(prefix="lab2-reader-stage-", dir=PRIVATE_ROOT) as temp:
        temp_path = Path(temp)
        manifest_file = temp_path / "manifest.json"
        manifest_file.write_text(json.dumps(manifest, separators=(",", ":")), encoding="utf-8")
        manifest_sha = base.digest_file(manifest_file)
        configure_lab2_transport()
        baseline = base.collect_identity()
        with base.CachedVmwareGuest(baseline) as g:
            verify_guest_addon(g, addon_files)
            cmd = ("$ErrorActionPreference='Stop';"
                   "if([Environment]::MachineName -cne 'RIFT-LAB-2'){throw 'wrong-machine'};"
                   "$parents=@('C:\\Users','C:\\Users\\mrkoo','C:\\Users\\mrkoo\\AppData','C:\\Users\\mrkoo\\AppData\\Local');"
                   "foreach($q in $parents){$d=Get-Item -LiteralPath $q -Force -ErrorAction Stop;"
                   "if((-not $d.PSIsContainer) -or ($d.Attributes -band [IO.FileAttributes]::ReparsePoint)){throw 'invalid-parent'}};"
                   "$p='" + GUEST_STAGE_ROOT + "';"
                   "$r='" + GUEST_ROOT + "';"
                   "if(-not(Test-Path -LiteralPath $r)){[void](New-Item -ItemType Directory -Path $r -ErrorAction Stop)};"
                   "if(-not(Test-Path -LiteralPath $p)){[void](New-Item -ItemType Directory -Path $p -ErrorAction Stop)};"
                   "foreach($q in @($r,$p)){$d=Get-Item -LiteralPath $q -Force -ErrorAction Stop;"
                   "if((-not $d.PSIsContainer) -or ($d.Attributes -band [IO.FileAttributes]::ReparsePoint)){throw 'invalid-stage-parent'}};"
                   "if(Test-Path -LiteralPath '" + guest_stage + "'){throw 'stage-exists'};"
                   "[void](New-Item -ItemType Directory -Path '" + guest_stage + "' -ErrorAction Stop)")
            g.run("runProgramInGuest", base.POWERSHELL, "-NoProfile", "-NonInteractive",
                  "-Command", cmd, timeout=90)
            inputs = ((package / EXE, EXE),
                      (guest_config_path, GUEST_CONFIG),
                      (manifest_file, "manifest.json"))
            pinned = {EXE: files[0]["sha256"], GUEST_CONFIG: files[1]["sha256"],
                      "manifest.json": manifest_sha}
            for source, name in inputs:
                if base.digest_file(source) != pinned[name]:
                    fail("lab2-stage-source-changed-before-copy")
                remote = guest_stage + "\\" + name
                g.run("copyFileFromHostToGuest", str(source), remote, timeout=240)
                copied = temp_path / ("copy-" + name)
                g.run("copyFileFromGuestToHost", remote, str(copied), timeout=75)
                if base.digest_file(copied) != pinned[name]:
                    fail("lab2-stage-copy-hash-mismatch")
                if base.digest_file(source) != pinned[name]:
                    fail("lab2-stage-source-changed-during-copy")
        for addon in addon_files:
            source = ROOT / "addon" / "AutoFight" / addon["relativePath"]
            if base.digest_file(source) != addon["sha256"]:
                fail("lab2-stage-addon-source-changed")
        if base.digest_file(pin_path) != pin_sha:
            fail("lab2-stage-game-pin-changed")
        receipt = {"schemaVersion": 1, "kind": "autofight-lab2-reader-stage-receipt",
                   "stageId": stage_id, "manifestSha256": manifest_sha,
                   "gamePinSha256": pin_sha,
                   "guestConfigSha256": files[1]["sha256"],
                   "readerExeSha256": files[0]["sha256"],
                   "addonFiles": addon_files}
        _write_absent(PRIVATE_ROOT / ("lab2-stage-" + stage_id + ".local.json"), receipt)
    return {"state": "verified-staged-only", "lab": "lab2",
            "stageId": stage_id, "guestReaderStarted": False,
            "addonModified": False, "gameReloaded": False}


def _install(stage_id: str, guest_config_path: Path, package: Path,
             pin_path: Path) -> dict:
    if HEX32.fullmatch(stage_id) is None:
        fail("stage-id-invalid")
    _, files, addon_files = validate_host_inputs(guest_config_path, package)
    pin = parse_json(regular_bytes(pin_path, 4096))
    validate_game_pin(pin)
    stage_receipt_path = PRIVATE_ROOT / ("lab2-stage-" + stage_id + ".local.json")
    record = parse_json(regular_bytes(stage_receipt_path, 16000), limit=16000)
    receipt_keys = {"schemaVersion", "kind", "stageId", "manifestSha256",
                    "gamePinSha256", "guestConfigSha256", "readerExeSha256", "addonFiles"}
    if (set(record) != receipt_keys or
            type(record["schemaVersion"]) is not int or record["schemaVersion"] != 1 or
            record["kind"] != "autofight-lab2-reader-stage-receipt" or
            record["stageId"] != stage_id or
            record["guestConfigSha256"] != files[1]["sha256"] or
            record["readerExeSha256"] != files[0]["sha256"] or
            record["addonFiles"] != addon_files or
            record["gamePinSha256"] != base.digest_file(pin_path) or
            type(record["manifestSha256"]) is not str or
            HEX64.fullmatch(record["manifestSha256"]) is None):
        fail("stage-receipt-or-input-drift")
    if not INSTALL_SCRIPT.is_file() or INSTALL_SCRIPT.is_symlink():
        fail("lab2-install-script-missing")
    script_sha = base.digest_file(INSTALL_SCRIPT)
    guest_stage = GUEST_STAGE_ROOT + "\\" + stage_id
    run_id = uuid.uuid4().hex
    guest_script = guest_stage + "\\install-reader-" + run_id + ".ps1"
    guest_receipt = guest_stage + "\\install-result-" + run_id + ".json"
    with tempfile.TemporaryDirectory(prefix="lab2-reader-install-", dir=PRIVATE_ROOT) as temp:
        temp_path = Path(temp)
        configure_lab2_transport()
        baseline = base.collect_identity()
        with base.CachedVmwareGuest(baseline) as g:
            verify_guest_addon(g, addon_files)
            fetched_manifest = temp_path / "manifest.json"
            g.run("copyFileFromGuestToHost", guest_stage + "\\manifest.json",
                  str(fetched_manifest), timeout=75)
            if base.digest_file(fetched_manifest) != record["manifestSha256"]:
                fail("lab2-stage-manifest-drift")
            validate_manifest(parse_json(regular_bytes(fetched_manifest, 16384)),
                              stage_id, files, addon_files)
            for name, sha in ((EXE, files[0]["sha256"]), (GUEST_CONFIG, files[1]["sha256"])):
                local = temp_path / name
                g.run("copyFileFromGuestToHost", guest_stage + "\\" + name,
                      str(local), timeout=75)
                if base.digest_file(local) != sha:
                    fail("lab2-staged-file-drift")
            g.run("copyFileFromHostToGuest", str(INSTALL_SCRIPT), guest_script, timeout=75)
            copied_script = temp_path / "installer.ps1"
            g.run("copyFileFromGuestToHost", guest_script, str(copied_script), timeout=75)
            if base.digest_file(copied_script) != script_sha or base.digest_file(INSTALL_SCRIPT) != script_sha:
                fail("lab2-installer-script-drift")
            cmd_ok = True
            try:
                g.run("runProgramInGuest", base.POWERSHELL, "-NoProfile",
                      "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File",
                      guest_script, "-StageId", stage_id, "-InstallRunId", run_id,
                      "-ExpectedManifestSha256", record["manifestSha256"],
                      "-ExpectedGameProcessId", str(pin["gameProcessId"]),
                      "-ExpectedGameProcessStartUtcTicks",
                      str(pin["gameProcessStartUtcTicks"]),
                      "-ExpectedGameExecutablePath", pin["gameExecutablePath"],
                      "-ExpectedGameExecutableSha256", pin["gameExecutableSha256"],
                      timeout=240)
            except base.Blocked:
                cmd_ok = False
            local_receipt = temp_path / "result.json"
            try:
                g.run("copyFileFromGuestToHost", guest_receipt, str(local_receipt), timeout=75)
            except base.Blocked:
                fail("lab2-install-outcome-uncertain-no-retry")
        report = validate_install_receipt(parse_json(regular_bytes(local_receipt, 16384)),
                                          stage_id, run_id)
        if not cmd_ok or report["state"] != "installed-not-running":
            fail("lab2-install-not-complete-or-uncertain-no-retry")
    return {"state": "installed-not-running", "lab": "lab2",
            "stageId": stage_id, "guestReaderStarted": False,
            "addonModified": False, "gameReloaded": False}


def _vm_preflight() -> dict:
    configure_lab2_transport()
    baseline = base.collect_identity()
    with base.CachedVmwareGuest(baseline) as g:
        raw = g.run("listProcessesInGuest", timeout=50)
        if not re.search(r"(?im)rift_x64\.exe", raw):
            fail("lab2-rift-process-not-listed")
    return {"state": "vm-guest-session-verified", "lab": "lab2",
            "vmProcessId": baseline[3], "gameProcessListContainsRift": True,
            "guestModified": False}


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    mode = parser.add_mutually_exclusive_group(required=True)
    mode.add_argument("--preflight", action="store_true")
    mode.add_argument("--vm-preflight", action="store_true")
    mode.add_argument("--stage", action="store_true")
    mode.add_argument("--install", metavar="STAGE_ID")
    parser.add_argument("--guest-config", type=Path, default=DEFAULT_CONFIG)
    parser.add_argument("--package", type=Path, default=DEFAULT_PACKAGE)
    parser.add_argument("--game-pin", type=Path)
    args = parser.parse_args(argv)
    try:
        if args.vm_preflight:
            result = _vm_preflight()
        elif args.preflight:
            _, files, addons = validate_host_inputs(args.guest_config, args.package)
            result = {"state": "host-package-ready", "lab": "lab2",
                      "readerExeSha256": files[0]["sha256"],
                      "guestConfigSha256": files[1]["sha256"],
                      "sourceAddonFiles": len(addons),
                      "guestModified": False}
        else:
            if args.game_pin is None or not same_private_root(args.game_pin) or not args.game_pin.name.startswith("lab2-") or not args.game_pin.name.endswith(".local.json"):
                fail("lab2-private-game-pin-required")
            if args.stage:
                result = _stage(args.guest_config, args.package, args.game_pin)
            else:
                result = _install(args.install, args.guest_config, args.package, args.game_pin)
        print(json.dumps(result, sort_keys=True, separators=(",", ":")))
        return 0
    except (Blocked, base.Blocked) as error:
        print(json.dumps({"state": "blocked", "reason": str(error)}, sort_keys=True))
        return 2
    except Exception:
        print(json.dumps({"state": "blocked", "reason": "unexpected-deployment-error"}, sort_keys=True))
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
