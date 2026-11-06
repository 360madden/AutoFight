"""Host-only Lab2 reader-start contract tests; never contact VMware or launch RIFT."""
from __future__ import annotations

import contextlib
import io
import json
from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "scripts"))
import lab2_guest_reader_start as subject


STAGE = "a" * 32
RUN = "b" * 32


def start_receipt(state="reader-launched-not-bound"):
    return {
        "schemaVersion": 1, "kind": "autofight-lab2-reader-start",
        "stageId": STAGE, "startRunId": RUN, "machine": "RIFT-LAB-2",
        "state": state, "reason": "reader-process-started-no-input",
        "launchAttempted": True, "readerMayHaveStarted": True,
        "readerProcessId": 1234, "readerProcessStartUtcTicks": 639000000000000000,
        "noAddonWrite": True, "noGameInput": True,
        "noGameReload": True, "noVmLifecycleChange": True,
    }


def game_pin():
    return {
        "schemaVersion": 1, "lab": "lab2", "machine": "RIFT-LAB-2",
        "gameProcessId": 9752, "gameProcessStartUtcTicks": 639294897826627677,
        "gameExecutablePath": r"C:\Program Files (x86)\Glyph\Games\RIFT\Live\rift_x64.exe",
        "gameExecutableSha256": "e" * 64,
    }


class Lab2GuestReaderStartTests(unittest.TestCase):
    def test_receipt_accepts_exact_lab2_reader_started_not_bound(self):
        self.assertEqual(
            subject.validate_start_receipt(start_receipt(), STAGE, RUN)["readerProcessId"],
            1234)

    def test_receipt_rejects_extra_fields_worse_identity_flags_and_false_success(self):
        mutations = [
            ("kind", "autofight-lab1-reader-start"),
            ("machine", "RIFT-LAB-1"),
            ("startRunId", STAGE),
            ("noGameInput", False),
            ("noGameReload", False),
            ("noVmLifecycleChange", False),
            ("noAddonWrite", False),
            ("launchAttempted", False),
            ("readerMayHaveStarted", False),
            ("readerProcessId", 0),
            ("readerProcessStartUtcTicks", -1),
            ("reason", "reader-maybe-started"),
            ("state", "installed-not-running")
        ]
        for name, value in mutations:
            sample = start_receipt()
            sample[name] = value
            with self.subTest(name=name), self.assertRaises(subject.deploy.Blocked):
                subject.validate_start_receipt(sample, STAGE, RUN)
        extra = start_receipt()
        extra["token"] = "disallowed"
        with self.assertRaises(subject.deploy.Blocked):
            subject.validate_start_receipt(extra, STAGE, RUN)

    def test_blocked_and_uncertain_receipts_are_distinct(self):
        blocked = start_receipt("blocked-before-start")
        blocked.update(
            reason="preflight-failed", launchAttempted=False,
            readerMayHaveStarted=False, readerProcessId=0,
            readerProcessStartUtcTicks=0)
        subject.validate_start_receipt(blocked, STAGE, RUN)
        uncertain = start_receipt("uncertain-reader-start")
        uncertain.update(
            reason="start-or-postcheck-failed", launchAttempted=True,
            readerMayHaveStarted=True, readerProcessId=0,
            readerProcessStartUtcTicks=0)
        subject.validate_start_receipt(uncertain, STAGE, RUN)
        uncertain["readerMayHaveStarted"] = False
        with self.assertRaises(subject.deploy.Blocked):
            subject.validate_start_receipt(uncertain, STAGE, RUN)

    def test_stage_id_and_lab2_private_pin_path_fail_closed(self):
        with self.assertRaises(subject.deploy.Blocked):
            subject.validated_local_inputs("../lab1", Path("x"), Path("y"), Path("z"))
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            private = root / "artifacts" / "local"
            private.mkdir(parents=True)
            wrong = private / "lab1-game-identity.local.json"
            wrong.write_text(json.dumps(game_pin()), encoding="utf-8")
            with patch.object(subject, "PRIVATE", private), \
                    patch.object(subject.deploy, "PRIVATE_ROOT", private):
                with self.assertRaises(subject.deploy.Blocked):
                    subject.validated_local_inputs(STAGE, Path("x"), Path("y"), wrong)

    def test_host_preflight_matches_lab2_stage_receipt_hashed_source(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            private = root / "artifacts" / "local"
            src = root / "addon" / "AutoFight"
            package = private / "pkg"
            src.mkdir(parents=True)
            package.mkdir(parents=True)
            for name in subject.deploy.ADDON_NAMES:
                (src / name).write_bytes(name.encode())
            exe = package / subject.deploy.EXE
            exe.write_bytes(b"reader")
            config = private / "lab2-guest-reader.local.json"
            guest_config = {
                "schemaVersion": 1, "processName": "rift_x64",
                "durationSeconds": 900, "pollIntervalMilliseconds": 500,
                "maxScanBytes": 4294967296, "maxScanSeconds": 10,
                "outputPath": None, "freshnessTimeoutSeconds": 5,
                "delivery": {"endpoint": "http://192.168.56.1:48761/observation",
                             "token": "c" * 64}
            }
            config.write_text(json.dumps(guest_config), encoding="utf-8")
            pin_path = private / "lab2-game-identity.local.json"
            pin_path.write_text(json.dumps(game_pin()), encoding="utf-8")
            with patch.object(subject, "PRIVATE", private), \
                    patch.object(subject, "DEFAULT_PIN", pin_path), \
                    patch.object(subject.deploy, "ROOT", root), \
                    patch.object(subject.deploy, "PRIVATE_ROOT", private):
                _, files, addons = subject.deploy.validate_host_inputs(config, package)
                record = {
                    "schemaVersion": 1, "kind": "autofight-lab2-reader-stage-receipt",
                    "stageId": STAGE, "manifestSha256": "f" * 64,
                    "gamePinSha256": subject.deploy.base.digest_file(pin_path),
                    "guestConfigSha256": files[1]["sha256"],
                    "readerExeSha256": files[0]["sha256"],
                    "addonFiles": addons,
                }
                stage_path = private / ("lab2-stage-" + STAGE + ".local.json")
                stage_path.write_text(json.dumps(record), encoding="utf-8")
                _, returned, _, _ = subject.validated_local_inputs(
                    STAGE, config, package, pin_path)
                self.assertEqual(returned, record)
                stage_path.write_text(json.dumps({**record, "gamePinSha256": "0" * 64}),
                                      encoding="utf-8")
                with self.assertRaises(subject.deploy.Blocked):
                    subject.validated_local_inputs(STAGE, config, package, pin_path)

    def test_cli_start_requires_confirmation_before_guest_communication(self):
        with patch.object(subject, "validated_local_inputs", return_value=(
                game_pin(), {}, [], [])), \
                patch.object(subject, "require_start_script", return_value="a" * 64), \
                patch.object(subject, "_start", side_effect=AssertionError("guest called")):
            capture = io.StringIO()
            with contextlib.redirect_stdout(capture):
                code = subject.main([
                    "--start", "--stage-id", STAGE, "--game-pin",
                    str(subject.DEFAULT_PIN)])
            self.assertEqual(code, 2)
            self.assertIn("explicit-start-lab2-confirmation-required", capture.getvalue())

    def test_cli_preflight_never_creates_guest_session(self):
        files = [{"sha256": "a" * 64}, {"sha256": "b" * 64}]
        with patch.object(subject, "validated_local_inputs", return_value=(
                game_pin(), {}, files, [{}] * 6)), \
                patch.object(subject, "require_start_script", return_value="a" * 64), \
                patch.object(subject.deploy, "configure_lab2_transport",
                             side_effect=AssertionError("vm touched")):
            output = io.StringIO()
            with contextlib.redirect_stdout(output):
                code = subject.main([
                    "--preflight", "--stage-id", STAGE, "--game-pin",
                    str(subject.DEFAULT_PIN)])
            self.assertEqual(code, 0)
            self.assertIn("lab2-reader-start-host-ready", output.getvalue())

    def test_verify_installed_reader_checks_exact_two_names_before_copy(self):
        class NoCopy:
            def run(self, *_a, **_kw):
                raise AssertionError("unexpected guest copy")
        with patch.object(subject.deploy, "_extra", return_value=
                          "Directory list: 3\nRift.Automation.GuestReader.exe\n"
                          "autofight-guest-reader.json\nsurprise.txt"):
            with self.assertRaises(subject.deploy.Blocked):
                subject.verify_installed_reader(
                    NoCopy(), [], Path("unused"))

    def test_start_blocked_redacted_without_credentials(self):
        error = subject.StartBlocked(
            "reader-start-outcome-unavailable", STAGE, may_have_started=None)
        self.assertFalse(error.may_have_started is True)
        self.assertNotIn("token", str(error).lower())


if __name__ == "__main__":
    unittest.main()
