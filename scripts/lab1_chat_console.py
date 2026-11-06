"""AutoFight-only reviewed Lab1 VNC chat console.

Supports only open-chat, fixed titlebar focus, /reloadui, and /afbind 32 lowercase hex.
One screenshot review and one command per explicit invocation; no automatic input.
No RiftReader2 imports, processes, modules, services, or runtime dependencies.
"""
from __future__ import annotations
import argparse
import base64
from contextlib import contextmanager
import ctypes
from ctypes import wintypes
from datetime import datetime, timezone
import hashlib
import hmac
import json
import os
from pathlib import Path
import re
import stat
import tempfile
import time
import threading
import uuid

import lab1_guest_deploy as lab

AGE_LIMIT = 60
ALLOWED = re.compile(r"(?:/reloadui|/afbind [0-9a-f]{32})\Z", re.ASCII)
HEX32 = re.compile(r"[0-9a-f]{32}\Z", re.ASCII)
HEX64 = re.compile(r"[0-9a-f]{64}\Z", re.ASCII)
ARTIFACTS = lab.ROOT / "artifacts" / "local"
HMAC_LABEL = b"RIFT-Lab local console v1"
MAX_JSON = 32768


class Blocked(RuntimeError):
    """A non-secret diagnostic reason."""


def operation_for_command(value: str) -> dict:
    if type(value) is not str or ALLOWED.fullmatch(value) is None:
        raise Blocked("unsupported-chat-command")
    return {"type": "command", "command": value}


def open_chat_operation() -> dict:
    return {"type": "ui-step", "step": "open-chat"}


def focus_titlebar_operation() -> dict:
    # The coordinate is fixed, not taken from a user-supplied plan or receipt.
    return {"type": "ui-step", "step": "focus-titlebar", "x": 120, "y": 8}


def validate_operation(value: object) -> dict:
    if value == focus_titlebar_operation():
        return focus_titlebar_operation()
    if value == open_chat_operation():
        return open_chat_operation()
    if isinstance(value, dict) and set(value) == {"type", "command"} and value["type"] == "command":
        return operation_for_command(value["command"])
    raise Blocked("invalid-operation")


def utc_now() -> datetime:
    return datetime.now(timezone.utc)


def iso(value: datetime) -> str:
    return value.isoformat().replace("+00:00", "Z")


def parse_time(value: object) -> datetime:
    try:
        result = datetime.fromisoformat(value.replace("Z", "+00:00"))
        if result.tzinfo is None:
            raise ValueError()
        return result.astimezone(timezone.utc)
    except (AttributeError, ValueError, TypeError):
        raise Blocked("invalid-review-time") from None


def digest(data: dict) -> str:
    return hashlib.sha256(json.dumps(data, sort_keys=True, separators=(",", ":"),
                                     ensure_ascii=True).encode("ascii")).hexdigest()


def unique_pairs(pairs):
    output = {}
    for key, value in pairs:
        if key in output:
            raise ValueError("duplicate-key")
        output[key] = value
    return output


def read_json(path: Path) -> dict:
    try:
        info = path.lstat()
        if (not stat.S_ISREG(info.st_mode) or path.is_symlink()
            or getattr(info, "st_file_attributes", 0) &
               getattr(stat, "FILE_ATTRIBUTE_REPARSE_POINT", 0)
            or not 0 < info.st_size <= MAX_JSON):
            raise ValueError()
        result = json.loads(path.read_text(encoding="utf-8"),
                            object_pairs_hook=unique_pairs)
        if type(result) is not dict:
            raise ValueError()
        return result
    except (OSError, ValueError, UnicodeError):
        raise Blocked("invalid-json-file") from None


def write_new(path: Path, data: dict) -> None:
    with path.open("x", encoding="utf-8", newline="\n") as stream:
        json.dump(data, stream, sort_keys=True, indent=2)
        stream.write("\n")
        stream.flush()
        os.fsync(stream.fileno())


def output_directory(path: Path, create: bool) -> Path:
    try:
        base = ARTIFACTS.resolve(strict=True)
        target = path.resolve(strict=False)
        if target == base or target.parent != base or path.is_symlink() or path.is_junction():
            raise ValueError()
        if create:
            target.mkdir(exist_ok=False)
        elif not target.is_dir():
            raise ValueError()
        return target
    except (OSError, ValueError):
        raise Blocked("invalid-output-directory") from None


@contextmanager
def control_mutex():
    """Uses RR2's preexisting mutex name, without importing its implementation."""
    if os.name != "nt":
        raise Blocked("windows-required")
    key = str(lab.VMX.resolve(strict=True)).replace("/", "\\").casefold()
    name = "Local\\RiftReader.VMConsole." + hashlib.sha256(key.encode("utf-8")).hexdigest()[:24]
    api = ctypes.WinDLL("kernel32", use_last_error=True)
    api.CreateMutexW.argtypes = [ctypes.c_void_p, wintypes.BOOL, wintypes.LPCWSTR]
    api.CreateMutexW.restype = wintypes.HANDLE
    api.WaitForSingleObject.argtypes = [wintypes.HANDLE, wintypes.DWORD]
    api.ReleaseMutex.argtypes = [wintypes.HANDLE]
    api.CloseHandle.argtypes = [wintypes.HANDLE]
    handle = api.CreateMutexW(None, False, name)
    if not handle:
        raise Blocked("controller-lock-unavailable")
    owned = False
    try:
        if api.WaitForSingleObject(handle, 0) not in (0, 0x80):
            raise Blocked("controller-busy")
        owned = True
        yield
    finally:
        if owned and not api.ReleaseMutex(handle):
            raise Blocked("controller-lock-release-failed")
        api.CloseHandle(handle)


def vnc_password() -> str:
    secret, = lab._credential(lab.VM_PASSWORD_TARGET, guest=False)
    try:
        return base64.b64encode(hmac.new(secret.value.encode("utf-8"), HMAC_LABEL,
                                         hashlib.sha256).digest()).decode("ascii")[:8]
    finally:
        lab._wipe(secret)


def vnc_connection_types(api_module, client_module):
    """Keep disconnect scoped to this socket, including stalled RFB auth."""
    class Factory(client_module.VNCDoToolFactory):
        protocol = None

        def __init__(self):
            super().__init__()
            self._af_protocol = None
            self._af_close_requested = False
            self._af_close_scheduled = False
            self._af_closed = threading.Event()

        def clientConnectionLost(self, connector, reason):
            try:
                super().clientConnectionLost(connector, reason)
            finally:
                self._af_closed.set()

        def clientConnectionFailed(self, connector, reason):
            try:
                super().clientConnectionFailed(connector, reason)
            finally:
                self._af_closed.set()

    class Client(client_module.VNCDoToolClient):
        def connectionMade(self):
            self.factory._af_protocol = self
            super().connectionMade()
            if self.factory._af_close_requested and not self.factory._af_close_scheduled:
                self.factory._af_close_scheduled = True
                self.transport.abortConnection()

    Factory.protocol = Client

    class Proxy(api_module.ThreadedVNCClientProxy):
        def disconnect(self):
            factory = self.factory

            def close_connection():
                factory._af_close_requested = True
                if factory._af_closed.is_set() or factory._af_close_scheduled:
                    return
                protocol = factory._af_protocol
                if protocol is None:
                    return
                transport = getattr(protocol, "transport", None)
                if transport is None:
                    return
                factory._af_close_scheduled = True
                if self.protocol is not None:
                    transport.loseConnection()
                else:
                    transport.abortConnection()

            api_module.reactor.callFromThread(close_connection)
            factory._af_closed.wait(timeout=self.timeout)

    return Factory, Proxy


def connect_vnc():
    try:
        from vncdotool import api, client
        factory, proxy = vnc_connection_types(api, client)
        return api.connect(f"127.0.0.1::{lab.VM_CONSOLE_PORT}",
                           password=vnc_password(), factory_class=factory,
                           proxy=proxy, timeout=3)
    except BaseException:
        raise Blocked("console-connect-failed") from None


def screenshot(client, path: Path):
    temporary = path.with_name("." + path.name + "." + uuid.uuid4().hex)
    try:
        client.captureScreen(str(temporary), format="PNG")
        from PIL import Image
        with Image.open(temporary) as image:
            image.verify()
        with Image.open(temporary) as image:
            width, height = image.size
        if not (1 <= width <= 16384 and 1 <= height <= 16384):
            raise ValueError()
        os.replace(temporary, path)
        return (width, height)
    except BaseException:
        raise Blocked("screen-capture-failed") from None
    finally:
        temporary.unlink(missing_ok=True)


def game_identity(vm: tuple) -> dict:
    """Read live guest PID/start/path/hash with AutoFight's own guest client."""
    suffix = uuid.uuid4().hex
    guest_path = rf"C:\Users\Public\Documents\autofight-game-{suffix}.json"
    script = (
        "$ErrorActionPreference='Stop';"
        "$p=@(Get-Process -Name rift_x64 -ErrorAction Stop);"
        "if($p.Count -ne 1){throw 'game-count';};"
        "$path=$p[0].MainModule.FileName;"
        "$o=[ordered]@{gameProcessId=[int]$p[0].Id;"
        "gameProcessStartUtcTicks=[long]$p[0].StartTime.ToUniversalTime().Ticks;"
        "gameExecutablePath=[string]$path;"
        "gameExecutableSha256=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()};"
        f"$o|ConvertTo-Json -Compress|Set-Content -LiteralPath '{guest_path}' -Encoding ASCII"
    )
    cleanup = f"Remove-Item -LiteralPath '{guest_path}' -Force -ErrorAction SilentlyContinue"
    with tempfile.TemporaryDirectory(prefix="autofight-game-", dir=ARTIFACTS) as scratch:
        host = Path(scratch) / "identity.json"
        try:
            with lab.CachedVmwareGuest(vm) as guest:
                try:
                    guest.run("runProgramInGuest", lab.POWERSHELL, "-NoProfile",
                              "-NonInteractive", "-Command", script, timeout=60)
                    guest.run("copyFileFromGuestToHost", guest_path, str(host), timeout=60)
                finally:
                    try:
                        guest.run("runProgramInGuest", lab.POWERSHELL, "-NoProfile",
                                  "-NonInteractive", "-Command", cleanup, timeout=45)
                    except lab.Blocked:
                        pass  # Cleanup is best-effort; no permission to retry game input.
            result = read_json(host)
            if set(result) != {"gameProcessId", "gameProcessStartUtcTicks",
                               "gameExecutablePath", "gameExecutableSha256"}:
                raise Blocked("game-identity-invalid")
            return result
        except (lab.Blocked, Blocked):
            raise Blocked("game-identity-unavailable") from None


def require_game(vm: tuple, expected: dict):
    actual = game_identity(vm)
    for key in ("gameProcessId", "gameProcessStartUtcTicks",
                "gameExecutablePath", "gameExecutableSha256"):
        if actual.get(key) != expected[key]:
            raise Blocked("game-identity-drift")


def capture(output: Path, operation: dict) -> dict:
    operation = validate_operation(operation)
    expected = lab.load_game_identity(lab.DEFAULT_GAME_IDENTITY)
    directory = output_directory(output, create=True)
    with control_mutex():
        vm = lab.collect_identity()
        require_game(vm, expected)
        client = None
        try:
            client = connect_vnc()
            size = screenshot(client, directory / "review-screen.png")
            if operation == focus_titlebar_operation() and (
                    not isinstance(size, tuple) or len(size) != 2 or
                    type(size[0]) is not int or type(size[1]) is not int or
                    size[0] <= 120 or size[1] <= 8):
                raise Blocked("titlebar-target-outside-screen")
        finally:
            if client is not None:
                try:
                    client.disconnect()
                except BaseException:
                    pass
        if lab.direct_identity() != vm:
            raise Blocked("vm-identity-drift")
    plan = {"schemaVersion": 1, "captureId": uuid.uuid4().hex,
            "capturedAtUtc": iso(utc_now()),
            "screenSha256": lab.digest_file(directory / "review-screen.png"),
            "operation": operation, "vmIdentity": list(vm), "gameIdentity": expected}
    plan["planSha256"] = digest(plan)
    write_new(directory / "plan.json", plan)
    template = {"schemaVersion": 1, "captureId": plan["captureId"],
                "screenSha256": plan["screenSha256"], "planSha256": plan["planSha256"],
                "reviewedAtUtc": "", "decision": "denied",
                "gameForeground": False, "textEntryClosed": False,
                "chatEntryOpen": False, "chatEntryEmpty": False,
                "chatEntryFocused": False, "panelClosed": False,
                "otherControllerIdle": False, "queueIdle": False,
                "reviewer": "", "note": ""}
    if operation == focus_titlebar_operation():
        template.update({"riftWindowVisible": False, "titlebarTargetReviewed": False})
    write_new(directory / "review.json", template)
    return plan


def validate_plan(plan: dict, output: Path) -> dict:
    if set(plan) != {"schemaVersion", "captureId", "capturedAtUtc", "screenSha256",
                     "operation", "vmIdentity", "gameIdentity", "planSha256"} \
       or plan["schemaVersion"] != 1:
        raise Blocked("invalid-plan")
    if (type(plan["captureId"]) is not str or HEX32.fullmatch(plan["captureId"]) is None
        or type(plan["screenSha256"]) is not str or HEX64.fullmatch(plan["screenSha256"]) is None
        or type(plan["planSha256"]) is not str or HEX64.fullmatch(plan["planSha256"]) is None
        or digest({k:v for k,v in plan.items() if k != "planSha256"}) != plan["planSha256"]):
        raise Blocked("plan-changed")
    operation = validate_operation(plan["operation"])
    if type(plan["vmIdentity"]) is not list or len(plan["vmIdentity"]) != 7:
        raise Blocked("invalid-plan")
    expected = lab.load_game_identity(lab.DEFAULT_GAME_IDENTITY)
    if plan["gameIdentity"] != expected:
        raise Blocked("game-pin-changed")
    if lab.digest_file(output / "review-screen.png") != plan["screenSha256"]:
        raise Blocked("review-screen-changed")
    return operation


def validate_review(plan: dict, receipt: dict, operation: dict,
                    now: datetime | None = None) -> None:
    required = {"schemaVersion", "captureId", "screenSha256", "planSha256",
                "reviewedAtUtc", "decision", "gameForeground", "textEntryClosed",
                "chatEntryOpen", "chatEntryEmpty", "chatEntryFocused", "panelClosed",
                "otherControllerIdle", "queueIdle", "reviewer", "note"}
    focus = operation == focus_titlebar_operation()
    if focus:
        required.update({"riftWindowVisible", "titlebarTargetReviewed"})
    if not isinstance(receipt, dict) or set(receipt) != required or receipt["schemaVersion"] != 1:
        raise Blocked("review-invalid")
    if any(receipt[name] != plan[name] for name in ("captureId", "screenSha256", "planSha256")):
        raise Blocked("review-capture-mismatch")
    if (receipt["decision"] != "approved" or
        (not focus and receipt["gameForeground"] is not True) or
        (focus and (type(receipt["gameForeground"]) is not bool or
                    receipt["riftWindowVisible"] is not True or
                    receipt["titlebarTargetReviewed"] is not True))
        or receipt["panelClosed"] is not True
        or receipt["otherControllerIdle"] is not True or receipt["queueIdle"] is not True
        or receipt["reviewer"] != "root-agent" or type(receipt["note"]) is not str
        or not 1 <= len(receipt["note"]) <= 256):
        raise Blocked("review-not-approved")
    if operation in (open_chat_operation(), focus_titlebar_operation()):
        state_ok = (receipt["textEntryClosed"] is True and receipt["chatEntryOpen"] is False
                    and receipt["chatEntryEmpty"] is False
                    and receipt["chatEntryFocused"] is False)
    else:
        state_ok = (receipt["textEntryClosed"] is False and receipt["chatEntryOpen"] is True
                    and receipt["chatEntryEmpty"] is True
                    and receipt["chatEntryFocused"] is True)
    if not state_ok:
        raise Blocked("review-ui-mismatch")
    current = now or utc_now()
    captured = parse_time(plan["capturedAtUtc"])
    reviewed = parse_time(receipt["reviewedAtUtc"])
    if not captured <= reviewed <= current or not 0 <= (current-captured).total_seconds() <= AGE_LIMIT:
        raise Blocked("review-stale")


def inspect(output: Path):
    directory = output_directory(output, create=False)
    plan = read_json(directory / "plan.json")
    operation = validate_plan(plan, directory)
    review = read_json(directory / "review.json")
    validate_review(plan, review, operation)
    return plan, operation, review


def send_input(client, operation):
    operation = validate_operation(operation)
    if operation == focus_titlebar_operation():
        client.mouseMove(120, 8)
        client.mousePress(1)
        client.mouseUp(1)
    elif operation == open_chat_operation():
        client.keyPress("enter")
    else:
        for char in operation["command"]:
            if char == "-":
                key = "minus"
            elif "A" <= char <= "Z":
                key = "shift-" + char.lower()
            else:
                key = char
            client.keyPress(key)
        client.keyPress("enter")


def release_keys(client, operation) -> bool:
    keys = {"enter", "esc", "shift", "ctrl", "alt", "super"}
    if operation["type"] == "command":
        keys.update("minus" if c == "-" else c for c in operation["command"])
    okay = True
    if operation == focus_titlebar_operation():
        try:
            client.mouseUp(1)
        except BaseException:
            okay = False
    for key in sorted(keys):
        try:
            client.keyUp(key)
        except BaseException:
            okay = False
    return okay


def execute(output: Path) -> dict:
    directory = output_directory(output, create=False)
    plan, operation, review = inspect(directory)
    report_path = directory / "execution.json"
    if report_path.exists():
        raise Blocked("operation-already-attempted")
    report = {"schemaVersion": 1, "captureId": plan["captureId"],
              "startedAtUtc": iso(utc_now()), "status": "blocked",
              "inputMayHaveBeenSent": True, "inputSent": False,
              "releaseSucceeded": False}
    # On an abrupt process kill this durable marker must conservatively
    # describe possible input, even if the final replacement never occurs.
    write_new(report_path, report)
    input_attempted = False
    try:
        with control_mutex():
            vm = lab.collect_identity()
            if json.loads(json.dumps(vm)) != plan["vmIdentity"]:
                raise Blocked("vm-identity-drift")
            require_game(vm, plan["gameIdentity"])
            client = None
            try:
                client = connect_vnc()
                validate_review(plan, review, operation)
                if lab.direct_identity() != vm:
                    raise Blocked("vm-identity-drift")
                input_attempted = True
                send_input(client, operation)
                report["inputSent"] = True
                time.sleep(0.5)
                screenshot(client, directory / "after-screen.png")
            finally:
                if client is not None:
                    report["releaseSucceeded"] = release_keys(client, operation)
                    try:
                        client.disconnect()
                    except BaseException:
                        pass
            if not report["releaseSucceeded"]:
                raise Blocked("input-release-uncertain")
            if lab.direct_identity() != vm:
                raise Blocked("vm-identity-drift")
            require_game(vm, plan["gameIdentity"])
        report["status"] = "completed"
    except (Blocked, lab.Blocked) as exc:
        report["reason"] = str(exc)
    except BaseException:
        report["reason"] = "console-operation-failed"
    if not input_attempted:
        report["inputMayHaveBeenSent"] = False  # Pre-input failure is known safe.
    report["finishedAtUtc"] = iso(utc_now())
    temp = directory / ("execution." + uuid.uuid4().hex + ".tmp")
    try:
        write_new(temp, report)
        os.replace(temp, report_path)
    finally:
        temp.unlink(missing_ok=True)
    return report


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    modes = parser.add_mutually_exclusive_group(required=True)
    modes.add_argument("--capture", action="store_true")
    modes.add_argument("--review", action="store_true")
    modes.add_argument("--execute", action="store_true")
    parser.add_argument("--output", type=Path, required=True)
    choices = parser.add_mutually_exclusive_group()
    choices.add_argument("--command")
    choices.add_argument("--open-chat", action="store_true")
    choices.add_argument("--focus-titlebar", action="store_true")
    args = parser.parse_args(argv)
    try:
        if args.capture:
            if sum((args.command is not None, args.open_chat, args.focus_titlebar)) != 1:
                raise Blocked("exactly-one-operation-required")
            op = (operation_for_command(args.command) if args.command is not None
                  else open_chat_operation() if args.open_chat
                  else focus_titlebar_operation())
            result = capture(args.output, op)
            print(json.dumps({"status": "captured", "captureId": result["captureId"],
                              "output": str(args.output)}))
        else:
            if args.command is not None or args.open_chat or args.focus_titlebar:
                raise Blocked("operation-only-allowed-for-capture")
            if args.review:
                plan, _, _ = inspect(args.output)
                print(json.dumps({"status": "reviewed", "captureId": plan["captureId"]}))
            else:
                result = execute(args.output)
                print(json.dumps(result, sort_keys=True))
                return 0 if result["status"] == "completed" else 2
        return 0
    except (Blocked, lab.Blocked) as exc:
        print(json.dumps({"status": "blocked", "reason": str(exc)}))
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
