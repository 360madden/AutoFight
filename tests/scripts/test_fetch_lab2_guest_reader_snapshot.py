"""Offline/synthetic tests for strictly Lab2-only GuestReader snapshot fetch."""
from __future__ import annotations

from contextlib import redirect_stdout
from datetime import datetime, timedelta, timezone
import hashlib
import io
import json
from pathlib import Path
import sys
import tempfile
import unittest
from unittest import mock

SCRIPTS = Path(__file__).resolve().parents[2] / "scripts"
sys.path.insert(0, str(SCRIPTS))
import fetch_lab2_guest_reader_snapshot as fetcher  # noqa: E402

STAGE_ID = "a" * 32
SHA = "b" * 64
SHA_EXE = "c" * 64
SHA_CONFIG = "d" * 64
SHA_MANIFEST = "e" * 64
NONCE = "f" * 32
INSTANCE = "123e4567-e89b-12d3-a456-426614174000"
NOW = datetime(2030, 1, 1, tzinfo=timezone.utc)
GAME_PATH = r"C:\RIFT_TRANSFER\RIFT\rift_x64.exe"


def game_pin(*, lab="lab2", machine="RIFT-LAB-2"):
    return {
        "schemaVersion": 1, "lab": lab, "machine": machine,
        "gameProcessId": 9752, "gameProcessStartUtcTicks": 639294897866244204,
        "gameExecutablePath": GAME_PATH, "gameExecutableSha256": SHA,
    }


def binding(pin=None):
    return fetcher.Binding(
        stage_id=STAGE_ID, reader_sha256=SHA_EXE,
        config_sha256=SHA_CONFIG, game_pin_sha256=SHA,
        stage_receipt_sha256=SHA_MANIFEST,
        game_pin=game_pin() if pin is None else pin,
        addon_hashes=("1" * 64,) * 6
    )


def snapshot(pin=None, stamp=NOW):
    pin = pin or game_pin()
    return {
        "schemaVersion": 1, "instanceId": INSTANCE, "nonce": NONCE,
        "sequence": 10, "capturedAtUtc": stamp.isoformat(),
        "monotonicMilliseconds": 22.5, "readerStatus": "running",
        "readerReason": None,
        "target": {
            "processId": pin["gameProcessId"],
            "processStartUtcTicks": pin["gameProcessStartUtcTicks"],
            "executablePath": pin["gameExecutablePath"],
            "executableSha256": pin["gameExecutableSha256"],
            "readerSessionNonce": NONCE
        },
        "riftReader2": {"schemaVersion": 1},
        "delivery": {
            "enabled": True, "status": "succeeded",
            "endpoint": "http://192.168.56.1:48761/observation",
            "lastSuccessUtc": None, "error": None
        },
    }


class FakeGuest:
    def __init__(self, data, fail_second=False):
        self.data = data
        self.fail_second = fail_second
        self.calls = []

    def __enter__(self):
        return self

    def __exit__(self, *_):
        return None

    def run(self, command, *args, **kwargs):
        self.calls.append((command, args))
        if command == "copyFileFromGuestToHost":
            assert args[0] == fetcher.DEFAULT_SNAPSHOT
            Path(args[1]).write_text(json.dumps(self.data), encoding="utf-8")
        elif command == "runProgramInGuest":
            assert args[:3] == (
                fetcher.base.POWERSHELL, "-NoProfile", "-NonInteractive"
            )
            assert args[3] == "-Command"
            if self.fail_second and sum(x[0] == "runProgramInGuest" for x in self.calls) == 2:
                raise fetcher.base.Blocked("guest-operation-failed:runProgramInGuest")
        else:
            raise AssertionError("forbidden guest action: " + command)


class Lab2FetchSpecs(unittest.TestCase):
    def assertBlocked(self, expected, action):
        with self.assertRaises((fetcher.lab2.Blocked,
                                fetcher.common.FetchBlocked,
                                fetcher.host_json.Blocked)) as caught:
            action()
        self.assertEqual(str(caught.exception), expected)

    def test_exact_reader_game_machine_assertion_read_only(self):
        source = fetcher.identity_powershell(3214, 639294897866000001, binding())
        for needed in (
            "RIFT-LAB-2", "Get-Process", "Get-FileHash",
            "Rift.Automation.GuestReader.exe", "competing-rr2-reader",
            "reader-executable-hash-mismatch", "game-hash-mismatch",
            "3214", "639294897866000001", "9752",
        ):
            self.assertIn(needed, source)
        for forbidden in (
            "Start-Process", "Stop-Process", "Set-Content", "Remove-Item",
            "SendKeys", "Set-Item", "Restart-Computer", "Start-VM",
        ):
            self.assertNotIn(forbidden, source)

    def test_lab1_machine_pin_rejected_before_guest_access(self):
        self.assertBlocked(
            "lab2-reader-binding-invalid",
            lambda: fetcher.identity_powershell(
                3214, 639294897866000001,
                binding(game_pin(lab="lab1", machine="RIFT-LAB-1")))
        )

    def test_invalid_reader_pid_start_sha_and_injection(self):
        for pid, start in ((0, 1), (1, 0), (True, 1)):
            self.assertBlocked(
                "reader-process-pin-invalid",
                lambda p=pid, s=start: fetcher.identity_powershell(
                    p, s, binding()))
        bad = binding()
        bad = fetcher.Binding(**{**bad.__dict__, "reader_sha256": "broken"})
        self.assertBlocked(
            "lab2-reader-binding-invalid",
            lambda: fetcher.identity_powershell(1, 2, bad))
        pin = game_pin()
        pin["gameExecutablePath"] = GAME_PATH + "';Stop-Process"
        self.assertBlocked(
            "lab2-reader-binding-invalid",
            lambda: fetcher.identity_powershell(1, 2, binding(pin)))

    def test_good_schema_and_redacted_nonce(self):
        out = fetcher.verify_snapshot(snapshot(), binding(), now=NOW)
        self.assertEqual(out["readerNonce"], "[redacted]")
        self.assertEqual(out["sequence"], 10)
        self.assertEqual(out["gameProcessId"], 9752)
        self.assertNotIn(NONCE, json.dumps(out))

    def test_stale_future_and_terminated_snapshots_fail(self):
        for when in (NOW - timedelta(seconds=31), NOW + timedelta(seconds=6)):
            self.assertBlocked(
                "snapshot-stale-or-clock-skew",
                lambda t=when: fetcher.verify_snapshot(
                    snapshot(stamp=t), binding(), now=NOW))
        old = snapshot()
        old["readerStatus"] = "terminated"
        self.assertBlocked(
            "reader-not-running",
            lambda: fetcher.verify_snapshot(old, binding(), now=NOW))

    def test_mismatched_game_start_hash_or_nonce_rejected(self):
        for key, replacement, message in (
            ("processId", 9753, "snapshot-game-identity-mismatch"),
            ("processStartUtcTicks", 1, "snapshot-game-identity-mismatch"),
            ("executableSha256", "1" * 64, "snapshot-game-or-reader-identity-mismatch"),
            ("readerSessionNonce", "1" * 32, "snapshot-game-or-reader-identity-mismatch"),
        ):
            item = snapshot()
            item["target"][key] = replacement
            self.assertBlocked(
                message,
                lambda i=item: fetcher.verify_snapshot(i, binding(), now=NOW))

    def test_schema_unknown_missing_invalid_fields_fail_closed(self):
        changes = (
            (lambda x: x.update({"token": "SECRET"}), "snapshot-schema-invalid"),
            (lambda x: x.pop("delivery"), "snapshot-schema-invalid"),
            (lambda x: x.update({"monotonicMilliseconds": float("nan")}),
             "snapshot-monotonic-invalid"),
            (lambda x: x.update({"readerReason": ["wrong"]}),
             "snapshot-reason-invalid"),
            (lambda x: x["target"].update({"extra": True}),
             "snapshot-target-schema-invalid"),
            (lambda x: x["riftReader2"].update({"schemaVersion": 2}),
             "snapshot-rr2-schema-invalid"),
            (lambda x: x["delivery"].update({"token": "SECRET"}),
             "snapshot-delivery-schema-invalid"),
        )
        for change, expected in changes:
            item = snapshot()
            change(item)
            self.assertBlocked(
                expected,
                lambda i=item: fetcher.verify_snapshot(i, binding(), now=NOW))

    def test_reject_invalid_stage_before_any_filesystem_or_vm(self):
        for value in ("", "A" * 32, "a" * 31, "../" + "a" * 29):
            self.assertBlocked(
                "lab2-stage-id-invalid",
                lambda v=value: fetcher.load_binding(v))

    def test_stage_receipt_binds_all_source_game_and_config_hashes(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            pin = game_pin()
            pin_path = root / "lab2-game-identity.local.json"
            pin_bytes = json.dumps(pin).encode("utf-8")
            pin_path.write_bytes(pin_bytes)
            files = [
                {"relativePath": fetcher.lab2.EXE,
                 "sha256": SHA_EXE, "size": 734},
                {"relativePath": fetcher.lab2.GUEST_CONFIG,
                 "sha256": SHA_CONFIG, "size": 329},
            ]
            addons = [{"relativePath": "RiftAddon.toc", "sha256": "1" * 64, "size": 1}]
            receipt = {
                "schemaVersion": 1,
                "kind": "autofight-lab2-reader-stage-receipt",
                "stageId": STAGE_ID, "manifestSha256": SHA_MANIFEST,
                "gamePinSha256": hashlib.sha256(pin_bytes).hexdigest(),
                "guestConfigSha256": SHA_CONFIG,
                "readerExeSha256": SHA_EXE,
                "addonFiles": addons,
            }
            stage_path = root / ("lab2-stage-" + STAGE_ID + ".local.json")
            stage_path.write_text(json.dumps(receipt), encoding="utf-8")
            with mock.patch.object(fetcher, "PRIVATE", root), \
                 mock.patch.object(fetcher, "DEFAULT_GAME_PIN", pin_path), \
                 mock.patch.object(fetcher.lab2, "validate_host_inputs",
                                   return_value=({}, files, addons)):
                good = fetcher.load_binding(STAGE_ID, pin_path)
                self.assertEqual(good.reader_sha256, SHA_EXE)
                self.assertEqual(good.game_pin["machine"], "RIFT-LAB-2")
                receipt["guestConfigSha256"] = "a" * 64
                stage_path.write_text(json.dumps(receipt), encoding="utf-8")
                self.assertBlocked(
                    "lab2-private-stage-or-source-drift",
                    lambda: fetcher.load_binding(STAGE_ID, pin_path))
                receipt["guestConfigSha256"] = SHA_CONFIG
                stage_path.write_text(json.dumps(receipt), encoding="utf-8")
                pin["lab"], pin["machine"] = "lab1", "RIFT-LAB-1"
                pin_path.write_text(json.dumps(pin), encoding="utf-8")
                self.assertBlocked(
                    "lab2-game-pin-invalid",
                    lambda: fetcher.load_binding(STAGE_ID, pin_path))

    def test_fetch_has_two_gates_one_copy_and_absent_only_output(self):
        data = snapshot(stamp=datetime.now(timezone.utc))
        fake = FakeGuest(data)
        vm_identity = ("pinned-lab2-vm",)
        bind = binding()
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            output = root / ("lab2-guest-latest-" + STAGE_ID + ".local.json")
            with mock.patch.object(fetcher, "PRIVATE", root), \
                 mock.patch.object(fetcher.lab2, "configure_lab2_transport") as configure, \
                 mock.patch.object(fetcher.base, "collect_identity",
                                   return_value=vm_identity), \
                 mock.patch.object(fetcher.base, "CachedVmwareGuest",
                                   return_value=fake), \
                 mock.patch.object(fetcher, "load_binding", return_value=bind):
                result = fetcher.fetch(3214, 639294897866000001, bind, output)
            self.assertEqual([x[0] for x in fake.calls], [
                "runProgramInGuest", "copyFileFromGuestToHost", "runProgramInGuest"
            ])
            self.assertEqual(result["readerNonce"], "[redacted]")
            configure.assert_called_once_with()
            self.assertTrue(output.is_file())
            self.assertEqual(json.loads(output.read_text())["nonce"], NONCE)

    def test_failure_after_copy_never_publishes(self):
        data = snapshot(stamp=datetime.now(timezone.utc))
        fake = FakeGuest(data, fail_second=True)
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            output = root / ("lab2-guest-latest-" + STAGE_ID + ".local.json")
            with mock.patch.object(fetcher, "PRIVATE", root), \
                 mock.patch.object(fetcher.lab2, "configure_lab2_transport"), \
                 mock.patch.object(fetcher.base, "collect_identity",
                                   return_value=("pinned",)), \
                 mock.patch.object(fetcher.base, "CachedVmwareGuest",
                                   return_value=fake):
                with self.assertRaises(fetcher.base.Blocked):
                    fetcher.fetch(3214, 639294897866000001, binding(), output)
            self.assertFalse(output.exists())

    def test_snapshot_expires_during_second_gate_never_publishes(self):
        data = snapshot(stamp=datetime.now(timezone.utc))
        fake = FakeGuest(data)
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            output = root / ("lab2-guest-latest-" + STAGE_ID + ".local.json")
            with mock.patch.object(fetcher, "PRIVATE", root), \
                 mock.patch.object(fetcher.lab2, "configure_lab2_transport"), \
                 mock.patch.object(fetcher.base, "collect_identity",
                                   return_value=("pinned",)), \
                 mock.patch.object(fetcher.base, "CachedVmwareGuest",
                                   return_value=fake), \
                 mock.patch.object(fetcher, "load_binding", return_value=binding()), \
                 mock.patch.object(fetcher, "verify_snapshot",
                                   side_effect=({"sequence": 10},
                                                fetcher.FetchBlocked("snapshot-stale-or-clock-skew"))):
                self.assertBlocked(
                    "snapshot-stale-or-clock-skew",
                    lambda: fetcher.fetch(3214, 639294897866000001, binding(), output))
            self.assertFalse(output.exists())

    def test_vm_identity_change_or_source_drift_never_publishes(self):
        for case in ("vm", "source"):
            with tempfile.TemporaryDirectory() as temp:
                root = Path(temp)
                output = root / ("lab2-guest-latest-" + STAGE_ID + ".local.json")
                data = snapshot(stamp=datetime.now(timezone.utc))
                fake = FakeGuest(data)
                identity = iter((("vm-before",), ("vm-after",))) if case == "vm" else None
                collect = (mock.Mock(side_effect=identity)
                           if identity is not None else mock.Mock(return_value=("vm",)))
                with mock.patch.object(fetcher, "PRIVATE", root), \
                     mock.patch.object(fetcher.lab2, "configure_lab2_transport"), \
                     mock.patch.object(fetcher.base, "collect_identity", collect), \
                     mock.patch.object(fetcher.base, "CachedVmwareGuest",
                                       return_value=fake), \
                     mock.patch.object(fetcher, "load_binding",
                                       return_value=binding(game_pin(machine="WRONG"))):
                    self.assertBlocked(
                        "lab2-vm-identity-changed"
                        if case == "vm" else "lab2-binding-changed-during-fetch",
                        lambda: fetcher.fetch(
                            3214, 639294897866000001, binding(), output))
                self.assertFalse(output.exists())

    def test_existing_output_and_invalid_location_preserved(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            target = root / ("lab2-guest-latest-" + STAGE_ID + ".local.json")
            target.write_text("preserve", encoding="utf-8")
            with mock.patch.object(fetcher, "PRIVATE", root):
                self.assertBlocked("lab2-output-not-absent-private",
                                   lambda: fetcher.check_private_output(target))
                self.assertBlocked("lab2-output-not-absent-private",
                                   lambda: fetcher.check_private_output(
                                       root / "lab1-guest-latest-fake.local.json"))
                self.assertEqual(target.read_text(), "preserve")

    def test_host_json_duplicate_keys_rejected(self):
        with tempfile.TemporaryDirectory() as temp:
            bad = Path(temp) / "snapshot.json"
            bad.write_text('{"schemaVersion":1,"schemaVersion":2}', encoding="utf-8")
            self.assertBlocked("json-invalid",
                               lambda: fetcher.host_json.read_json(
                                   bad, fetcher.MAX_SNAPSHOT_BYTES))

    def test_main_blocks_token_free_before_guest_if_wrong_stage(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            output = root / ("lab2-guest-latest-" + STAGE_ID + ".local.json")
            out = io.StringIO()
            with mock.patch.object(fetcher, "PRIVATE", root), redirect_stdout(out), \
                 mock.patch.object(fetcher.base, "collect_identity") as no_guest:
                rc = fetcher.main([
                    "--fetch", "--stage-id", "NOT-A-VALID-STAGE",
                    "--reader-pid", "1", "--reader-start-ticks", "2",
                    "--output", str(output)
                ])
            self.assertEqual(rc, 2)
            self.assertIn("lab2-stage-id-invalid", out.getvalue())
            self.assertNotIn("token", out.getvalue().casefold())
            no_guest.assert_not_called()


if __name__ == "__main__":
    unittest.main(verbosity=2)
