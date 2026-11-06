"""Lab 2 one-shot private AutoFight addon bind through reviewed native chat.

A denied-by-default screenshot review must attest that RIFT is foregrounded,
other controllers idle, inventory/panels closed, and *empty native chat focused*.
Only the exact /afguide bind lab2 command is possible. A fresh Reader snapshot
is checked at capture and again before sending. No nonce in CLI arguments,
saved review/plan/execution, or stdout. No after-screen: chat may echo nonce.
A completed send does not establish binding success, live XYZ or movement.
"""
from __future__ import annotations

import argparse
from contextlib import contextmanager
import hashlib
import json
from pathlib import Path
import re
import sys
import time
from uuid import UUID, uuid4

import bootstrap_lab1_host_telemetry as host_json
import fetch_lab2_guest_reader_snapshot as reader
import lab1_chat_console as base
import lab1_guest_deploy as lab
import lab2_chat_status as status


PRIVATE = reader.PRIVATE
HOST_CONFIG = PRIVATE / "host-lab2.local.json"
PREFIX = "/afguide bind lab2 "
STAGE = re.compile(r"[0-9a-f]{32}\Z", re.ASCII)
HEX64 = re.compile(r"[0-9a-f]{64}\Z", re.ASCII)
OP_KEYS = {
    "type", "step", "stageId", "readerPid", "readerStartTicks",
    "readerInstanceId", "nonceSha256",
}
RELEASE = tuple(sorted({"space" if ch == " " else ch
                        for ch in PREFIX + "0123456789abcdef"} |
                       {"enter", "esc", "shift", "ctrl", "alt", "super"}))
KNOWN_ERRORS = {
    "operation-already-attempted", "invalid-output-directory", "invalid-plan",
    "plan-changed", "invalid-json-file", "review-not-approved", "review-invalid",
    "review-stale", "review-ui-mismatch", "review-capture-mismatch",
    "review-screen-changed", "invalid-review-time", "game-pin-changed",
    "game-identity-drift", "game-identity-unavailable", "vm-identity-drift",
    "console-connect-failed", "console-operation-failed", "controller-busy",
    "controller-lock-unavailable", "input-release-uncertain",
    "lab2-bind-reader-drift", "lab2-bind-host-config-mismatch",
    "lab2-bind-snapshot-unavailable", "lab2-bind-invalid-operation",
    "lab2-bind-stage-invalid", "lab2-bind-reader-id-invalid",
    "lab2-bind-arguments-invalid",
}


def safe_reason(exc: BaseException) -> str:
    """Only fixed, pre-reviewed codes may reach persistent or console output."""
    value = str(exc)
    return value if value in KNOWN_ERRORS else "lab2-bind-preflight-blocked"


def validate_operation(value: object) -> dict:
    if type(value) is not dict or set(value) != OP_KEYS:
        raise base.Blocked("lab2-bind-invalid-operation")
    if value["type"] != "ui-step" or value["step"] != "bind-lab2":
        raise base.Blocked("lab2-bind-invalid-operation")
    if (type(value["stageId"]) is not str or
            STAGE.fullmatch(value["stageId"]) is None or
            type(value["readerPid"]) is not int or
            not 0 < value["readerPid"] <= 0xffffffff or
            type(value["readerStartTicks"]) is not int or
            value["readerStartTicks"] <= 0 or
            type(value["nonceSha256"]) is not str or
            HEX64.fullmatch(value["nonceSha256"]) is None):
        raise base.Blocked("lab2-bind-invalid-operation")
    try:
        instance = value["readerInstanceId"]
        if type(instance) is not str or str(UUID(instance)) != instance:
            raise ValueError()
    except (ValueError, TypeError, AttributeError):
        raise base.Blocked("lab2-bind-invalid-operation") from None
    return dict(value)


def send_disabled(_client, _operation):
    """Never call the generic executor: it creates an after-screen PNG."""
    raise base.Blocked("lab2-bind-invalid-operation")


def release_keys(client, operation: dict) -> bool:
    try:
        validate_operation(operation)
    except base.Blocked:
        return False
    okay = True
    for key in RELEASE:
        try:
            client.keyUp(key)
        except BaseException:
            okay = False
    return okay


@contextmanager
def lab2_bind_scope():
    # The status scope owns the exact Lab 2 VM/game pin/guest credentials.
    with status.lab2_scope():
        original = base.validate_operation, base.send_input, base.release_keys
        try:
            base.validate_operation = validate_operation
            base.send_input = send_disabled
            base.release_keys = release_keys
            yield
        finally:
            base.validate_operation, base.send_input, base.release_keys = original


def fresh_private_binding(stage_id: str, reader_pid: int,
                          reader_start: int) -> tuple[str, str]:
    """Verify guest process/game twice, private stage/addon hashes, 30s TTL.

    The fresh raw snapshot is held in ignored artifacts/local only until parsed;
    this call removes only the new, invocation-owned file, never guest files.
    """
    if type(stage_id) is not str or STAGE.fullmatch(stage_id) is None:
        raise base.Blocked("lab2-bind-stage-invalid")
    if (type(reader_pid) is not int or not 0 < reader_pid <= 0xffffffff or
            type(reader_start) is not int or reader_start <= 0):
        raise base.Blocked("lab2-bind-reader-id-invalid")
    output = PRIVATE / ("lab2-guest-latest-" + uuid4().hex + ".local.json")
    try:
        binding = reader.load_binding(stage_id)
        reader.fetch(reader_pid, reader_start, binding, output)
        snapshot = host_json.read_json(output, reader.MAX_SNAPSHOT_BYTES)
        metadata = reader.verify_snapshot(snapshot, binding)
        instance, nonce = metadata["readerInstanceId"], snapshot["nonce"]
        if (type(nonce) is not str or STAGE.fullmatch(nonce) is None or
                snapshot["target"]["readerSessionNonce"] != nonce):
            raise base.Blocked("lab2-bind-reader-drift")
        # The owned host receiver's private config must target the same Reader.
        config = host_json.read_json(HOST_CONFIG, 16 * 1024)
        expected = {
            "lab": "lab2", "readerInstanceId": instance, "readerNonce": nonce,
            "gameProcessId": binding.game_pin["gameProcessId"],
            "gameProcessStartUtcTicks": binding.game_pin["gameProcessStartUtcTicks"],
            "gameExecutablePath": binding.game_pin["gameExecutablePath"],
            "gameExecutableSha256": binding.game_pin["gameExecutableSha256"],
        }
        current = config.get("binding")
        if type(current) is not dict or any(
                current.get(key) != value for key, value in expected.items()):
            raise base.Blocked("lab2-bind-host-config-mismatch")
        return instance, nonce
    except (reader.FetchBlocked, reader.lab2.Blocked, lab.Blocked,
            host_json.Blocked, OSError):
        raise base.Blocked("lab2-bind-snapshot-unavailable") from None
    finally:
        try:
            output.unlink(missing_ok=True)
        except OSError:
            pass


def capture(output: Path, stage_id: str, reader_pid: int,
            reader_start: int) -> dict:
    instance, nonce = fresh_private_binding(stage_id, reader_pid, reader_start)
    operation = validate_operation({
        "type": "ui-step", "step": "bind-lab2",
        "stageId": stage_id, "readerPid": reader_pid,
        "readerStartTicks": reader_start, "readerInstanceId": instance,
        "nonceSha256": hashlib.sha256(nonce.encode("ascii")).hexdigest(),
    })
    with lab2_bind_scope():
        # Screenshot + a DENIED review template, with no mouse/keyboard input.
        return base.capture(output, operation)


def send_private(client, operation: dict, nonce: str) -> None:
    validate_operation(operation)
    if type(nonce) is not str or STAGE.fullmatch(nonce) is None:
        raise base.Blocked("lab2-bind-reader-drift")
    for char in PREFIX + nonce:
        client.keyPress("space" if char == " " else char)
    client.keyPress("enter")


def finalize(directory: Path, report: dict) -> None:
    temporary = directory / ("execution." + uuid4().hex + ".tmp")
    try:
        base.write_new(temporary, report)
        temporary.replace(directory / "execution.json")
    finally:
        temporary.unlink(missing_ok=True)


def execute(output: Path) -> dict:
    with lab2_bind_scope():
        directory = base.output_directory(output, create=False)
        plan, operation, review = base.inspect(directory)
        receipt = directory / "execution.json"
        if receipt.exists():
            raise base.Blocked("operation-already-attempted")
        report = {
            "schemaVersion": 1, "captureId": plan["captureId"],
            "startedAtUtc": base.iso(base.utc_now()), "status": "blocked",
            "inputMayHaveBeenSent": True, "inputSent": False,
            "releaseSucceeded": False,
        }
        # Written before connection; abrupt process death is conservatively
        # a possible-input outcome and cannot silently retry.
        base.write_new(receipt, report)
        attempted = False
        try:
            with base.control_mutex():
                vm = lab.collect_identity()
                if json.loads(json.dumps(vm)) != plan["vmIdentity"]:
                    raise base.Blocked("vm-identity-drift")
                base.require_game(vm, plan["gameIdentity"])
                instance, nonce = fresh_private_binding(
                    operation["stageId"], operation["readerPid"],
                    operation["readerStartTicks"]
                )
                if (instance != operation["readerInstanceId"] or
                        hashlib.sha256(nonce.encode("ascii")).hexdigest() !=
                        operation["nonceSha256"]):
                    raise base.Blocked("lab2-bind-reader-drift")
                client = None
                try:
                    client = base.connect_vnc()
                    base.validate_review(plan, review, operation)
                    if lab.direct_identity() != vm:
                        raise base.Blocked("vm-identity-drift")
                    attempted = True
                    send_private(client, operation, nonce)
                    report["inputSent"] = True
                    time.sleep(0.5)
                    # NO AFTER SCREEN: native chat history might show nonce.
                finally:
                    if client is not None:
                        report["releaseSucceeded"] = release_keys(client, operation)
                        try:
                            client.disconnect()
                        except BaseException:
                            pass
                if not report["releaseSucceeded"]:
                    raise base.Blocked("input-release-uncertain")
                if lab.direct_identity() != vm:
                    raise base.Blocked("vm-identity-drift")
                base.require_game(vm, plan["gameIdentity"])
            report["status"] = "completed"
        except (base.Blocked, lab.Blocked) as exc:
            report["reason"] = safe_reason(exc)
        except BaseException:
            report["reason"] = "console-operation-failed"
        if not attempted:
            report["inputMayHaveBeenSent"] = False
        report["finishedAtUtc"] = base.iso(base.utc_now())
        finalize(directory, report)
        return report


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    modes = parser.add_mutually_exclusive_group(required=True)
    modes.add_argument("--capture", action="store_true")
    modes.add_argument("--review", action="store_true")
    modes.add_argument("--execute", action="store_true")
    parser.add_argument("--output", required=True, type=Path)
    parser.add_argument("--stage-id")
    parser.add_argument("--reader-pid", type=int)
    parser.add_argument("--reader-start-ticks", type=int)
    args = parser.parse_args(argv)
    try:
        identifiers = (args.stage_id is not None, args.reader_pid is not None,
                       args.reader_start_ticks is not None)
        if (args.capture and not all(identifiers)) or (
                not args.capture and any(identifiers)):
            raise base.Blocked("lab2-bind-arguments-invalid")
        if args.capture:
            plan = capture(args.output, args.stage_id,
                           args.reader_pid, args.reader_start_ticks)
            print(json.dumps({"status": "captured", "lab": "lab2",
                              "captureId": plan["captureId"],
                              "output": str(args.output)}))
            return 0
        if args.review:
            with lab2_bind_scope():
                plan, _, _ = base.inspect(args.output)
            print(json.dumps({"status": "reviewed", "lab": "lab2",
                              "captureId": plan["captureId"]}))
            return 0
        report = execute(args.output)
        print(json.dumps(report, sort_keys=True))
        return 0 if report["status"] == "completed" else 2
    except BaseException as exc:
        # Never serialize arbitrary guest, VNC, JSON or OS exceptions.
        print(json.dumps({"status": "blocked", "lab": "lab2",
                          "reason": safe_reason(exc)}))
        return 2


if __name__ == "__main__":
    sys.exit(main())
