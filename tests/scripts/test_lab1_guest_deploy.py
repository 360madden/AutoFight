r"""Deterministic offline specs for AutoFight's own Lab1 inactive guest staging.

No vmrun, game process, real credentials, or guest process is touched.
Run on Windows: C:\Python314\python.exe -m unittest discover -s tests\scripts -p test_lab1_guest_deploy.py -v
"""
from __future__ import annotations

from contextlib import redirect_stdout
from io import StringIO
from pathlib import Path
import json
import sys
import tempfile
import unittest
from unittest import mock

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "scripts"))
import lab1_guest_deploy as deployment


class Lab1DeploymentSpecs(unittest.TestCase):
    def setUp(self) -> None:
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.publisher = self.root / "artifacts" / "local" / "build"
        self.publisher.mkdir(parents=True)
        self.source = self.root / "addon" / "AutoFight"
        self.source.mkdir(parents=True)
        published_addon = self.publisher / "addon" / "AutoFight"
        published_addon.mkdir(parents=True)
        toc = ('Identifier = "AutoFight"\n' +
               'RunOnStartup = { "wire.lua", "producer.lua", "live.lua", '
               '"guidance.lua", "main.lua" }\n')
        for name in deployment.ADDON_FILES:
            data = toc if name == "RiftAddon.toc" else "-- sample " + name
            (self.source / name).write_text(data, encoding="utf-8")
            (published_addon / name).write_text(data, encoding="utf-8")
        (self.publisher / deployment.EXE).write_bytes(b"exe fixture")
        (self.publisher / deployment.PDB).write_bytes(b"symbols not staged")
        (self.publisher / deployment.PRIVATE_CONFIG).write_text(
            '{"deliveryToken":"DO-NOT-PRINT"}', encoding="utf-8"
        )
        self.patcher = mock.patch.object(deployment, "ROOT", self.root)
        self.patcher.start()
        self.addCleanup(self.patcher.stop)

    def test_eight_payload_files_pdb_not_staged(self) -> None:
        files = deployment.validate_package(self.publisher)
        self.assertEqual(len(files), 8)
        self.assertIn(deployment.EXE, [item.relativePath for item in files])
        self.assertIn(deployment.PRIVATE_CONFIG, [item.relativePath for item in files])
        self.assertNotIn(deployment.PDB, [item.relativePath for item in files])
        self.assertTrue(all(len(item.sha256) == 64 for item in files))

    def test_absent_optional_private_config_and_pdb_is_accepted(self) -> None:
        (self.publisher / deployment.PRIVATE_CONFIG).unlink()
        (self.publisher / deployment.PDB).unlink()
        files = deployment.validate_package(self.publisher)
        self.assertEqual(len(files), 7)

    def test_private_config_not_in_stdout(self) -> None:
        with mock.patch.object(deployment, "collect_identity",
                               side_effect=AssertionError("unexpected-vm-access")):
            output = StringIO()
            with redirect_stdout(output):
                exit_code = deployment.main(
                    ["--preflight", "--package", str(self.publisher)]
                )
        self.assertEqual(exit_code, 0)
        text = output.getvalue()
        self.assertNotIn("DO-NOT-PRINT", text)
        self.assertEqual(len(json.loads(text)["fileHashes"]), 7)

    def test_reject_unexpected_or_missing_file(self) -> None:
        intruder = self.publisher / "evil.cmd"
        intruder.write_text("unknown", encoding="utf-8")
        with self.assertRaisesRegex(deployment.Blocked, "package-invalid"):
            deployment.validate_package(self.publisher)
        intruder.unlink()
        (self.publisher / "addon" / "AutoFight" / "wire.lua").unlink()
        with self.assertRaisesRegex(deployment.Blocked, "package-invalid"):
            deployment.validate_package(self.publisher)

    def test_reject_source_mismatch_and_wrong_toc_order(self) -> None:
        path = self.publisher / "addon" / "AutoFight" / "live.lua"
        path.write_bytes(b"wrong")
        with self.assertRaisesRegex(deployment.Blocked, "package-invalid"):
            deployment.validate_package(self.publisher)
        path.write_bytes((self.source / "live.lua").read_bytes())
        toc = self.publisher / "addon" / "AutoFight" / "RiftAddon.toc"
        replacement = toc.read_text(encoding="utf-8").replace(
            '"wire.lua", "producer.lua"', '"producer.lua", "wire.lua"'
        )
        toc.write_text(replacement, encoding="utf-8")
        (self.source / "RiftAddon.toc").write_text(replacement, encoding="utf-8")
        with self.assertRaisesRegex(deployment.Blocked, "package-invalid"):
            deployment.validate_package(self.publisher)

    def test_receipt_rejects_changed_hash_duplicate_or_wrong_machine(self) -> None:
        files = deployment.validate_package(self.publisher)
        def receipt() -> dict:
            return {
                "schemaVersion": 1, "kind": "autofight-staging-only",
                "stageId": "a" * 32, "machine": "RIFT-LAB-1",
                "verified": True, "reason": "verified-staged-only",
                "files": [vars(item).copy() for item in files],
            }
        deployment.validate_guest_receipt(receipt(), "a" * 32, files)
        forged = receipt()
        forged["files"][0]["sha256"] = "0" * 64
        with self.assertRaisesRegex(deployment.Blocked, "hash-mismatch"):
            deployment.validate_guest_receipt(forged, "a" * 32, files)
        duplicated = receipt()
        duplicated["files"][-1] = duplicated["files"][0]
        with self.assertRaises(deployment.Blocked):
            deployment.validate_guest_receipt(duplicated, "a" * 32, files)
        wrong = receipt()
        wrong["machine"] = "RIFT-LAB-2"
        with self.assertRaises(deployment.Blocked):
            deployment.validate_guest_receipt(wrong, "a" * 32, files)

    def test_explicit_stage_only_in_inactive_namespace(self) -> None:
        files = deployment.validate_package(self.publisher)
        calls: list[tuple] = []
        manifest = {}

        class FakeGuest:
            def __init__(self, baseline: tuple):
                self.baseline = baseline

            def __enter__(self) -> "FakeGuest":
                return self

            def __exit__(self, *_args: object) -> None:
                pass

            def run(self, operation: str, *args: str, timeout: int) -> str:
                calls.append((operation, *args))
                if operation == "copyFileFromHostToGuest" and args[1].endswith(
                    "manifest.json"
                ):
                    manifest.update(json.loads(Path(args[0]).read_text(encoding="utf-8")))
                if operation == "copyFileFromGuestToHost":
                    receipt = {
                        "schemaVersion": 1, "kind": "autofight-staging-only",
                        "stageId": manifest["stageId"], "machine": "RIFT-LAB-1",
                        "verified": True, "reason": "verified-staged-only",
                        "files": manifest["files"],
                    }
                    Path(args[1]).write_text(json.dumps(receipt), encoding="utf-8")
                return ""

        with (mock.patch.object(deployment, "collect_identity", return_value=("vm1",)),
              mock.patch.object(deployment, "CachedVmwareGuest", FakeGuest),
              mock.patch.object(deployment, "STAGE_VERIFIER", self.source / "wire.lua")):
            result = deployment._stage(self.publisher, files)
        self.assertEqual(result["state"], "verified-staged-only")
        self.assertFalse(result["addonInstalled"])
        self.assertFalse(result["guestReaderStarted"])
        self.assertEqual(sum(c[0] == "copyFileFromHostToGuest" for c in calls),
                         len(files) + 2)
        dests = [c[2] for c in calls if c[0] == "copyFileFromHostToGuest"]
        self.assertTrue(all(
            path.startswith(deployment.GUEST_STAGE_PARENT + "\\") for path in dests
        ))
        self.assertFalse(any("Interface\\AddOns" in path for path in dests))
        self.assertFalse(any(path.endswith(".pdb") for path in dests))
        self.assertFalse(any(
            any(forbidden in " ".join(c).lower()
                for forbidden in ("reloadui", "install-addon", "run-game", "startvm"))
            for c in calls
        ))

    def test_interrupted_stage_reports_exact_orphan_location_without_secrets(self) -> None:
        with (mock.patch.object(deployment, "_stage_impl",
                                side_effect=deployment.Blocked("guest-operation-timeout")),
              mock.patch.object(deployment, "STAGE_VERIFIER", self.source / "wire.lua")):
            output = StringIO()
            with redirect_stdout(output):
                exit_code = deployment.main([
                    "--stage", "--confirm", "STAGE-LAB1",
                    "--package", str(self.publisher),
                ])
        self.assertEqual(exit_code, 2)
        value = json.loads(output.getvalue())
        self.assertTrue(value["possiblePartialStage"])
        self.assertEqual(len(value["stageId"]), 32)
        self.assertTrue(value["guestStageDirectory"].startswith(
            deployment.GUEST_STAGE_PARENT + "\\"
        ))
        self.assertTrue(value["manualCleanupRequiredIfPresent"])
        self.assertNotIn("DO-NOT-PRINT", output.getvalue())

    def test_private_exact_game_identity_schema(self) -> None:
        value = {
            "schemaVersion": 1, "lab": "lab1", "machine": "RIFT-LAB-1",
            "gameProcessId": 7164, "gameProcessStartUtcTicks": 639294897866244204,
            "gameExecutablePath": r"C:\RIFT_TRANSFER\RIFT\rift_x64.exe",
            "gameExecutableSha256": "a" * 64,
        }
        path = self.root / "artifacts" / "local" / "test.local.json"
        path.write_text(json.dumps(value), encoding="utf-8")
        self.assertEqual(deployment.load_game_identity(path), value)
        value["gameProcessId"] = True
        path.write_text(json.dumps(value), encoding="utf-8")
        with self.assertRaises(deployment.Blocked):
            deployment.load_game_identity(path)
        value["gameProcessId"] = 7164
        path.write_text(json.dumps(value) + "\n" + json.dumps(value), encoding="utf-8")
        with self.assertRaises(deployment.Blocked):
            deployment.load_game_identity(path)

    def test_staged_manifest_requires_exact_current_eight_hashes(self) -> None:
        files = deployment.validate_package(self.publisher)
        stage_id = "b" * 32
        data = {
            "schemaVersion": 1, "kind": "autofight-staging-only",
            "stageId": stage_id, "files": [vars(item).copy() for item in files]
        }
        deployment.validate_staged_manifest(data, stage_id, files)
        data["files"][0]["sha256"] = "0" * 64
        with self.assertRaisesRegex(deployment.Blocked, "stage-source-drift"):
            deployment.validate_staged_manifest(data, stage_id, files)
        data["files"][0]["sha256"] = files[0].sha256
        data["files"][-1] = data["files"][0].copy()
        with self.assertRaises(deployment.Blocked):
            deployment.validate_staged_manifest(data, stage_id, files)

    def test_install_receipt_partial_vs_complete(self) -> None:
        stage_id = "c" * 32
        run_id = "e" * 32
        data = {
            "schemaVersion": 1, "kind": "autofight-lab1-install",
            "stageId": stage_id, "installRunId": run_id, "machine": "RIFT-LAB-1",
            "state": "installed-not-running",
            "reason": "verified-installed-no-reload",
            "readerPromoted": True, "addonPromoted": True,
            "readerFinal": r"C:\Users\mrkoo\AppData\Local\AutoFight\GuestReader",
            "addonFinal": (r"C:\Users\mrkoo\OneDrive\Documents\RIFT\Interface"
                           r"\AddOns\AutoFight"),
            "readerPending": (r"C:\Users\mrkoo\AppData\Local\AutoFight"
                              + r"\.GuestReader-" + stage_id + ".pending"),
            "addonPending": (r"C:\Users\mrkoo\OneDrive\Documents\RIFT\Interface"
                             + r"\.AutoFight-" + stage_id + ".pending"),
            "noReaderStart": True, "noGameReload": True, "noGameInput": True,
        }
        deployment.validate_install_receipt(data, stage_id, run_id)
        data["addonPromoted"] = False
        with self.assertRaisesRegex(deployment.Blocked, "inconsistent"):
            deployment.validate_install_receipt(data, stage_id, run_id)
        data["state"] = "partial-install-needs-review"
        deployment.validate_install_receipt(data, stage_id, run_id)
        with self.assertRaises(deployment.Blocked):
            deployment.validate_install_receipt(data, stage_id, "f" * 32)

    def test_install_requires_confirmation_and_stage_id(self) -> None:
        for args in (["--install"], ["--install", "--confirm", "INSTALL-LAB1"],
                     ["--install", "--stage-id", "c" * 32],
                     ["--install", "--stage-id", "unsafe/path", "--confirm",
                      "INSTALL-LAB1"]):
            with self.assertRaises(SystemExit) as caught:
                deployment.main(args + ["--package", str(self.publisher)])
            self.assertEqual(caught.exception.code, 2)

    def test_install_blocked_receipt_marks_unknown_promotion(self) -> None:
        files = deployment.validate_package(self.publisher)
        with mock.patch.object(deployment, "_install_impl",
                               side_effect=deployment.Blocked("guest-lost")):
            with self.assertRaises(deployment.InstallBlocked) as caught:
                deployment._install(self.publisher, files, "d" * 32, {})
        error = caught.exception
        self.assertEqual(error.state, "unknown-install-state")
        self.assertIsNone(error.reader_promoted)
        self.assertIsNone(error.addon_promoted)

    def test_installer_checks_exact_kernel_start_ticks(self) -> None:
        # Lab1 pin has 100 ns precision; WMI CreationDate loses its final digit.
        native_ticks = 639294897866244204
        cim_ticks = 639294897866244200
        self.assertNotEqual(native_ticks, cim_ticks)
        source = (Path(__file__).resolve().parents[2] / "scripts" /
                  "install_lab1_guest_stage.ps1").read_text(encoding="utf-8")
        self.assertIn("$live=Get-Process -Id $ExpectedGameProcessId", source)
        self.assertIn("$live.StartTime.ToUniversalTime().Ticks", source)
        self.assertNotIn("$record.CreationDate.ToUniversalTime().Ticks", source)
        self.assertIn("Get-CimInstance Win32_Process", source)
        self.assertIn("Get-FileHash -LiteralPath $record.ExecutablePath", source)

    def test_stage_requires_second_explicit_confirmation(self) -> None:
        with self.assertRaises(SystemExit) as caught:
            deployment.main(["--stage", "--package", str(self.publisher)])
        self.assertEqual(caught.exception.code, 2)
        with self.assertRaises(SystemExit) as caught:
            deployment.main(["--preflight", "--confirm", "STAGE-LAB1",
                             "--package", str(self.publisher)])
        self.assertEqual(caught.exception.code, 2)


if __name__ == "__main__":
    unittest.main()
