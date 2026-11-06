"""Offline Lab2 status-only VNC console tests; NEVER connect to a VM or send input."""
from __future__ import annotations

from contextlib import nullcontext
from datetime import timedelta
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


VM = (r"c:\rift modding\vms\rift-lab-2\rift-lab.vmx",
      (1, 2, 3, 4), "2026-11-06T08:00:00Z",
      17848, r"c:\vmware-vmx.exe", 123456, 5962)
PIN = {
    "schemaVersion": 1, "lab": "lab2", "machine": "RIFT-LAB-2",
    "gameProcessId": 9752, "gameProcessStartUtcTicks": 639294897826627677,
    "gameExecutablePath": r"C:\RIFT_TRANSFER\RIFT\rift_x64.exe",
    "gameExecutableSha256": "a" * 64,
}


class FakeClient:
    def __init__(self):
        self.keys = []
        self.ups = []
        self.mouse = []
        self.disconnected = False

    def keyPress(self, value):
        self.keys.append(value)

    def keyUp(self, value):
        self.ups.append(value)

    def mouseMove(self, x, y):
        self.mouse.append(("move", x, y))

    def mousePress(self, button):
        self.mouse.append(("press", button))

    def mouseUp(self, button):
        self.mouse.append(("up", button))

    def disconnect(self):
        self.disconnected = True


class Lab2StatusSpecs(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.artifacts = self.root / "artifacts" / "local"
        self.artifacts.mkdir(parents=True)
        self.pin = self.artifacts / "lab2-game-identity.local.json"
        self.pin.write_text(json.dumps(PIN), encoding="utf-8")
        self.output = self.artifacts / "case"
        self.client = FakeClient()
        patches = [
            mock.patch.object(lab, "ROOT", self.root),
            mock.patch.object(status, "LAB2_PIN", self.pin),
            mock.patch.object(base, "ARTIFACTS", self.artifacts),
            mock.patch.object(base, "control_mutex", lambda: nullcontext()),
            mock.patch.object(base, "connect_vnc", lambda: self.client),
            mock.patch.object(base, "screenshot", self.mock_screenshot),
            mock.patch.object(base, "require_game", lambda vm, pin: None),
            mock.patch.object(lab, "collect_identity", lambda: VM),
            mock.patch.object(lab, "direct_identity", lambda: VM),
        ]
        for patch in patches:
            patch.start()
            self.addCleanup(patch.stop)

    @staticmethod
    def mock_screenshot(client, path):
        path.write_bytes(b"mock-lab2-screen")
        return (1211, 757)

    def capture(self):
        with status.lab2_scope():
            return base.capture(self.output, dict(status.STATUS_OPERATION))

    def approve(self, plan):
        review_path = self.output / "review.json"
        review = json.loads(review_path.read_text())
        review.update({
            "reviewedAtUtc": plan["capturedAtUtc"],
            "decision": "approved", "gameForeground": True,
            "textEntryClosed": False, "chatEntryOpen": True,
            "chatEntryEmpty": True, "chatEntryFocused": True,
            "panelClosed": True, "otherControllerIdle": True,
            "queueIdle": True, "reviewer": "root-agent",
            "note": "current screenshot: Lab2, empty focused RIFT chat",
        })
        review_path.write_text(json.dumps(review))
        return review

    def test_allowlist_is_one_exact_status_operation(self):
        self.assertEqual(status.validate_status_only(dict(status.STATUS_OPERATION)),
                         status.STATUS_OPERATION)
        for candidate in (
            {"type": "command", "command": "/afguide status "},
            {"type": "command", "command": "/afguide bind lab2 1234"},
            {"type": "command", "command": "/reloadui"},
            {"type": "command", "command": "/afbind " + "a" * 32},
            {"type": "ui-step", "step": "open-chat"},
            {"type": "ui-step", "step": "focus-titlebar", "x": 120, "y": 8},
            {"type": "ui-step", "step": "reload-toolbar", "x": 1125, "y": 553},
            {"type": "command", "command": "/afguide status", "extra": True},
            ["command", "/afguide status"],
            None,
        ):
            with self.subTest(candidate=candidate), self.assertRaises(base.Blocked):
                status.validate_status_only(candidate)

    def test_key_mapping_uses_vnc_space_not_bare_space(self):
        self.assertIn("space", status.STATUS_KEYS)
        self.assertNotIn(" ", status.STATUS_KEYS)
        self.assertEqual(status.STATUS_KEYS[0], "/")
        self.assertEqual(status.STATUS_KEYS[-1], "s")

    def test_scope_is_lab2_only_and_reverts_after_exception(self):
        old = (lab.VMX, lab.VM_CONSOLE_PORT, lab.VM_PASSWORD_TARGET,
               lab.GUEST_PASSWORD_TARGET, lab.LEGACY_GUEST_TARGET,
               lab.DEFAULT_GAME_IDENTITY, lab.load_game_identity,
               base.validate_operation, base.send_input, base.release_keys)
        with self.assertRaisesRegex(RuntimeError, "test-stop"):
            with status.lab2_scope():
                self.assertEqual(lab.VMX, status.LAB2_VMX)
                self.assertEqual(lab.VM_CONSOLE_PORT, 5962)
                self.assertEqual(lab.DEFAULT_GAME_IDENTITY, self.pin)
                self.assertEqual(lab.GUEST_PASSWORD_TARGET,
                                 lab.LEGACY_GUEST_TARGET)
                self.assertIs(base.validate_operation, status.validate_status_only)
                raise RuntimeError("test-stop")
        self.assertEqual((lab.VMX, lab.VM_CONSOLE_PORT, lab.VM_PASSWORD_TARGET,
                          lab.GUEST_PASSWORD_TARGET, lab.LEGACY_GUEST_TARGET,
                          lab.DEFAULT_GAME_IDENTITY, lab.load_game_identity,
                          base.validate_operation, base.send_input, base.release_keys),
                         old)

    def test_pin_accepts_only_exact_lab2_and_private_location(self):
        self.assertEqual(status.load_lab2_pin(self.pin), PIN)
        with self.assertRaisesRegex(base.Blocked, "lab2-game-pin-invalid"):
            status.load_lab2_pin(self.root / "outside.json")
        for changed in (
            dict(PIN, lab="lab1"), dict(PIN, machine="RIFT-LAB-1"),
            dict(PIN, gameProcessId=-1), dict(PIN, gameExecutableSha256="Z" * 64),
            dict(PIN, gameExecutablePath=r"C:\Other.exe"),
            dict(PIN, gameProcessStartUtcTicks=0),
        ):
            with self.subTest(changed=changed):
                self.pin.write_text(json.dumps(changed), encoding="utf-8")
                with self.assertRaisesRegex(base.Blocked, "lab2-game-pin-invalid"):
                    status.load_lab2_pin(self.pin)
        self.pin.write_text(json.dumps(PIN), encoding="utf-8")

    def test_capture_never_types_or_clicks_and_starts_denied(self):
        plan = self.capture()
        self.assertEqual(plan["operation"], status.STATUS_OPERATION)
        self.assertEqual(plan["vmIdentity"][-1], 5962)
        self.assertEqual(plan["gameIdentity"]["lab"], "lab2")
        self.assertEqual(json.loads((self.output / "review.json").read_text())["decision"],
                         "denied")
        self.assertFalse(self.client.keys)
        self.assertFalse(self.client.mouse)
        self.assertTrue(self.client.disconnected)

    def test_denied_review_blocks_before_input(self):
        self.capture()
        with status.lab2_scope():
            with self.assertRaisesRegex(base.Blocked, "review-not-approved"):
                base.inspect(self.output)
            with self.assertRaisesRegex(base.Blocked, "review-not-approved"):
                base.execute(self.output)
        self.assertFalse(self.client.keys)
        self.assertFalse((self.output / "execution.json").exists())

    def test_empty_focused_chat_is_required(self):
        plan = self.capture()
        review = self.approve(plan)
        for flag in ("chatEntryOpen", "chatEntryEmpty", "chatEntryFocused"):
            changed = dict(review, **{flag: False})
            (self.output / "review.json").write_text(json.dumps(changed))
            with status.lab2_scope():
                with self.assertRaisesRegex(base.Blocked, "review-ui-mismatch"):
                    base.inspect(self.output)
            self.assertFalse(self.client.keys)

    def test_status_types_only_exact_command_then_releases_keys(self):
        plan = self.capture()
        self.approve(plan)
        with mock.patch.object(base.time, "sleep", return_value=None), status.lab2_scope():
            report = base.execute(self.output)
        self.assertEqual(report["status"], "completed")
        self.assertEqual(self.client.keys, list(status.STATUS_KEYS) + ["enter"])
        self.assertEqual(set(self.client.ups), set(status.RELEASE_KEYS))
        self.assertFalse(self.client.mouse)
        self.assertTrue((self.output / "after-screen.png").exists())
        self.assertTrue(self.client.disconnected)

    def test_single_attempt_receipt_prohibits_retry(self):
        plan = self.capture()
        self.approve(plan)
        with mock.patch.object(base.time, "sleep", return_value=None), status.lab2_scope():
            self.assertEqual(base.execute(self.output)["status"], "completed")
            with self.assertRaisesRegex(base.Blocked, "operation-already-attempted"):
                base.execute(self.output)
        self.assertEqual(self.client.keys.count("enter"), 1)

    def test_review_ttl_and_changed_screen_fail_closed(self):
        plan = self.capture()
        review = self.approve(plan)
        captured = base.parse_time(plan["capturedAtUtc"])
        with status.lab2_scope():
            with self.assertRaisesRegex(base.Blocked, "review-stale"):
                base.validate_review(plan, review, status.STATUS_OPERATION,
                                     now=captured + timedelta(seconds=61))
        (self.output / "review-screen.png").write_bytes(b"tampered")
        with status.lab2_scope():
            with self.assertRaisesRegex(base.Blocked, "review-screen-changed"):
                base.execute(self.output)
        self.assertFalse(self.client.keys)

    def test_wrong_vm_pin_blocks_before_input(self):
        plan = self.capture()
        self.approve(plan)
        with mock.patch.object(lab, "collect_identity", return_value=VM[:-1] + (5905,)):
            with status.lab2_scope():
                report = base.execute(self.output)
        self.assertEqual(report["status"], "blocked")
        self.assertEqual(report["reason"], "vm-identity-drift")
        self.assertFalse(self.client.keys)

    def test_release_failure_is_uncertain_and_never_retry(self):
        plan = self.capture()
        self.approve(plan)
        def bad_keyup(key):
            if key == "enter":
                raise RuntimeError("simulated failed release")
            self.client.ups.append(key)
        self.client.keyUp = bad_keyup
        with mock.patch.object(base.time, "sleep", return_value=None), status.lab2_scope():
            report = base.execute(self.output)
        self.assertEqual(report["status"], "blocked")
        self.assertEqual(report["reason"], "input-release-uncertain")
        self.assertTrue(report["inputMayHaveBeenSent"])
        self.assertFalse(report["releaseSucceeded"])
        self.assertFalse(self.client.mouse)


if __name__ == "__main__":
    unittest.main()
