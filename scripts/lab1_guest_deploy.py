"""AutoFight-owned, fail-closed Lab 1 VMware guest package staging.

No RiftReader2 code, services, modules, or subprocesses are used at runtime.
--preflight is *host filesystem only*. --vm-preflight performs authenticated
read-only VMware guest process inventory. --stage requires a second explicit
confirmation and only copies into unique inactive guest-user AppData staging.
No RIFT addon install/reload, reader launch, config/token generation, or input.
"""
from __future__ import annotations

import argparse
import ctypes
from ctypes import wintypes
from dataclasses import asdict, dataclass
from datetime import datetime
import hashlib
import json
import os
from pathlib import Path
import re
import socket
import struct
import subprocess
import sys
import tempfile
import uuid

ROOT = Path(__file__).resolve().parents[1]
DEFAULT_PACKAGE = ROOT / "artifacts" / "local" / "autofight-guest-live-20261105"
STAGE_VERIFIER = ROOT / "scripts" / "verify_lab1_guest_stage.ps1"
INSTALL_HELPER = ROOT / "scripts" / "install_lab1_guest_stage.ps1"
DEFAULT_GAME_IDENTITY = ROOT / "artifacts" / "local" / "lab1-game-identity.local.json"
VMX = Path(r"C:\rift modding\VMs\RIFT-Lab-1\RIFT-Lab.vmx")
VMRUN = Path(r"C:\Program Files\VMware\VMware Workstation\vmrun.exe")
POWERSHELL = r"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe"
GUEST_STAGE_PARENT = r"C:\Users\mrkoo\AppData\Local\AutoFight\staging"
VM_CONSOLE_PORT = 5905
EXE = "Rift.Automation.GuestReader.exe"
PDB = "Rift.Automation.GuestReader.pdb"
ADDON_FILES = ("RiftAddon.toc", "wire.lua", "producer.lua",
               "live.lua", "guidance.lua", "main.lua")
PACKAGE_FILES = (EXE,) + tuple("addon/AutoFight/" + name for name in ADDON_FILES)
PRIVATE_CONFIG = "autofight-guest-reader.json"
LOG_PATTERN = re.compile(
    r"(?m)^(?P<epoch>\S+) .*Log for VMware Workstation pid=(?P<pid>\d+)"
)
VM_PASSWORD_TARGET = "VMware Encrypted VM: " + str(VMX)
GUEST_PASSWORD_TARGET = "RiftReader2 Guest Windows: " + str(VMX)
LEGACY_GUEST_TARGET = (
    r"RiftReader2 Guest Windows: C:\rift modding\VMs\RIFT-Lab\RIFT-Lab.vmx"
)


class Blocked(RuntimeError):
    """Stable non-secret diagnostic only, safe to show to an operator."""


class InstallBlocked(Blocked):
    """Install outcome unavailable or partial; never assume safe retry."""

    def __init__(self, code: str, stage_id: str, state: str = "unknown-install-state",
                 reader_promoted: bool | None = None,
                 addon_promoted: bool | None = None):
        super().__init__(code)
        self.stage_id = stage_id
        self.state = state
        self.reader_promoted = reader_promoted
        self.addon_promoted = addon_promoted


class StageBlocked(Blocked):
    """A failed copy may leave a private JSON in a uniquely owned stage."""

    def __init__(self, code: str, stage_id: str, directory: str):
        super().__init__(code)
        self.stage_id = stage_id
        self.directory = directory


@dataclass(frozen=True)
class PackageItem:
    relativePath: str
    sha256: str
    size: int


def digest_file(path: Path) -> str:
    hasher = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            hasher.update(chunk)
    return hasher.hexdigest()


def validate_package(directory: Path) -> tuple[PackageItem, ...]:
    """Hash one reader plus six Lua files and optional private config; skip PDB."""
    try:
        root = directory.resolve(strict=True)
        root.relative_to((ROOT / "artifacts" / "local").resolve(strict=True))
        if not root.is_dir() or root.is_symlink() or root.is_junction():
            raise ValueError()
        existing = {
            item.relative_to(root).as_posix()
            for item in root.rglob("*") if item.is_file()
        }
        has_private_config = PRIVATE_CONFIG in existing
        wanted = set(PACKAGE_FILES) | ({PDB} if PDB in existing else set()) | (
            {PRIVATE_CONFIG} if has_private_config else set())
        if existing != wanted:
            raise ValueError()
        if any(item.is_symlink() or item.is_junction() for item in root.rglob("*")):
            raise ValueError()
        result = []
        for relative in PACKAGE_FILES + ((PRIVATE_CONFIG,) if has_private_config else ()):
            path = root.joinpath(*relative.split("/"))
            stat = path.stat()
            maximum = (256 * 1024 * 1024 if relative == EXE else
                       32 * 1024 if relative == PRIVATE_CONFIG else 1024 * 1024)
            if not path.is_file() or not 0 < stat.st_size <= maximum:
                raise ValueError()
            if relative.startswith("addon/AutoFight/"):
                original = ROOT / "addon" / "AutoFight" / relative.rsplit("/", 1)[1]
                if digest_file(path) != digest_file(original):
                    raise ValueError()
            result.append(PackageItem(relative.replace("/", "\\"), digest_file(path),
                                      stat.st_size))
        toc = (root / "addon" / "AutoFight" / "RiftAddon.toc").read_text(
            encoding="utf-8"
        )
        line = re.search(r"(?m)^RunOnStartup\s*=\s*\{([^\r\n}]*)\}\s*$", toc)
        if not line or re.findall(r'"([^"]+)"', line.group(1)) != list(ADDON_FILES[1:]):
            raise ValueError()
        return tuple(result)
    except (OSError, ValueError, UnicodeError):
        raise Blocked("package-invalid-or-incomplete") from None


def _windows_path(path: Path) -> str:
    return str(path.resolve(strict=True)).replace("/", "\\").casefold()


def _read_vm_log(vmx: Path) -> tuple[str, int]:
    try:
        with vmx.with_name("vmware.log").open(encoding="utf-8",
                                             errors="replace") as stream:
            match = LOG_PATTERN.search(stream.read(8192))
        if match is None:
            raise ValueError()
        date = datetime.fromisoformat(match["epoch"].replace("Z", "+00:00"))
        if date.tzinfo is None or int(match["pid"]) <= 0:
            raise ValueError()
        return match["epoch"], int(match["pid"])
    except (OSError, ValueError):
        raise Blocked("vm-log-identity-unavailable") from None


def _process_identity(pid: int) -> tuple[str, int]:
    kernel = ctypes.WinDLL("kernel32", use_last_error=True)
    kernel.OpenProcess.argtypes = [wintypes.DWORD, wintypes.BOOL, wintypes.DWORD]
    kernel.OpenProcess.restype = wintypes.HANDLE
    kernel.CloseHandle.argtypes = [wintypes.HANDLE]
    handle = kernel.OpenProcess(0x1000, False, pid)
    if not handle:
        raise Blocked("vm-process-unavailable")
    try:
        kernel.QueryFullProcessImageNameW.argtypes = [
            wintypes.HANDLE, wintypes.DWORD, wintypes.LPWSTR,
            ctypes.POINTER(wintypes.DWORD)
        ]
        buffer = ctypes.create_unicode_buffer(32768)
        length = wintypes.DWORD(len(buffer))
        if not kernel.QueryFullProcessImageNameW(
            handle, 0, buffer, ctypes.byref(length)
        ):
            raise Blocked("vm-process-image-unavailable")
        kernel.GetProcessTimes.argtypes = [
            wintypes.HANDLE,
            ctypes.POINTER(wintypes.FILETIME),
            ctypes.POINTER(wintypes.FILETIME),
            ctypes.POINTER(wintypes.FILETIME),
            ctypes.POINTER(wintypes.FILETIME),
        ]
        times = [wintypes.FILETIME() for _ in range(4)]
        if not kernel.GetProcessTimes(
            handle, *(ctypes.byref(value) for value in times)
        ):
            raise Blocked("vm-process-start-unavailable")
        start = (times[0].dwHighDateTime << 32) | times[0].dwLowDateTime
        return buffer.value.casefold(), start
    finally:
        kernel.CloseHandle(handle)


def _listener_owner(port: int) -> int:
    api = ctypes.WinDLL("iphlpapi", use_last_error=True)
    get_table = api.GetExtendedTcpTable
    get_table.argtypes = [
        ctypes.c_void_p, ctypes.POINTER(wintypes.DWORD), wintypes.BOOL,
        wintypes.ULONG, ctypes.c_int, wintypes.ULONG
    ]
    size = wintypes.DWORD()
    get_table(None, ctypes.byref(size), False, socket.AF_INET, 3, 0)
    if not 4 <= size.value <= 2 * 1024 * 1024:
        raise Blocked("vm-console-listener-query-failed")
    raw = ctypes.create_string_buffer(size.value)
    if get_table(raw, ctypes.byref(size), False, socket.AF_INET, 3, 0):
        raise Blocked("vm-console-listener-query-failed")
    data = raw.raw[:size.value]
    if len(data) < 4:
        raise Blocked("vm-console-listener-invalid")
    count = struct.unpack_from("<I", data)[0]
    if 4 + count * 24 > len(data):
        raise Blocked("vm-console-listener-invalid")
    matches = []
    for index in range(count):
        _, addr, local_port, _, _, pid = struct.unpack_from(
            "<6I", data, 4 + index * 24
        )
        if socket.ntohs(local_port & 0xffff) == port:
            ip = socket.inet_ntoa(struct.pack("<I", addr))
            if ip != "127.0.0.1":
                raise Blocked("vm-console-not-loopback-only")
            matches.append(pid)
    if len(matches) != 1:
        raise Blocked("vm-console-listener-not-unique")
    return matches[0]


def _binding_files(vmx: Path) -> tuple:
    try:
        vmx_stat = vmx.stat()
        log_stat = vmx.with_name("vmware.log").stat()
        if not vmx_stat.st_ino or not log_stat.st_ino:
            raise ValueError()
        return (vmx_stat.st_dev, vmx_stat.st_ino,
                log_stat.st_dev, log_stat.st_ino)
    except (OSError, ValueError):
        raise Blocked("vm-binding-files-unavailable") from None


def direct_identity() -> tuple:
    if os.name != "nt":
        raise Blocked("windows-required")
    binding_before = _binding_files(VMX)
    epoch, pid = _read_vm_log(VMX)
    image, started = _process_identity(pid)
    expected = _windows_path(VMRUN.parent / "x64" / "vmware-vmx.exe")
    if image.replace("/", "\\") != expected:
        raise Blocked("vm-process-image-mismatch")
    if _listener_owner(VM_CONSOLE_PORT) != pid:
        raise Blocked("vm-console-pid-mismatch")
    if _process_identity(pid) != (image, started):
        raise Blocked("vm-process-restarted")
    if _read_vm_log(VMX) != (epoch, pid):
        raise Blocked("vm-log-identity-changed")
    vm_launch = datetime.fromisoformat(epoch.replace("Z", "+00:00")).timestamp()
    process_launch = started / 10_000_000 - 11_644_473_600
    if not 0 <= vm_launch - process_launch < 10:
        raise Blocked("vm-launch-time-mismatch")
    if binding_before != _binding_files(VMX):
        raise Blocked("vm-binding-files-changed")
    return (_windows_path(VMX), binding_before, epoch, pid, image, started,
            VM_CONSOLE_PORT)


def collect_identity() -> tuple:
    if not VMX.is_file() or not VMRUN.is_file():
        raise Blocked("vmware-path-unavailable")
    before = _binding_files(VMX)
    try:
        result = subprocess.run(
            [str(VMRUN), "list"], capture_output=True, text=True,
            timeout=10, creationflags=subprocess.CREATE_NO_WINDOW, check=False
        )
    except (OSError, subprocess.SubprocessError):
        raise Blocked("vmrun-list-unavailable") from None
    if result.returncode:
        raise Blocked("vmrun-list-failed")
    rows = [line.strip() for line in result.stdout.splitlines()
            if line.strip().casefold().endswith(".vmx")]
    exact = _windows_path(VMX)
    try:
        normalized = [str(Path(row).resolve(strict=False)).replace("/", "\\").casefold()
                      for row in rows]
    except OSError:
        raise Blocked("vmrun-list-invalid") from None
    if normalized.count(exact) != 1:
        raise Blocked("lab1-not-uniquely-running")
    if any(
        item != exact and str(Path(item).parent).casefold()
        == str(Path(exact).parent).casefold() for item in normalized
    ):
        raise Blocked("vm-log-parent-ambiguous")
    identity = direct_identity()
    if before != _binding_files(VMX):
        raise Blocked("vm-binding-files-changed")
    return identity


class _Credential(ctypes.Structure):
    _fields_ = [
        ("Flags", wintypes.DWORD), ("Type", wintypes.DWORD),
        ("TargetName", wintypes.LPWSTR), ("Comment", wintypes.LPWSTR),
        ("LastWritten", wintypes.FILETIME), ("BlobSize", wintypes.DWORD),
        ("Blob", ctypes.POINTER(ctypes.c_ubyte)),
        ("Persist", wintypes.DWORD), ("AttributeCount", wintypes.DWORD),
        ("Attributes", ctypes.c_void_p), ("TargetAlias", wintypes.LPWSTR),
        ("UserName", wintypes.LPWSTR),
    ]


def _credential(target: str, *, guest: bool) -> tuple[ctypes.Array, ...]:
    """Read existing Windows Credential Manager entry; NEVER prompt/write."""
    if os.name != "nt":
        raise Blocked("windows-required")
    api = ctypes.WinDLL("advapi32", use_last_error=True)
    api.CredReadW.argtypes = [
        wintypes.LPCWSTR, wintypes.DWORD, wintypes.DWORD,
        ctypes.POINTER(ctypes.POINTER(_Credential)),
    ]
    api.CredReadW.restype = wintypes.BOOL
    api.CredFree.argtypes = [ctypes.c_void_p]
    ptr = ctypes.POINTER(_Credential)()
    if not api.CredReadW(target, 1, 0, ctypes.byref(ptr)):
        raise Blocked("cached-guest-credentials-missing" if guest
                      else "cached-vm-password-missing")
    try:
        entry = ptr.contents
        if not 0 < entry.BlobSize <= 2048:
            raise ValueError()
        raw = ctypes.string_at(entry.Blob, entry.BlobSize)
        if guest:
            username = entry.UserName or ""
            password = raw.decode("utf-16-le").rstrip("\x00")
            if not username or "\x00" in username:
                raise ValueError()
            return (ctypes.create_unicode_buffer(username),
                    ctypes.create_unicode_buffer(password))
        encoding = "utf-16-le" if len(raw) % 2 == 0 and raw[1:2] == b"\x00" else "utf-8"
        return (ctypes.create_unicode_buffer(raw.decode(encoding).rstrip("\x00")),)
    except (ValueError, UnicodeError):
        raise Blocked("cached-credential-invalid") from None
    finally:
        api.CredFree(ptr)


def _wipe(buffer: ctypes.Array | None) -> None:
    if buffer is not None:
        ctypes.memset(ctypes.addressof(buffer), 0, ctypes.sizeof(buffer))


class CachedVmwareGuest:
    """Identity-pinned vmrun guest session, with cached credentials only."""

    def __init__(self, baseline: tuple):
        self.baseline = baseline
        self.vm_password: ctypes.Array | None = None
        self.username: ctypes.Array | None = None
        self.guest_password: ctypes.Array | None = None

    def __enter__(self) -> "CachedVmwareGuest":
        self._require_identity()
        try:
            self.vm_password, = _credential(VM_PASSWORD_TARGET, guest=False)
            try:
                self.username, self.guest_password = _credential(
                    GUEST_PASSWORD_TARGET, guest=True
                )
            except Blocked as error:
                if str(error) != "cached-guest-credentials-missing":
                    raise
                self.username, self.guest_password = _credential(
                    LEGACY_GUEST_TARGET, guest=True
                )
            self.run("listProcessesInGuest", timeout=45)
            return self
        except BaseException:
            self.close()
            raise

    def __exit__(self, *_args: object) -> None:
        self.close()

    def close(self) -> None:
        for buffer in (self.vm_password, self.username, self.guest_password):
            _wipe(buffer)
        self.vm_password = self.username = self.guest_password = None

    def _require_identity(self) -> None:
        if direct_identity() != self.baseline:
            raise Blocked("vm-identity-changed")

    def run(self, command: str, *arguments: str, timeout: int = 60) -> str:
        if command not in (
            "listProcessesInGuest", "copyFileFromHostToGuest",
            "copyFileFromGuestToHost", "runProgramInGuest"
        ):
            raise Blocked("vmrun-command-disallowed")
        if type(timeout) is not int or not 1 <= timeout <= 300:
            raise Blocked("vmrun-timeout-invalid")
        if not self.vm_password or not self.username or not self.guest_password:
            raise Blocked("guest-session-not-open")
        self._require_identity()
        argv = [
            str(VMRUN), "-T", "ws", "-vp", self.vm_password.value,
            "-gu", self.username.value, "-gp", self.guest_password.value,
            command, str(VMX), *arguments,
        ]
        try:
            result = subprocess.run(
                argv, capture_output=True, text=True, timeout=timeout,
                creationflags=subprocess.CREATE_NO_WINDOW, check=False
            )
        except subprocess.TimeoutExpired:
            raise Blocked("guest-operation-timeout:" + command) from None
        except OSError:
            raise Blocked("guest-operation-start-failed:" + command) from None
        finally:
            for position in (4, 6, 8):
                argv[position] = "[cleared]"
        self._require_identity()
        if result.returncode:
            # stderr/stdout can contain user names, paths or credentials; discard.
            raise Blocked("guest-operation-failed:" + command)
        return result.stdout


def validate_guest_receipt(receipt: object, stage_id: str,
                           expected: tuple[PackageItem, ...]) -> None:
    if not isinstance(receipt, dict) or set(receipt) != {
        "schemaVersion", "kind", "stageId", "machine", "verified", "files", "reason"
    }:
        raise Blocked("guest-staging-receipt-invalid")
    if (type(receipt["schemaVersion"]) is not int or receipt["schemaVersion"] != 1
            or receipt["kind"] != "autofight-staging-only"
            or receipt["stageId"] != stage_id or receipt["machine"] != "RIFT-LAB-1"
            or receipt["verified"] is not True
            or receipt["reason"] != "verified-staged-only"):
        raise Blocked("guest-stage-verification-rejected")
    files = receipt["files"]
    if not isinstance(files, list) or len(files) != len(expected):
        raise Blocked("guest-stage-files-incomplete")
    by_name = {}
    for item in files:
        if (not isinstance(item, dict) or set(item) != {
            "relativePath", "sha256", "size"
        } or not isinstance(item["relativePath"], str) or
                item["relativePath"].casefold() in by_name or
                not isinstance(item["sha256"], str) or
                type(item["size"]) is not int):
            raise Blocked("guest-staging-receipt-invalid")
        by_name[item["relativePath"].casefold()] = item
    for file in expected:
        item = by_name.get(file.relativePath.casefold())
        if (item is None or item["relativePath"] != file.relativePath
                or item["sha256"] != file.sha256 or item["size"] != file.size):
            raise Blocked("guest-staged-hash-mismatch")


def _stage_impl(package: Path, files: tuple[PackageItem, ...],
                stage_id: str, guest_root: str) -> dict:
    """Only touches a freshly namespaced INACTIVE stage directory in Lab1."""
    guest_manifest = guest_root + r"\manifest.json"
    guest_script = guest_root + r"\verify.ps1"
    guest_receipt = guest_root + r"\receipt.json"
    with tempfile.TemporaryDirectory(
        prefix="guest-stage-", dir=ROOT / "artifacts" / "local"
    ) as scratch:
        temp = Path(scratch)
        manifest = temp / "manifest.json"
        manifest.write_text(json.dumps({
            "schemaVersion": 1, "kind": "autofight-staging-only",
            "stageId": stage_id, "files": [asdict(item) for item in files]
        }, separators=(",", ":")), encoding="utf-8")
        local_receipt = temp / "receipt.json"
        baseline = collect_identity()
        with CachedVmwareGuest(baseline) as guest:
            # No operation names 'start', 'stop', 'install', 'reload', or 'input'.
            # The UUID ensures staging does not touch an installed addon.
            cmd = (
                "$ErrorActionPreference='Stop';"
                "$base='" + GUEST_STAGE_PARENT + "';"
                "$check=@('C:\\Users','C:\\Users\\mrkoo',"
                "'C:\\Users\\mrkoo\\AppData','C:\\Users\\mrkoo\\AppData\\Local');"
                "foreach($path in $check){$d=Get-Item -LiteralPath $path -Force;"
                "if((-not $d.PSIsContainer) -or "
                "($d.Attributes -band [IO.FileAttributes]::ReparsePoint))"
                "{throw 'stage-parent-reparse';}};"
                "$parent='C:\\Users\\mrkoo\\AppData\\Local\\AutoFight';"
                "if(-not(Test-Path -LiteralPath $parent)){"
                "[void](New-Item -ItemType Directory -Path $parent -ErrorAction Stop);};"
                "$d=Get-Item -LiteralPath $parent -Force -ErrorAction Stop;"
                "if((-not $d.PSIsContainer) -or "
                "($d.Attributes -band [IO.FileAttributes]::ReparsePoint))"
                "{throw 'stage-parent-reparse';};"
                "if(-not(Test-Path -LiteralPath $base)){"
                "[void](New-Item -ItemType Directory -Path $base -ErrorAction Stop);};"
                "$d=Get-Item -LiteralPath $base -Force -ErrorAction Stop;"
                "if((-not $d.PSIsContainer) -or "
                "($d.Attributes -band [IO.FileAttributes]::ReparsePoint))"
                "{throw 'stage-parent-reparse';};"
                "[void](New-Item -ItemType Directory -Path '" +
                guest_root + "' -ErrorAction Stop);"
                "[void](New-Item -ItemType Directory -Path '" +
                guest_root + r"\addon\AutoFight' -Force -ErrorAction Stop)"
            )
            guest.run(
                "runProgramInGuest", POWERSHELL, "-NoProfile",
                "-NonInteractive", "-Command", cmd, timeout=60
            )
            guest.run("copyFileFromHostToGuest", str(STAGE_VERIFIER),
                      guest_script, timeout=90)
            guest.run("copyFileFromHostToGuest", str(manifest),
                      guest_manifest, timeout=90)
            for item in files:
                source = package.joinpath(*item.relativePath.split("\\"))
                if digest_file(source) != item.sha256:
                    raise Blocked("package-changed-before-copy")
                target = guest_root + "\\" + item.relativePath
                guest.run("copyFileFromHostToGuest", str(source),
                          target, timeout=240)
                if digest_file(source) != item.sha256:
                    raise Blocked("package-changed-during-copy")
            guest.run(
                "runProgramInGuest", POWERSHELL, "-NoProfile",
                "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File",
                guest_script, "-StageRoot", guest_root,
                "-ManifestPath", guest_manifest, "-ReportPath", guest_receipt,
                timeout=150,
            )
            guest.run("copyFileFromGuestToHost", guest_receipt,
                      str(local_receipt), timeout=60)
        try:
            if local_receipt.stat().st_size > 32 * 1024:
                raise ValueError()
            receipt = json.loads(local_receipt.read_text(encoding="utf-8-sig"))
        except (OSError, ValueError, UnicodeError):
            raise Blocked("guest-staging-receipt-invalid") from None
        validate_guest_receipt(receipt, stage_id, files)
    return {
        "state": "verified-staged-only", "lab": "lab1",
        "stageId": stage_id, "guestStageDirectory": guest_root,
        "fileCount": len(files),
        "guestReaderStarted": False, "addonInstalled": False,
        "gameReloaded": False,
    }


def _stage(package: Path, files: tuple[PackageItem, ...]) -> dict:
    stage_id = uuid.uuid4().hex
    guest_root = GUEST_STAGE_PARENT + "\\" + stage_id
    try:
        return _stage_impl(package, files, stage_id, guest_root)
    except Exception as error:
        code = str(error) if isinstance(error, Blocked) else "stage-operation-failed"
        # A timeout/guest disconnect can leave partial bytes. Never silently
        # imply a successful cleanup: identify only the nonce-scoped directory.
        raise StageBlocked(code, stage_id, guest_root) from None


def _strict_object(pairs: list[tuple[str, object]]) -> dict:
    value = {}
    for key, item in pairs:
        if key in value:
            raise ValueError("duplicate-key")
        value[key] = item
    return value


def _read_json(path: Path, maximum_bytes: int) -> object:
    try:
        if not path.is_file() or path.is_symlink() or path.stat().st_size > maximum_bytes:
            raise ValueError()
        return json.loads(path.read_text(encoding="utf-8-sig"),
                          object_pairs_hook=_strict_object)
    except (OSError, ValueError, UnicodeError):
        raise Blocked("private-json-or-receipt-invalid") from None


def load_game_identity(path: Path) -> dict:
    """Read a private *pin*, never infer that the game is still alive."""
    try:
        candidate = path.resolve(strict=True)
        candidate.relative_to((ROOT / "artifacts" / "local").resolve(strict=True))
        if not candidate.name.endswith(".local.json"):
            raise ValueError()
        data = _read_json(candidate, 4096)
        if (not isinstance(data, dict) or set(data) != {
            "schemaVersion", "lab", "machine", "gameProcessId",
            "gameProcessStartUtcTicks", "gameExecutablePath",
            "gameExecutableSha256",
        } or type(data["schemaVersion"]) is not int
                or data["schemaVersion"] != 1 or data["lab"] != "lab1"
                or data["machine"] != "RIFT-LAB-1"
                or type(data["gameProcessId"]) is not int
                or not 0 < data["gameProcessId"] <= 0xffffffff
                or type(data["gameProcessStartUtcTicks"]) is not int
                or data["gameProcessStartUtcTicks"] <= 0
                or not isinstance(data["gameExecutablePath"], str)
                or not re.fullmatch(r"[A-Za-z]:\\[^'\x00\r\n]*\\rift_x64\.exe",
                                    data["gameExecutablePath"], re.I)
                or not isinstance(data["gameExecutableSha256"], str)
                or not re.fullmatch(r"[a-f0-9]{64}",
                                    data["gameExecutableSha256"])):
            raise ValueError()
        return data
    except (OSError, ValueError, Blocked):
        raise Blocked("game-identity-pin-invalid") from None


def validate_staged_manifest(data: object, stage_id: str,
                             current: tuple[PackageItem, ...]) -> None:
    if not isinstance(data, dict) or set(data) != {
        "schemaVersion", "kind", "stageId", "files"
    }:
        raise Blocked("stage-manifest-invalid")
    if (type(data["schemaVersion"]) is not int or data["schemaVersion"] != 1
            or data["kind"] != "autofight-staging-only"
            or data["stageId"] != stage_id
            or not isinstance(data["files"], list)
            or len(data["files"]) != 8 or len(current) != 8):
        raise Blocked("stage-manifest-invalid")
    by_name = {item.relativePath: item for item in current}
    if len(by_name) != 8:
        raise Blocked("stage-manifest-invalid")
    seen = set()
    for item in data["files"]:
        if (not isinstance(item, dict) or set(item) != {
            "relativePath", "sha256", "size"
        } or not isinstance(item["relativePath"], str)):
            raise Blocked("stage-manifest-invalid")
        name = item["relativePath"]
        if name in seen or name not in by_name:
            raise Blocked("stage-manifest-invalid")
        seen.add(name)
        expected = by_name[name]
        if (item["sha256"] != expected.sha256 or
                type(item["size"]) is not int or item["size"] != expected.size):
            raise Blocked("stage-source-drift")
    if seen != set(by_name):
        raise Blocked("stage-manifest-invalid")


def validate_install_receipt(data: object, stage_id: str, install_run_id: str) -> dict:
    names = {
        "schemaVersion", "kind", "stageId", "installRunId", "machine", "state", "reason",
        "readerPromoted", "addonPromoted", "readerFinal", "addonFinal",
        "readerPending", "addonPending", "noReaderStart", "noGameReload",
        "noGameInput"
    }
    if not isinstance(data, dict) or set(data) != names:
        raise Blocked("install-receipt-invalid")
    if (type(data["schemaVersion"]) is not int or data["schemaVersion"] != 1
            or data["kind"] != "autofight-lab1-install"
            or data["stageId"] != stage_id or data["installRunId"] != install_run_id
            or data["machine"] != "RIFT-LAB-1"
            or type(data["readerPromoted"]) is not bool
            or type(data["addonPromoted"]) is not bool
            or data["noReaderStart"] is not True
            or data["noGameReload"] is not True
            or data["noGameInput"] is not True
            or data["state"] not in (
                "installed-not-running", "partial-install-needs-review",
                "blocked-no-final-install",
            )):
        raise Blocked("install-receipt-invalid")
    expected_reader = r"C:\Users\mrkoo\AppData\Local\AutoFight\GuestReader"
    expected_addon = (r"C:\Users\mrkoo\OneDrive\Documents\RIFT\Interface"
                      r"\AddOns\AutoFight")
    expected_pending_reader = (r"C:\Users\mrkoo\AppData\Local\AutoFight"
                               + r"\.GuestReader-" + stage_id + ".pending")
    expected_pending_addon = (r"C:\Users\mrkoo\OneDrive\Documents\RIFT\Interface"
                              + r"\.AutoFight-" + stage_id + ".pending")
    if (data["readerFinal"] != expected_reader or data["addonFinal"] != expected_addon
            or data["readerPending"] != expected_pending_reader
            or data["addonPending"] != expected_pending_addon):
        raise Blocked("install-receipt-path-mismatch")
    if data["state"] == "installed-not-running" and (
        data["readerPromoted"] is not True or data["addonPromoted"] is not True
        or data["reason"] != "verified-installed-no-reload"
    ):
        raise Blocked("install-receipt-inconsistent")
    if data["state"] == "blocked-no-final-install" and (
        data["readerPromoted"] or data["addonPromoted"]
    ):
        raise Blocked("install-receipt-inconsistent")
    return data


def _install_impl(package: Path, files: tuple[PackageItem, ...],
                  stage_id: str, identity: dict) -> dict:
    stage_root = GUEST_STAGE_PARENT + "\\" + stage_id
    install_run_id = uuid.uuid4().hex
    install_script = stage_root + "\\install-" + install_run_id + ".ps1"
    baseline = collect_identity()
    with tempfile.TemporaryDirectory(
        prefix="guest-install-", dir=ROOT / "artifacts" / "local"
    ) as scratch:
        temp = Path(scratch)
        with CachedVmwareGuest(baseline) as guest:
            # Check original guest stage manifest and verifier source without writes.
            local_manifest = temp / "manifest.json"
            local_verifier = temp / "verify.ps1"
            guest.run("copyFileFromGuestToHost", stage_root + r"\manifest.json",
                      str(local_manifest), timeout=60)
            manifest = _read_json(local_manifest, 16 * 1024)
            validate_staged_manifest(manifest, stage_id, files)
            approved_manifest_sha256 = digest_file(local_manifest)
            guest.run("copyFileFromGuestToHost", stage_root + r"\verify.ps1",
                      str(local_verifier), timeout=60)
            if digest_file(local_verifier) != digest_file(STAGE_VERIFIER):
                raise Blocked("guest-stage-verifier-source-drift")
            # Rerun guest SHA256 verification before any installed path is touched.
            guest.run("runProgramInGuest", POWERSHELL, "-NoProfile",
                      "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File",
                      stage_root + r"\verify.ps1", "-StageRoot", stage_root,
                      "-ManifestPath", stage_root + r"\manifest.json",
                      "-ReportPath", stage_root + r"\receipt.json", timeout=150)
            local_receipt = temp / "stage-receipt.json"
            guest.run("copyFileFromGuestToHost", stage_root + r"\receipt.json",
                      str(local_receipt), timeout=60)
            receipt = _read_json(local_receipt, 32 * 1024)
            validate_guest_receipt(receipt, stage_id, files)

            # Copy an AutoFight-owned script only into inactive staging. Verify
            # the transferred script's full bytes before invoking it.
            guest.run("copyFileFromHostToGuest", str(INSTALL_HELPER),
                      install_script, timeout=90)
            copied_script = temp / "install.ps1"
            guest.run("copyFileFromGuestToHost", install_script,
                      str(copied_script), timeout=90)
            if digest_file(copied_script) != digest_file(INSTALL_HELPER):
                raise Blocked("guest-install-helper-hash-mismatch")

            command_ok = True
            try:
                guest.run("runProgramInGuest", POWERSHELL, "-NoProfile",
                          "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File",
                          install_script, "-StageId", stage_id,
                          "-InstallRunId", install_run_id,
                          "-ExpectedManifestSha256", approved_manifest_sha256,
                          "-ExpectedGameProcessId", str(identity["gameProcessId"]),
                          "-ExpectedGameProcessStartUtcTicks",
                          str(identity["gameProcessStartUtcTicks"]),
                          "-ExpectedGameExecutablePath",
                          identity["gameExecutablePath"],
                          "-ExpectedGameExecutableSha256",
                          identity["gameExecutableSha256"], timeout=240)
            except Blocked:
                command_ok = False
            install_result = temp / "install-result.json"
            guest.run("copyFileFromGuestToHost",
                      stage_root + "\\install-result-" + install_run_id + ".json",
                      str(install_result), timeout=60)
        report = validate_install_receipt(_read_json(install_result, 16 * 1024),
                                          stage_id, install_run_id)
        if not command_ok or report["state"] != "installed-not-running":
            raise InstallBlocked(
                "guest-install-not-complete", stage_id, report["state"],
                report["readerPromoted"], report["addonPromoted"]
            )
        return {
            "state": "installed-not-running", "lab": "lab1", "stageId": stage_id,
            "readerInstalled": True, "addonInstalled": True,
            "guestReaderStarted": False, "gameReloaded": False,
            "gameInput": False,
        }


def _install(package: Path, files: tuple[PackageItem, ...],
             stage_id: str, identity: dict) -> dict:
    try:
        return _install_impl(package, files, stage_id, identity)
    except InstallBlocked:
        raise
    except Exception:
        # Even an unreturned vmrun/PowerShell command may have promoted files.
        raise InstallBlocked("guest-install-outcome-unavailable", stage_id) from None


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    action = parser.add_mutually_exclusive_group(required=True)
    action.add_argument("--preflight", action="store_true",
                        help="Offline package hashes only (no VMware access)")
    action.add_argument("--vm-preflight", action="store_true",
                        help="Read-only exact Lab1 VM identity and cached guest login")
    action.add_argument("--stage", action="store_true",
                        help="Explicit inactive Lab1 copy, verified against SHA256")
    action.add_argument("--install", action="store_true",
                        help="Explicit absent-only promotion of a verified Lab1 stage")
    parser.add_argument("--package", type=Path, default=DEFAULT_PACKAGE)
    parser.add_argument("--identity", type=Path, default=DEFAULT_GAME_IDENTITY,
                        help="Ignored private Lab1 exact-game identity pin")
    parser.add_argument("--stage-id", help="Verified 32-hex inactive stage ID")
    parser.add_argument("--confirm", choices=["STAGE-LAB1", "INSTALL-LAB1"],
                        help="Required distinct confirmation for stage or install")
    opts = parser.parse_args(argv)
    if opts.stage:
        if opts.confirm != "STAGE-LAB1" or opts.stage_id is not None:
            parser.error("--stage requires --confirm STAGE-LAB1 and no --stage-id")
    elif opts.install:
        if (opts.confirm != "INSTALL-LAB1" or not isinstance(opts.stage_id, str)
                or not re.fullmatch(r"[a-f0-9]{32}", opts.stage_id)):
            parser.error("--install requires --confirm INSTALL-LAB1 and 32hex --stage-id")
    elif opts.confirm is not None or opts.stage_id is not None:
        parser.error("read-only modes reject --confirm and --stage-id")
    try:
        files = validate_package(opts.package)
        if (opts.stage or opts.install) and not STAGE_VERIFIER.is_file():
            raise Blocked("stage-verifier-unavailable")
        if opts.install and not INSTALL_HELPER.is_file():
            raise Blocked("install-helper-unavailable")
        summary = {
            "state": "offline-package-verified", "lab": "lab1",
            "packageFileCount": len(files),
            "fileHashes": [asdict(item) for item in files
                           if item.relativePath != PRIVATE_CONFIG],
            "privateGuestConfigStagedIfAuthorized": any(
                item.relativePath == PRIVATE_CONFIG for item in files),
            "vmConnected": False, "addonInstalled": False,
            "guestReaderStarted": False, "gameReloaded": False,
        }
        if opts.vm_preflight:
            baseline = collect_identity()
            with CachedVmwareGuest(baseline):
                pass
            summary["state"] = "cached-auth-read-only-verified"
            summary["vmConnected"] = True
        elif opts.stage:
            summary = _stage(opts.package.resolve(strict=True), files)
        elif opts.install:
            identity = load_game_identity(opts.identity)
            if len(files) != 8:
                raise Blocked("private-guest-config-required-for-install")
            summary = _install(opts.package.resolve(strict=True), files,
                               opts.stage_id, identity)
        print(json.dumps(summary, separators=(",", ":")))
        return 0
    except (Blocked, OSError) as error:
        # Never dump traceback / subprocess argv / credential material.
        code = str(error) if isinstance(error, Blocked) else "host-io-unavailable"
        report = {"state": "blocked", "lab": "lab1", "reason": code}
        if isinstance(error, StageBlocked):
            report.update({"possiblePartialStage": True,
                           "stageId": error.stage_id,
                           "guestStageDirectory": error.directory,
                           "manualCleanupRequiredIfPresent": True})
        if isinstance(error, InstallBlocked):
            report.update({"state": error.state, "stageId": error.stage_id,
                           "readerPromoted": error.reader_promoted,
                           "addonPromoted": error.addon_promoted,
                           "manualReviewRequired": True,
                           "noAutomaticRetry": True})
        print(json.dumps(report))
        return 2


if __name__ == "__main__":
    sys.exit(main())
