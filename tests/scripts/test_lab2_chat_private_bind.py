"""Offline tests for Lab2 private Reader nonce binding. Never contact a VM."""
from __future__ import annotations

from contextlib import nullcontext
from datetime import timedelta
import hashlib
import json
from pathlib import Path
import sys
import tempfile
import unittest
from unittest import mock

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "scripts"))
import lab1_chat_console as base
import lab1_guest_deploy as lab
import lab2_chat_status as status
import lab2_chat_private_bind as binder


STAGE = "3fe0dafb233f4763901c18bc951f2501"
INSTANCE = "4fe553ed-fe5f-4411-984f-7b9c8a47cdab"
NONCE = "abcde01234567890abcde01234567890"
VM = (r"c:\rift modding\vms\rift-lab-2\rift-lab.vmx",
      (1, 2, 3, 4), "2026-11-06T08:00:00Z",
      17848, r"c:\vmware-vmx.exe", 123456, 5962)
PIN = {
    "schemaVersion": 1, "lab": "lab2", "machine": "RIFT-LAB-2",
    "gameProcessId": 9752, "gameProcessStartUtcTicks": 639294897826627677,
    "gameExecutablePath": r"C:\RIFT_TRANSFER\RIFT\rift_x64.exe",
    "gameExecutableSha256": "a" * 64,
}


class Client:
    def __init__(self):
        self.keys, self.ups = [], []
        self.mouse = []
        self.disconnected = False

    def keyPress(self, x):
        self.keys.append(x)

    def keyUp(self, x):
        self.ups.append(x)

    def disconnect(self):
        self.disconnected = True


class Lab2PrivateBindTests(unittest.TestCase):
    def setUp(self):
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        self.root = Path(temporary.name)
        self.private = self.root / "artifacts" / "local"
        self.private.mkdir(parents=True)
        self.pin = self.private / "lab2-game-identity.local.json"
        self.pin.write_text(json.dumps(PIN))
        self.output = self.private / "private-bind"
        self.client = Client()
        patches = [
            mock.patch.object(lab, "ROOT", self.root),
            mock.patch.object(status, "LAB2_PIN", self.pin),
            mock.patch.object(base, "ARTIFACTS", self.private),
            mock.patch.object(base, "control_mutex", lambda: nullcontext()),
            mock.patch.object(base, "connect_vnc", lambda: self.client),
            mock.patch.object(base, "screenshot", self.mock_screenshot),
            mock.patch.object(base, "require_game", lambda vm, pin: None),
            mock.patch.object(lab, "collect_identity", lambda: VM),
            mock.patch.object(lab, "direct_identity", lambda: VM),
            mock.patch.object(binder, "fresh_private_binding",
                              return_value=(INSTANCE, NONCE)),
        ]
        self.fresh_patch = patches[-1]
        for p in patches:
            p.start()
            self.addCleanup(p.stop)

    @staticmethod
    def mock_screenshot(_client, path):
        path.write_bytes(b"review-screen-with-no-chat-text")
        return 1211, 757

    def capture(self):
        return binder.capture(self.output, STAGE, 6252, 639295544587249998)

    def approve(self, plan):
        receipt = json.loads((self.output / "review.json").read_text())
        receipt.update({
            "reviewedAtUtc": plan["capturedAtUtc"],
            "decision": "approved", "gameForeground": True,
            "textEntryClosed": False, "chatEntryOpen": True,
            "chatEntryEmpty": True, "chatEntryFocused": True,
            "panelClosed": True, "otherControllerIdle": True,
            "queueIdle": True, "reviewer": "root-agent",
            "note": "RIFT foreground, empty native chat focused",
        })
        (self.output / "review.json").write_text(json.dumps(receipt))
        return receipt

    def test_strict_nonsecret_operation_schema(self):
        plan = self.capture()
        op = plan["operation"]
        self.assertEqual(set(op), binder.OP_KEYS)
        self.assertNotIn(NONCE, json.dumps(op))
        self.assertEqual(op["nonceSha256"],
                         hashlib.sha256(NONCE.encode()).hexdigest())
        for invalid in (
            dict(op, step="reload-toolbar"),
            dict(op, nonce=NONCE),
            dict(op, nonceSha256=NONCE),
            dict(op, readerPid=-5),
            dict(op, readerStartTicks="1"),
            dict(op, readerInstanceId="wrong"),
            {"type": "command", "command": "/afguide bind lab2 " + NONCE},
        ):
            with self.subTest(invalid=invalid), self.assertRaises(base.Blocked):
                binder.validate_operation(invalid)

    def test_capture_is_dry_and_review_denied(self):
        plan = self.capture()
        self.assertEqual(plan["gameIdentity"]["lab"], "lab2")
        self.assertEqual(plan["vmIdentity"][-1], 5962)
        self.assertEqual(json.loads((self.output / "review.json").read_text())["decision"],
                         "denied")
        self.assertFalse(self.client.keys)
        self.assertFalse(self.client.mouse)
        self.assertTrue(self.client.disconnected)
        self.assertNotIn(NONCE, (self.output / "plan.json").read_text())
        self.assertNotIn(NONCE, (self.output / "review.json").read_text())

    def test_denied_or_unfocused_or_open_panel_blocks_before_input(self):
        plan = self.capture()
        with self.assertRaisesRegex(base.Blocked, "review-not-approved"):
            binder.execute(self.output)
        self.assertFalse((self.output / "execution.json").exists())
        approved = self.approve(plan)
        for key in ("gameForeground", "chatEntryOpen", "chatEntryEmpty",
                    "chatEntryFocused", "panelClosed", "otherControllerIdle",
                    "queueIdle"):
            changed = dict(approved, **{key: False})
            (self.output / "review.json").write_text(json.dumps(changed))
            with self.assertRaises(base.Blocked):
                binder.execute(self.output)
            self.assertFalse((self.output / "execution.json").exists())
        self.assertFalse(self.client.keys)

    def test_stale_or_tampered_capture_cannot_send(self):
        plan = self.capture()
        self.approve(plan)
        captured = base.parse_time(plan["capturedAtUtc"])
        review = json.loads((self.output / "review.json").read_text())
        with binder.lab2_bind_scope(), self.assertRaisesRegex(base.Blocked, "review-stale"):
            base.validate_review(plan, review, plan["operation"],
                                 now=captured + timedelta(seconds=61))
        (self.output / "review-screen.png").write_bytes(b"replaced")
        with self.assertRaisesRegex(base.Blocked, "review-screen-changed"):
            binder.execute(self.output)
        self.assertFalse(self.client.keys)

    def test_one_exact_bind_only_no_after_screen_no_nonce_in_receipts(self):
        plan = self.capture()
        self.approve(plan)
        with mock.patch.object(binder.time, "sleep"):
            receipt = binder.execute(self.output)
        self.assertEqual(receipt["status"], "completed")
        expected = ["space" if c == " " else c for c in binder.PREFIX + NONCE]
        self.assertEqual(self.client.keys, expected + ["enter"])
        self.assertEqual(set(self.client.ups), set(binder.RELEASE))
        self.assertIn("space", binder.RELEASE)
        self.assertNotIn(" ", binder.RELEASE)
        self.assertTrue(self.client.disconnected)
        self.assertFalse((self.output / "after-screen.png").exists())
        self.assertFalse(self.client.mouse)
        self.assertNotIn(NONCE, (self.output / "plan.json").read_text())
        self.assertNotIn(NONCE, (self.output / "review.json").read_text())
        self.assertNotIn(NONCE, (self.output / "execution.json").read_text())
        with self.assertRaisesRegex(base.Blocked, "operation-already-attempted"):
            binder.execute(self.output)
        self.assertEqual(self.client.keys.count("enter"), 1)

    def test_changed_reader_instance_or_nonce_blocks_with_durable_receipt(self):
        for replacement in ((INSTANCE, "f" * 32),
                            ("aabbccdd-eeee-4e44-aaaa-123456789abc", NONCE)):
            with self.subTest(replacement=replacement):
                self.output = self.private / ("case-" + replacement[1][:5])
                plan = self.capture()
                self.approve(plan)
                with mock.patch.object(binder, "fresh_private_binding",
                                       return_value=replacement):
                    receipt = binder.execute(self.output)
                self.assertEqual(receipt["status"], "blocked")
                self.assertEqual(receipt["reason"], "lab2-bind-reader-drift")
                self.assertFalse(receipt["inputMayHaveBeenSent"])
                self.assertFalse(self.client.keys)

    def test_wrong_vm_pin_never_sends(self):
        plan = self.capture()
        self.approve(plan)
        with mock.patch.object(lab, "collect_identity",
                               return_value=VM[:-1] + (5905,)):
            receipt = binder.execute(self.output)
        self.assertEqual(receipt["reason"], "vm-identity-drift")
        self.assertFalse(self.client.keys)

    def test_key_release_uncertainty_and_partial_send_no_retry(self):
        plan = self.capture()
        self.approve(plan)
        self.client.keyUp = lambda key: (_ for _ in ()).throw(RuntimeError("key up"))
        with mock.patch.object(binder.time, "sleep"):
            receipt = binder.execute(self.output)
        self.assertEqual(receipt["reason"], "input-release-uncertain")
        self.assertTrue(receipt["inputMayHaveBeenSent"])
        self.assertFalse(receipt["releaseSucceeded"])
        self.assertNotIn(NONCE, (self.output / "execution.json").read_text())

    def test_partial_send_is_uncertain_and_never_retried(self):
        plan = self.capture()
        self.approve(plan)
        original = self.client.keyPress
        def interrupted(key):
            if len(self.client.keys) == 4:
                raise RuntimeError("secret-bearing failure " + NONCE)
            original(key)
        self.client.keyPress = interrupted
        receipt = binder.execute(self.output)
        self.assertEqual(receipt["status"], "blocked")
        self.assertEqual(receipt["reason"], "console-operation-failed")
        self.assertTrue(receipt["inputMayHaveBeenSent"])
        self.assertFalse((self.output / "after-screen.png").exists())
        self.assertNotIn(NONCE, (self.output / "execution.json").read_text())
        with self.assertRaisesRegex(base.Blocked, "operation-already-attempted"):
            binder.execute(self.output)

    def test_generic_executor_cannot_send_bind_or_make_after_screen(self):
        plan = self.capture()
        self.approve(plan)
        with binder.lab2_bind_scope():
            receipt = base.execute(self.output)
        self.assertEqual(receipt["status"], "blocked")
        self.assertFalse(self.client.keys)
        self.assertFalse((self.output / "after-screen.png").exists())

    def test_main_never_prints_nonce(self):
        with mock.patch("builtins.print") as printed:
            self.assertEqual(binder.main(["--capture", "--output", str(self.output),
                                          "--stage-id", STAGE,
                                          "--reader-pid", "6252",
                                          "--reader-start-ticks", "639295544587249998"]), 0)
        self.assertNotIn(NONCE, printed.call_args.args[0])
        plan = json.loads((self.output / "plan.json").read_text())
        self.approve(plan)
        with mock.patch("builtins.print") as printed:
            self.assertEqual(binder.main(["--review", "--output", str(self.output)]), 0)
        self.assertNotIn(NONCE, printed.call_args.args[0])
        with mock.patch("builtins.print") as printed, mock.patch.object(binder.time, "sleep"):
            self.assertEqual(binder.main(["--execute", "--output", str(self.output)]), 0)
        self.assertNotIn(NONCE, printed.call_args.args[0])

    def test_main_rejects_arguments_in_review_and_execute(self):
        with mock.patch("builtins.print") as printed:
            self.assertEqual(binder.main(["--execute", "--output", str(self.output),
                                          "--reader-pid", "6252"]), 2)
        self.assertIn("lab2-bind-arguments-invalid", printed.call_args.args[0])
        self.assertFalse(self.client.keys)

    def test_never_stringify_unknown_secret_exception(self):
        self.assertEqual(binder.safe_reason(base.Blocked(NONCE)),
                         "lab2-bind-preflight-blocked")

    def test_private_source_validation_and_cleanup_without_vm(self):
        self.fresh_patch.stop()
        config_path = self.private / "host-lab2.local.json"
        config_path.write_text(json.dumps({"binding": {
            "lab": "lab2", "readerInstanceId": INSTANCE, "readerNonce": NONCE,
            "gameProcessId": PIN["gameProcessId"],
            "gameProcessStartUtcTicks": PIN["gameProcessStartUtcTicks"],
            "gameExecutablePath": PIN["gameExecutablePath"],
            "gameExecutableSha256": PIN["gameExecutableSha256"]}}))
        snapshot = {"nonce": NONCE, "target": {"readerSessionNonce": NONCE}}
        binding = type("Binding", (), {"game_pin": PIN})()

        def fake_fetch(_pid, _start, _binding, output):
            output.write_text(json.dumps(snapshot))
            return {"readerInstanceId": INSTANCE}

        with (mock.patch.object(binder, "PRIVATE", self.private),
              mock.patch.object(binder, "HOST_CONFIG", config_path),
              mock.patch.object(binder.reader, "load_binding", return_value=binding),
              mock.patch.object(binder.reader, "fetch", side_effect=fake_fetch),
              mock.patch.object(binder.reader, "verify_snapshot",
                                return_value={"readerInstanceId": INSTANCE})):
            self.assertEqual(binder.fresh_private_binding(
                STAGE, 6252, 639295544587249998), (INSTANCE, NONCE))
            self.assertEqual(list(self.private.glob("lab2-guest-latest-*.local.json")), [])
            config = json.loads(config_path.read_text())
            config["binding"]["readerNonce"] = "f" * 32
            config_path.write_text(json.dumps(config))
            with self.assertRaisesRegex(base.Blocked, "lab2-bind-host-config-mismatch"):
                binder.fresh_private_binding(STAGE, 6252, 639295544587249998)
            self.assertEqual(list(self.private.glob("lab2-guest-latest-*.local.json")), [])
            snapshot["nonce"] = "f" * 32
            with self.assertRaisesRegex(base.Blocked, "lab2-bind-reader-drift"):
                binder.fresh_private_binding(STAGE, 6252, 639295544587249998)
            self.assertEqual(list(self.private.glob("lab2-guest-latest-*.local.json")), [])


if __name__ == "__main__":
    unittest.main()
