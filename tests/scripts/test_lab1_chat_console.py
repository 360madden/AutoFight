"""Offline, deterministic specs for AutoFight's reviewed Lab1 VNC helper.

No VMware, guest, game, credential, VNC socket, or actual key input is used.
"""
from __future__ import annotations

from contextlib import nullcontext
from datetime import timedelta
from pathlib import Path
import json
import sys
import tempfile
import unittest
from unittest import mock

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "scripts"))
import lab1_chat_console as chat


PIN = {
    "schemaVersion": 1, "lab": "lab1", "machine": "RIFT-LAB-1",
    "gameProcessId": 7164, "gameProcessStartUtcTicks": 123456789,
    "gameExecutablePath": r"C:\Games\RIFT\rift_x64.exe",
    "gameExecutableSha256": "a" * 64
}
VM = ("c:\\vm\\lab1.vmx", (1, 2, 3, 4), "2026-11-05T20:00:00Z",
      502, "c:\\vmware-vmx.exe", 123456, 5905)


class FakeClient:
    def __init__(self):
        self.keys = []
        self.ups = []
        self.disconnected = False
        self.mouse = []

    def mouseMove(self, x, y):
        self.mouse.append(("move", x, y))

    def mousePress(self, button):
        self.mouse.append(("press", button))

    def mouseUp(self, button):
        self.mouse.append(("up", button))

    def keyPress(self, value):
        self.keys.append(value)

    def keyUp(self, value):
        self.ups.append(value)

    def disconnect(self):
        self.disconnected = True


class ChatSpecs(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.artifacts = Path(self.temporary.name) / "artifacts" / "local"
        self.artifacts.mkdir(parents=True)
        self.output = self.artifacts / "case"
        self.client = FakeClient()
        self.stack = mock.patch.multiple(
            chat, ARTIFACTS=self.artifacts,
            control_mutex=lambda: nullcontext(),
            connect_vnc=lambda: self.client,
            screenshot=lambda client, path: (path.write_bytes(b"fake-screen"), (1211, 757))[1],
            require_game=lambda vm, game: None,
        )
        self.stack.start()
        self.addCleanup(self.stack.stop)
        self.pin_patch = mock.patch.object(chat.lab, "load_game_identity", return_value=PIN)
        self.pin_patch.start()
        self.addCleanup(self.pin_patch.stop)
        self.vm_patch = mock.patch.object(chat.lab, "collect_identity", return_value=VM)
        self.vm_patch.start()
        self.addCleanup(self.vm_patch.stop)
        self.direct_patch = mock.patch.object(chat.lab, "direct_identity", return_value=VM)
        self.direct_patch.start()
        self.addCleanup(self.direct_patch.stop)

    def plan(self, operation):
        return chat.capture(self.output, operation)

    def approve(self, plan, *, open_chat):
        review_path = self.output / "review.json"
        review = json.loads(review_path.read_text())
        review.update({
            "reviewedAtUtc": plan["capturedAtUtc"],
            "decision": "approved",
            "gameForeground": True, "panelClosed": True,
            "otherControllerIdle": True, "queueIdle": True,
            "reviewer": "root-agent", "note": "independent screenshot reviewed",
            "textEntryClosed": open_chat,
            "chatEntryOpen": not open_chat,
            "chatEntryEmpty": not open_chat,
            "chatEntryFocused": not open_chat,
        })
        review_path.write_text(json.dumps(review))
        return review

    def test_allowlist_accepts_only_two_command_shapes(self):
        self.assertEqual(chat.operation_for_command("/reloadui")["command"], "/reloadui")
        self.assertEqual(chat.operation_for_command("/afbind " + "a" * 32)["type"], "command")
        for value in ("/afbind", "/afbind " + "a" * 31, "/afbind " + "A" * 32,
                      "/afbind " + "a" * 32 + "\n", "/reloadui ",
                      "/ReloadUI", "/afbind " + "a" * 32 + " /reloadui",
                      "/rr2guide status", "/afbind " + "g" * 32, "open-chat"):
            with self.subTest(value=value), self.assertRaises(chat.Blocked):
                chat.operation_for_command(value)

    def test_reload_toolbar_only_fixed_reviewed_button(self):
        expected = {"type": "ui-step", "step": "reload-toolbar", "x": 1125,
                    "y": 553, "screenWidth": 1211, "screenHeight": 757,
                    "visibleAddonVersion": "0.4.10"}
        self.assertEqual(chat.reload_toolbar_operation(), expected)
        self.assertEqual(chat.validate_operation(expected), expected)
        for changed in (dict(expected, x=1126), dict(expected, y=552),
                        dict(expected, screenWidth=1920),
                        dict(expected, visibleAddonVersion="0.4.11"),
                        {key: value for key, value in expected.items() if key != "x"}):
            with self.subTest(changed=changed), self.assertRaises(chat.Blocked):
                chat.validate_operation(changed)

    def test_reload_toolbar_requires_all_visual_assertions(self):
        plan = self.plan(chat.reload_toolbar_operation())
        receipt = self.approve(plan, open_chat=True)
        with self.assertRaisesRegex(chat.Blocked, "review-not-approved"):
            chat.inspect(self.output)
        self.assertFalse(self.client.mouse)
        receipt.update({"riftWindowVisible": True, "reloadButtonVisible": True,
                        "reloadButtonEnabled": True,
                        "reloadButtonCenterReviewed": True,
                        "visibleAddonVersionReviewed": "0.4.10"})
        (self.output / "review.json").write_text(json.dumps(receipt))
        with mock.patch.object(chat.time, "sleep", return_value=None):
            report = chat.execute(self.output)
        self.assertEqual(report["status"], "completed")
        self.assertEqual(self.client.keys, [])
        self.assertEqual(self.client.mouse,
                         [("move", 1125, 553), ("press", 1), ("up", 1), ("up", 1)])
        self.assertTrue(report["releaseSucceeded"])
        self.assertTrue((self.output / "after-screen.png").exists())

    def test_reload_toolbar_screen_geometry_is_exact(self):
        def changed_geometry(client, path):
            path.write_bytes(b"screen")
            return (1212, 757)
        with mock.patch.object(chat, "screenshot", side_effect=changed_geometry):
            with self.assertRaisesRegex(chat.Blocked, "reload-toolbar-screen-size-changed"):
                self.plan(chat.reload_toolbar_operation())
        self.assertFalse((self.output / "plan.json").exists())
        self.assertFalse(self.client.keys)
        self.assertFalse(self.client.mouse)

    def test_reload_toolbar_wrong_label_version_blocks_before_input(self):
        plan = self.plan(chat.reload_toolbar_operation())
        receipt = self.approve(plan, open_chat=True)
        receipt.update({"riftWindowVisible": True, "reloadButtonVisible": True,
                        "reloadButtonEnabled": True,
                        "reloadButtonCenterReviewed": True,
                        "visibleAddonVersionReviewed": "0.4.11"})
        (self.output / "review.json").write_text(json.dumps(receipt))
        with self.assertRaisesRegex(chat.Blocked, "review-not-approved"):
            chat.execute(self.output)
        self.assertFalse(self.client.keys)
        self.assertFalse(self.client.mouse)
        self.assertFalse((self.output / "execution.json").exists())

    def test_focus_titlebar_only_fixed_coordinate(self):
        self.assertEqual(chat.focus_titlebar_operation(),
                         {"type": "ui-step", "step": "focus-titlebar", "x": 120, "y": 8})
        self.assertEqual(chat.validate_operation(chat.focus_titlebar_operation()),
                         chat.focus_titlebar_operation())
        for candidate in ({"type": "ui-step", "step": "focus-titlebar", "x": 121, "y": 8},
                          {"type": "ui-step", "step": "focus-titlebar", "x": 120, "y": 7},
                          {"type": "ui-step", "step": "focus-titlebar"},
                          {"type": "ui-step", "step": "focus-titlebar", "x": 120, "y": 8, "button": 2}):
            with self.subTest(candidate=candidate), self.assertRaises(chat.Blocked):
                chat.validate_operation(candidate)

    def test_focus_requires_visible_titlebar_target_review(self):
        plan = self.plan(chat.focus_titlebar_operation())
        receipt = self.approve(plan, open_chat=True)
        with self.assertRaisesRegex(chat.Blocked, "review-not-approved"):
            chat.inspect(self.output)
        self.assertFalse(self.client.mouse)
        receipt.update({"riftWindowVisible": True, "titlebarTargetReviewed": True})
        (self.output / "review.json").write_text(json.dumps(receipt))
        with mock.patch.object(chat.time, "sleep", return_value=None):
            report = chat.execute(self.output)
        self.assertEqual(report["status"], "completed")
        self.assertEqual(self.client.keys, [])
        self.assertEqual(self.client.mouse, [
            ("move", 120, 8), ("press", 1), ("up", 1), ("up", 1)
        ])
        self.assertTrue((self.output / "after-screen.png").exists())

    def test_focus_target_outside_screenshot_blocks_capture(self):
        def too_small(client, path):
            path.write_bytes(b"small")
            return (120, 8)
        with mock.patch.object(chat, "screenshot", side_effect=too_small):
            with self.assertRaisesRegex(chat.Blocked, "titlebar-target-outside-screen"):
                self.plan(chat.focus_titlebar_operation())
        self.assertFalse((self.output / "plan.json").exists())
        self.assertEqual(self.client.keys, [])
        self.assertFalse(self.client.mouse)

    def test_focus_mouse_failure_releases_button_and_no_retry(self):
        plan = self.plan(chat.focus_titlebar_operation())
        receipt = self.approve(plan, open_chat=True)
        receipt.update({"riftWindowVisible": True, "titlebarTargetReviewed": True})
        (self.output / "review.json").write_text(json.dumps(receipt))
        def fail_press(_button):
            raise RuntimeError("simulated-mouse-failure")
        self.client.mousePress = fail_press
        report = chat.execute(self.output)
        self.assertEqual(report["status"], "blocked")
        self.assertTrue(report["inputMayHaveBeenSent"])
        self.assertIn(("up", 1), self.client.mouse)
        self.assertTrue(self.client.disconnected)

    def test_only_explicit_open_chat_ui_operation(self):
        self.assertEqual(chat.validate_operation(chat.open_chat_operation()),
                         chat.open_chat_operation())
        for value in ({"type": "ui-step", "step": "close-ui"},
                      {"type": "ui-step", "step": "open-chat", "extra": 1},
                      {"type": "command", "command": "/reloadui", "extra": 1}):
            with self.subTest(value=value), self.assertRaises(chat.Blocked):
                chat.validate_operation(value)

    def test_capture_never_sends_keys_and_review_starts_denied(self):
        plan = self.plan(chat.open_chat_operation())
        self.assertEqual(self.client.keys, [])
        self.assertTrue(self.client.disconnected)
        self.assertEqual(len(plan["planSha256"]), 64)
        self.assertEqual(json.loads((self.output / "review.json").read_text())["decision"],
                         "denied")

    def test_open_chat_requires_closed_entry_then_sends_one_enter(self):
        plan = self.plan(chat.open_chat_operation())
        self.approve(plan, open_chat=True)
        with mock.patch.object(chat.time, "sleep", return_value=None):
            report = chat.execute(self.output)
        self.assertEqual(report["status"], "completed")
        self.assertEqual(self.client.keys, ["enter"])
        self.assertTrue(self.client.disconnected)
        self.assertTrue(report["releaseSucceeded"])

    def test_command_requires_separate_open_focused_empty_review(self):
        plan = self.plan(chat.operation_for_command("/reloadui"))
        self.approve(plan, open_chat=True)
        with self.assertRaisesRegex(chat.Blocked, "review-ui-mismatch"):
            chat.inspect(self.output)
        self.assertFalse((self.output / "execution.json").exists())
        self.assertEqual(self.client.keys, [])

    def test_command_types_only_reviewed_text_and_enter(self):
        plan = self.plan(chat.operation_for_command("/reloadui"))
        self.approve(plan, open_chat=False)
        with mock.patch.object(chat.time, "sleep", return_value=None):
            report = chat.execute(self.output)
        self.assertEqual(report["status"], "completed")
        self.assertEqual(self.client.keys, list("/reloadui") + ["enter"])
        self.assertIn("enter", self.client.ups)
        self.assertTrue(self.client.disconnected)

    def test_unapproved_review_stops_before_input_and_marker(self):
        self.plan(chat.open_chat_operation())
        with self.assertRaisesRegex(chat.Blocked, "review-not-approved"):
            chat.execute(self.output)
        self.assertFalse((self.output / "execution.json").exists())
        self.assertFalse(self.client.keys)

    def test_captured_image_tampering_blocks_input(self):
        plan = self.plan(chat.open_chat_operation())
        self.approve(plan, open_chat=True)
        (self.output / "review-screen.png").write_bytes(b"modified")
        with self.assertRaisesRegex(chat.Blocked, "review-screen-changed"):
            chat.execute(self.output)
        self.assertEqual(self.client.keys, [])

    def test_capture_and_review_ttl(self):
        plan = self.plan(chat.open_chat_operation())
        receipt = self.approve(plan, open_chat=True)
        captured = chat.parse_time(plan["capturedAtUtc"])
        with self.assertRaisesRegex(chat.Blocked, "review-stale"):
            chat.validate_review(plan, receipt, chat.open_chat_operation(),
                                 now=captured + timedelta(seconds=61))
        with self.assertRaisesRegex(chat.Blocked, "review-capture-mismatch"):
            altered = dict(receipt, captureId="b" * 32)
            chat.validate_review(plan, altered, chat.open_chat_operation(),
                                 now=captured + timedelta(seconds=2))

    def test_vm_pin_drift_stops_before_key_and_reports_failed_attempt(self):
        plan = self.plan(chat.open_chat_operation())
        self.approve(plan, open_chat=True)
        with mock.patch.object(chat.lab, "collect_identity", return_value=VM[:-1] + (5906,)):
            report = chat.execute(self.output)
        self.assertEqual(report["status"], "blocked")
        self.assertEqual(report["reason"], "vm-identity-drift")
        self.assertEqual(self.client.keys, [])
        with self.assertRaisesRegex(chat.Blocked, "operation-already-attempted"):
            chat.execute(self.output)

    def test_game_pin_drift_stops_before_input(self):
        plan = self.plan(chat.open_chat_operation())
        self.approve(plan, open_chat=True)
        with mock.patch.object(chat, "require_game", side_effect=chat.Blocked("game-identity-drift")):
            report = chat.execute(self.output)
        self.assertEqual(report["status"], "blocked")
        self.assertEqual(self.client.keys, [])

    def test_abrupt_report_replacement_failure_leaves_uncertain_marker(self):
        plan = self.plan(chat.open_chat_operation())
        self.approve(plan, open_chat=True)
        with mock.patch.object(chat.os, "replace", side_effect=RuntimeError("simulated-abrupt-failure")), \
             mock.patch.object(chat.time, "sleep", return_value=None):
            with self.assertRaisesRegex(RuntimeError, "simulated-abrupt-failure"):
                chat.execute(self.output)
        marker = json.loads((self.output / "execution.json").read_text())
        self.assertTrue(marker["inputMayHaveBeenSent"])
        self.assertFalse(marker["inputSent"])
        with self.assertRaisesRegex(chat.Blocked, "operation-already-attempted"):
            chat.execute(self.output)

    def test_input_failure_records_uncertain_and_releases_keys(self):
        plan = self.plan(chat.open_chat_operation())
        self.approve(plan, open_chat=True)
        def fail(_key):
            raise RuntimeError("fake VNC failure")
        self.client.keyPress = fail
        report = chat.execute(self.output)
        self.assertEqual(report["status"], "blocked")
        self.assertTrue(report["inputMayHaveBeenSent"])
        self.assertFalse(report["inputSent"])
        self.assertIn("enter", self.client.ups)
        self.assertTrue(self.client.disconnected)


if __name__ == "__main__":
    unittest.main()
