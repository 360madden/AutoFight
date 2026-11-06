r"""Offline deterministic tests for the Lab1 read-only GuestReader launch helper.

No real VM, Windows credentials, guest files, game process, or reader launch.
Windows: C:\Python314\python.exe -m unittest discover -s tests\scripts -p test_lab1_guest_reader_start.py -v
"""
from __future__ import annotations

from contextlib import redirect_stdout
from io import StringIO
import json
from pathlib import Path
import sys
import tempfile
import unittest
from unittest import mock

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "scripts"))
import lab1_guest_deploy as deploy
import lab1_guest_reader_start as start


class Lab1ReaderStartSpecs(unittest.TestCase):
    def setUp(self) -> None:
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.artifacts = self.root / "artifacts" / "local"
        self.artifacts.mkdir(parents=True)
        self.package = self.artifacts / "build"
        self.package.mkdir()
        self.addon = self.root / "addon" / "AutoFight"
        self.addon.mkdir(parents=True)
        pub = self.package / "addon" / "AutoFight"
        pub.mkdir(parents=True)
        toc = ('Identifier = "AutoFight"\nRunOnStartup = { "wire.lua", '
               '"producer.lua", "live.lua", "guidance.lua", "main.lua" }\n')
        for name in deploy.ADDON_FILES:
            data = toc if name == "RiftAddon.toc" else "-- fixture " + name
            (self.addon / name).write_text(data, encoding="utf-8")
            (pub / name).write_text(data, encoding="utf-8")
        (self.package / deploy.EXE).write_bytes(b"reader-fixture")
        (self.package / deploy.PRIVATE_CONFIG).write_text(
            '{"deliveryToken":"VERY-PRIVATE-TOKEN"}', encoding="utf-8")
        self.script = self.root / "scripts" / "start_lab1_guest_reader.ps1"
        self.script.parent.mkdir()
        self.script.write_text(
            "param([string]$StageId)\nStart-Process --live\n", encoding="utf-8")
        self.identity = {
            "schemaVersion": 1, "lab": "lab1", "machine": "RIFT-LAB-1",
            "gameProcessId": 7164,
            "gameProcessStartUtcTicks": 639294897866244204,
            "gameExecutablePath": r"C:\RIFT_TRANSFER\RIFT\rift_x64.exe",
            "gameExecutableSha256": "a" * 64,
        }
        self.pin = self.artifacts / "lab1.local.json"
        self.pin.write_text(json.dumps(self.identity), encoding="utf-8")
        self.patchers = [
            mock.patch.object(deploy, "ROOT", self.root),
            mock.patch.object(start, "ROOT", self.root),
            mock.patch.object(start, "START_SCRIPT", self.script),
        ]
        for patcher in self.patchers:
            patcher.start()
            self.addCleanup(patcher.stop)

    @staticmethod
    def receipt(stage_id: str, run_id: str) -> dict:
        return {
            "schemaVersion": 1, "kind": "autofight-lab1-reader-start",
            "stageId": stage_id, "startRunId": run_id, "machine": "RIFT-LAB-1",
            "state": "reader-launched-not-bound",
            "reason": "reader-process-started-no-input",
            "launchAttempted": True, "readerMayHaveStarted": True,
            "readerProcessId": 3010,
            "readerProcessStartUtcTicks": 639294900000000000,
            "gameInput": False, "gameReloaded": False,
            "vmLifecycleChanged": False,
        }

    def test_host_preflight_does_not_access_vm_or_print_token(self) -> None:
        with mock.patch.object(
            deploy, "collect_identity",
            side_effect=AssertionError("unexpected VM access")
        ):
            out = StringIO()
            with redirect_stdout(out):
                code = start.main([
                    "--preflight", "--package", str(self.package),
                    "--identity", str(self.pin),
                ])
        self.assertEqual(code, 0)
        self.assertEqual(json.loads(out.getvalue())["fileCount"], 8)
        self.assertNotIn("VERY-PRIVATE-TOKEN", out.getvalue())

    def test_receipt_is_strict_and_no_game_input(self) -> None:
        receipt = self.receipt("b" * 32, "c" * 32)
        self.assertIs(start.validate_start_receipt(receipt, "b" * 32, "c" * 32), receipt)
        tampered = dict(receipt, gameInput=True)
        with self.assertRaises(deploy.Blocked):
            start.validate_start_receipt(tampered, "b" * 32, "c" * 32)
        tampered = dict(receipt, readerProcessId=0)
        with self.assertRaises(deploy.Blocked):
            start.validate_start_receipt(tampered, "b" * 32, "c" * 32)
        tampered = dict(receipt, unknown="extra")
        with self.assertRaises(deploy.Blocked):
            start.validate_start_receipt(tampered, "b" * 32, "c" * 32)
        tampered = dict(receipt, machine="RIFT-LAB-2")
        with self.assertRaises(deploy.Blocked):
            start.validate_start_receipt(tampered, "b" * 32, "c" * 32)

    def test_blocked_vs_uncertain_receipt_rejects_forged_state(self) -> None:
        r = self.receipt("b" * 32, "c" * 32)
        r.update(state="blocked-before-start", reason="preflight-failed",
                 launchAttempted=False, readerMayHaveStarted=False,
                 readerProcessId=0, readerProcessStartUtcTicks=0)
        start.validate_start_receipt(r, "b" * 32, "c" * 32)
        r["readerMayHaveStarted"] = True
        with self.assertRaises(deploy.Blocked):
            start.validate_start_receipt(r, "b" * 32, "c" * 32)
        r["state"] = "uncertain-reader-start"
        r["launchAttempted"] = True
        start.validate_start_receipt(r, "b" * 32, "c" * 32)

    def test_start_uses_only_guest_copy_and_pinned_powershell_launch(self) -> None:
        stage_id, run_id = "b" * 32, "c" * 32
        files = deploy.validate_package(self.package)
        commands: list[tuple] = []

        class FakeGuest:
            def __init__(self, baseline: tuple): self.baseline = baseline
            def __enter__(self): return self
            def __exit__(self, *args): pass
            def run(self, operation: str, *args: str, timeout: int) -> str:
                commands.append((operation, *args))
                if operation == "copyFileFromGuestToHost":
                    if str(args[0]).endswith("manifest.json"):
                        payload = {
                            "schemaVersion": 1, "kind": "autofight-staging-only",
                            "stageId": stage_id,
                            "files": [vars(item).copy() for item in files],
                        }
                    else:
                        payload = Lab1ReaderStartSpecs.receipt(stage_id, run_id)
                    Path(args[1]).write_text(json.dumps(payload), encoding="utf-8")
                return ""

        with (mock.patch.object(deploy, "collect_identity", return_value=("vm1",)),
              mock.patch.object(deploy, "CachedVmwareGuest", FakeGuest)):
            result = start._start_impl(self.package, files, self.identity,
                                       stage_id, run_id)
        self.assertEqual(result["state"], "reader-launched-not-bound")
        self.assertFalse(result["gameInput"])
        self.assertFalse(result["gameReloaded"])
        self.assertFalse(result["vmLifecycleChanged"])
        self.assertEqual(result["readerProcessId"], 3010)
        self.assertEqual([v[0] for v in commands], [
            "copyFileFromGuestToHost", "copyFileFromHostToGuest",
            "runProgramInGuest", "copyFileFromGuestToHost"
        ])
        invocation = commands[2]
        self.assertIn("-ExpectedGameProcessStartUtcTicks", invocation)
        self.assertIn(str(self.identity["gameProcessStartUtcTicks"]), invocation)
        self.assertIn("-ExpectedLauncherSha256", invocation)
        self.assertIn("start_lab1_guest_reader.ps1", str(commands[1][1]))
        self.assertTrue(all("VERY-PRIVATE-TOKEN" not in repr(c) for c in commands))

    def test_unavailable_receipt_does_not_retry_unknown_launch(self) -> None:
        stage_id, run_id = "d" * 32, "e" * 32
        files = deploy.validate_package(self.package)
        launch_count = 0

        class FakeGuest:
            def __init__(self, baseline: tuple): pass
            def __enter__(self): return self
            def __exit__(self, *args): pass
            def run(self, operation: str, *args: str, timeout: int) -> str:
                nonlocal launch_count
                if operation == "copyFileFromGuestToHost":
                    if args[0].endswith("manifest.json"):
                        Path(args[1]).write_text(json.dumps({
                            "schemaVersion": 1, "kind": "autofight-staging-only",
                            "stageId": stage_id,
                            "files": [vars(item).copy() for item in files],
                        }), encoding="utf-8")
                    else:
                        raise deploy.Blocked("simulated-receipt-missing")
                if operation == "runProgramInGuest":
                    launch_count += 1
                    raise deploy.Blocked("simulated-timeout")
                return ""
        with (mock.patch.object(deploy, "collect_identity", return_value=("vm1",)),
              mock.patch.object(deploy, "CachedVmwareGuest", FakeGuest)):
            with self.assertRaises(start.StartBlocked) as captured:
                start._start_impl(self.package, files, self.identity,
                                  stage_id, run_id)
        self.assertEqual(launch_count, 1)
        self.assertEqual(captured.exception.state, "unknown-reader-start")

    def test_requires_explicit_confirmation_and_stage_id(self) -> None:
        with (mock.patch.object(deploy, "collect_identity",
                                side_effect=AssertionError("unexpected VM"))):
            for args in (
                ["--start"], ["--start", "--confirm", "START-LAB1"],
                ["--start", "--stage-id", "g" * 32, "--confirm", "START-LAB1"],
            ):
                with self.assertRaises(SystemExit):
                    with redirect_stdout(StringIO()):
                        start.main(args)


if __name__ == "__main__":
    unittest.main()
