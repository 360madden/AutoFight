"""AutoFight Lab2-only, reviewed, two-step RIFT native chat status probe.

Reuse AutoFight's tested Lab1 screenshot/review, exact VM/game identity, console
mutex, one-shot execution receipt, VNC disconnect and key-release machinery.
The only possible operations are:
  --capture --open-chat: prepare reviewed ONE Enter to open native RIFT chat;
  --capture: prepare reviewed EXACT /afguide status plus Enter.
Each operation needs a distinct fresh (<60 second) screenshot, explicit review,
and separately authorized --execute. Never infer chat focus from merely sending
Enter. Review the after-screen, then capture again for the status command.

No mouse clicks, titlebar focus, reloads, binding, arbitrary commands or retries.
Neither importing this module nor --capture/--review sends gameplay input.
Do not execute either operation in Lab2 without coordinator approval.
"""
from __future__ import annotations

import argparse
from contextlib import contextmanager
import json
from pathlib import Path
import re
import threading

import lab1_chat_console as base
import lab1_guest_deploy as lab


LAB2_VMX = Path(r"C:\rift modding\VMs\RIFT-Lab-2\RIFT-Lab.vmx")
LAB2_PORT = 5962
LAB2_MACHINE = "RIFT-LAB-2"
LAB2_PIN = lab.ROOT / "artifacts" / "local" / "lab2-game-identity.local.json"
STATUS_COMMAND = "/afguide status"
STATUS_OPERATION = {"type": "command", "command": STATUS_COMMAND}
OPEN_CHAT_OPERATION = {"type": "ui-step", "step": "open-chat"}
STATUS_KEYS = tuple("space" if character == " " else character
                    for character in STATUS_COMMAND)
RELEASE_KEYS = tuple(sorted(set(STATUS_KEYS) |
                                {"enter", "esc", "shift", "ctrl", "alt", "super"}))
OPEN_CHAT_RELEASE_KEYS = ("alt", "ctrl", "enter", "esc", "shift", "super")
_SCOPE_MUTEX = threading.Lock()


def validate_lab2_operation(operation: object) -> dict:
    """Allow exactly native-chat Enter or fixed status, no other Lab1 UI."""
    if type(operation) is not dict:
        raise base.Blocked("lab2-reviewed-operation-required")
    if operation == OPEN_CHAT_OPERATION:
        return dict(OPEN_CHAT_OPERATION)
    if operation == STATUS_OPERATION:
        return dict(STATUS_OPERATION)
    raise base.Blocked("lab2-reviewed-operation-required")


def load_lab2_pin(path: Path) -> dict:
    """Review exact private Lab2 game pin; do not infer a pin from historic PID."""
    try:
        target = path.resolve(strict=True)
        root = (lab.ROOT / "artifacts" / "local").resolve(strict=True)
        if target.parent != root or target.name != LAB2_PIN.name:
            raise ValueError()
        data = lab._read_json(target, 4096)
        if (type(data) is not dict or set(data) != {
                "schemaVersion", "lab", "machine", "gameProcessId",
                "gameProcessStartUtcTicks", "gameExecutablePath",
                "gameExecutableSha256",
            } or type(data["schemaVersion"]) is not int
                or data["schemaVersion"] != 1
                or data["lab"] != "lab2" or data["machine"] != LAB2_MACHINE
                or type(data["gameProcessId"]) is not int
                or not 0 < data["gameProcessId"] <= 0xffffffff
                or type(data["gameProcessStartUtcTicks"]) is not int
                or data["gameProcessStartUtcTicks"] <= 0
                or type(data["gameExecutablePath"]) is not str
                or re.fullmatch(r"[A-Za-z]:\\[^'\x00\r\n]*\\rift_x64\.exe",
                                data["gameExecutablePath"], re.I) is None
                or type(data["gameExecutableSha256"]) is not str
                or re.fullmatch(r"[a-f0-9]{64}",
                                data["gameExecutableSha256"]) is None):
            raise ValueError()
        return data
    except (OSError, ValueError, lab.Blocked):
        raise base.Blocked("lab2-game-pin-invalid") from None


def send_lab2_input(client, operation: dict) -> None:
    operation = validate_lab2_operation(operation)
    if operation == OPEN_CHAT_OPERATION:
        client.keyPress("enter")
        return
    for key in STATUS_KEYS:
        client.keyPress(key)
    client.keyPress("enter")


def release_lab2_keys(client, operation: dict) -> bool:
    """Best-effort release all keys potentially owned by either narrow action."""
    try:
        operation = validate_lab2_operation(operation)
    except base.Blocked:
        return False
    keys = (OPEN_CHAT_RELEASE_KEYS if operation == OPEN_CHAT_OPERATION
            else RELEASE_KEYS)
    okay = True
    for key in keys:
        try:
            client.keyUp(key)
        except BaseException:
            okay = False
    return okay


@contextmanager
def lab2_scope():
    """Temporarily bind AutoFight's own generic primitives to exactly Lab2.

    The wrapper is a one-operation CLI process. Serialize temporary module
    binding and always restore original values, including after exceptions.
    The legacy Lab1 guest-credential fallback is explicitly disabled.
    """
    changes = (
        (lab, "VMX", LAB2_VMX),
        (lab, "VM_CONSOLE_PORT", LAB2_PORT),
        (lab, "VM_PASSWORD_TARGET", "VMware Encrypted VM: " + str(LAB2_VMX)),
        (lab, "GUEST_PASSWORD_TARGET", "RiftReader2 Guest Windows: " + str(LAB2_VMX)),
        (lab, "LEGACY_GUEST_TARGET", "RiftReader2 Guest Windows: " + str(LAB2_VMX)),
        (lab, "DEFAULT_GAME_IDENTITY", LAB2_PIN),
        (lab, "load_game_identity", load_lab2_pin),
        (base, "validate_operation", validate_lab2_operation),
        (base, "send_input", send_lab2_input),
        (base, "release_keys", release_lab2_keys),
    )
    with _SCOPE_MUTEX:
        originals = []
        try:
            for module, name, new_value in changes:
                originals.append((module, name, getattr(module, name)))
                setattr(module, name, new_value)
            yield
        finally:
            for module, name, old_value in reversed(originals):
                setattr(module, name, old_value)


def main(argv=None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    modes = parser.add_mutually_exclusive_group(required=True)
    modes.add_argument("--capture", action="store_true",
                       help="Capture Lab2 screenshot and a DENIED review template")
    modes.add_argument("--review", action="store_true",
                       help="Validate the exact manually completed review and its TTL")
    modes.add_argument("--execute", action="store_true",
                       help="One independently reviewed action; NEVER auto-send")
    parser.add_argument("--open-chat", action="store_true",
                        help="Only with --capture: review single Enter to open RIFT chat")
    parser.add_argument("--output", required=True, type=Path,
                        help="One new immediate child of AutoFight/artifacts/local")
    args = parser.parse_args(argv)
    try:
        if args.open_chat and not args.capture:
            raise base.Blocked("open-chat-only-allowed-for-capture")
        with lab2_scope():
            if args.capture:
                operation = OPEN_CHAT_OPERATION if args.open_chat else STATUS_OPERATION
                plan = base.capture(args.output, dict(operation))
                print(json.dumps({"status": "captured", "captureId": plan["captureId"],
                                  "output": str(args.output), "lab": "lab2"}))
                return 0
            if args.review:
                plan, operation, review = base.inspect(args.output)
                validate_lab2_operation(operation)
                print(json.dumps({"status": "reviewed", "captureId": plan["captureId"],
                                  "lab": "lab2"}))
                return 0
            report = base.execute(args.output)
            print(json.dumps(report, sort_keys=True))
            return 0 if report["status"] == "completed" else 2
    except (base.Blocked, lab.Blocked) as error:
        print(json.dumps({"status": "blocked", "reason": str(error)}))
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
