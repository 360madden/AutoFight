"""Deterministic, host-only contracts for Lab2 reader-only staging.

No VMware calls, guest access, live game input, or filesystem mutations outside
a per-test temporary directory occur in this suite.
"""
import contextlib
import copy
import io
import json
from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "scripts"))
import lab2_guest_reader_deploy as subject


def config(token="a" * 64):
    return {
        "schemaVersion": 1, "processName": "rift_x64",
        "durationSeconds": 900, "pollIntervalMilliseconds": 500,
        "maxScanBytes": 4294967296, "maxScanSeconds": 10,
        "outputPath": None, "freshnessTimeoutSeconds": 5,
        "delivery": {
            "endpoint": "http://192.168.56.1:48761/observation",
            "token": token,
        },
    }


def game_pin():
    return {
        "schemaVersion": 1, "lab": "lab2", "machine": "RIFT-LAB-2",
        "gameProcessId": 9752, "gameProcessStartUtcTicks": 639294897826627677,
        "gameExecutablePath": r"C:\Games\RIFT\rift_x64.exe",
        "gameExecutableSha256": "f" * 64
    }


def valid_receipt(stage_id="a" * 32, run_id="b" * 32):
    return {
        "schemaVersion": 1, "kind": "autofight-lab2-reader-install",
        "stageId": stage_id, "installRunId": run_id,
        "machine": "RIFT-LAB-2", "state": "installed-not-running",
        "reason": "verified-reader-installed-addon-untouched", "readerPromoted": True,
        "addonUntouched": True,
        "readerFinal": subject.GUEST_READER_ROOT,
        "readerPending": subject.GUEST_ROOT + r"\.GuestReader-" + stage_id + ".pending",
        "noReaderStart": True, "noGameReload": True,
        "noGameInput": True, "noVmLifecycleChange": True,
    }


class Lab2HostDeployTests(unittest.TestCase):
    def test_guest_configuration_accepts_private_delivery(self):
        subject.validate_guest_config(config())

    def test_guest_config_rejects_lab1_endpoint_or_token_and_never_discloses_token(self):
        for change in [
            lambda c: c["delivery"].update(endpoint="http://192.168.56.128:48761/observation"),
            lambda c: c["delivery"].update(token="short"),
            lambda c: c.update(processName="wrong"),
            lambda c: c.update(outputPath=r"C:\Users\Public\leak.json"),
            lambda c: c.update(durationSeconds=True),
        ]:
            c = config("SENSITIVE" * 8)
            change(c)
            with self.subTest(change=change), self.assertRaises(subject.Blocked) as got:
                subject.validate_guest_config(c)
            self.assertNotIn("SENSITIVE", str(got.exception))

    def test_json_rejects_case_duplicate_and_nonfinite(self):
        with self.assertRaises(subject.Blocked):
            subject.parse_json(b'{"a":1,"A":2}')
        with self.assertRaises(subject.Blocked):
            subject.parse_json(b'{"x": NaN}')

    def test_lab2_pin_validation(self):
        subject.validate_game_pin(game_pin())
        good = game_pin()
        good["gameExecutablePath"] = r"C:\Program Files (x86)\Glyph\Games\RIFT\Live\rift_x64.exe"
        subject.validate_game_pin(good)
        for change in [
            lambda p: p.update(lab="lab1"),
            lambda p: p.update(machine="RIFT-LAB-1"),
            lambda p: p.update(gameProcessId=0),
            lambda p: p.update(gameProcessStartUtcTicks=True),
            lambda p: p.update(gameExecutablePath=r"C:\Games\RIFT\other.exe"),
            lambda p: p.update(gameExecutablePath=r"C:\Games\RIFT;&calc\rift_x64.exe"),
            lambda p: p.update(gameExecutablePath=r"C:\Games\RIFT|calc\rift_x64.exe"),
            lambda p: p.update(gameExecutablePath=r"C:\Games\RIFT`calc\rift_x64.exe"),
            lambda p: p.update(gameExecutablePath=r"C:\Games\RIFT$calc\rift_x64.exe"),
            lambda p: p.update(gameExecutablePath=r"C:\Games\RIFT%TEMP%\rift_x64.exe"),
            lambda p: p.update(gameExecutablePath=r"C:\Games\..\rift_x64.exe"),
            lambda p: p.update(gameExecutablePath=r"C:\Games\RIFT. \rift_x64.exe"),
            lambda p: p.update(gameExecutablePath=r"C:\CON\RIFT\rift_x64.exe"),
            lambda p: p.update(gameExecutablePath=r"\\?\C:\Games\RIFT\rift_x64.exe"),
            lambda p: p.update(gameExecutablePath=r"C:/Games/RIFT/rift_x64.exe"),
            lambda p: p.update(gameExecutablePath=r"C:\Games\RIFT\rift_x64.exe" + "\n"),
            lambda p: p.update(gameExecutableSha256="g" * 64),
        ]:
            p = game_pin()
            change(p)
            with self.subTest(change=change), self.assertRaises(subject.Blocked):
                subject.validate_game_pin(p)

    def test_manifest_exact_names_hashes_and_order(self):
        files = [
            {"relativePath": subject.EXE, "size": 4, "sha256": "a" * 64},
            {"relativePath": subject.GUEST_CONFIG, "size": 5, "sha256": "b" * 64},
        ]
        addons = [{"relativePath": n, "size": i + 1, "sha256": "c" * 64}
                  for i, n in enumerate(subject.ADDON_NAMES)]
        m = {"schemaVersion": 1, "kind": "autofight-lab2-reader-stage",
             "stageId": "a" * 32, "files": files, "addonFiles": addons}
        subject.validate_manifest(m, "a" * 32, files, addons)
        for mutated in [
            dict(m, kind="autofight-lab1-reader-stage"),
            dict(m, stageId="b" * 32),
            dict(m, files=files + files[:1]),
            dict(m, addonFiles=addons[:-1]),
            dict(m, addonFiles=list(reversed(addons))),
        ]:
            with self.subTest(mutated=mutated), self.assertRaises(subject.Blocked):
                subject.validate_manifest(mutated, "a" * 32, files, addons)

    def test_receipt_accepts_only_exact_machine_and_no_side_effects(self):
        subject.validate_install_receipt(valid_receipt(), "a" * 32, "b" * 32)
        for key, value in [
            ("machine", "RIFT-LAB-1"), ("addonUntouched", False),
            ("noReaderStart", False), ("noGameReload", False),
            ("noGameInput", False), ("noVmLifecycleChange", False),
            ("readerFinal", r"C:\Windows"), ("readerPending", "bad"),
            ("readerPromoted", False),
            ("reason", "verified-installed-no-reload"),
            ("state", "bogus")
        ]:
            item = dict(valid_receipt())
            item[key] = value
            with self.subTest(key=key), self.assertRaises(subject.Blocked):
                subject.validate_install_receipt(item, "a" * 32, "b" * 32)

    def test_host_inputs_require_local_lab2_config_and_hash_six_sources(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            private = root / "artifacts" / "local"
            source = root / "addon" / "AutoFight"
            package = private / "autofight-guest-live-20261105"
            source.mkdir(parents=True)
            package.mkdir(parents=True)
            for name in subject.ADDON_NAMES:
                (source / name).write_bytes(("mock_" + name).encode("ascii"))
            (package / subject.EXE).write_bytes(b"safe-reader-byte-fixture")
            cfg = private / "lab2-guest-reader.local.json"
            cfg.write_text(json.dumps(config()), encoding="utf-8")
            with patch.object(subject, "ROOT", root), patch.object(subject, "PRIVATE_ROOT", private):
                _, files, addon_files = subject.validate_host_inputs(cfg, package)
                self.assertEqual(len(files), 2)
                self.assertEqual(len(addon_files), 6)
                self.assertEqual(files[1]["sha256"], subject.sha256(cfg.read_bytes()))
                self.assertTrue(all("token" not in str(i) for i in files + addon_files))
                cfg_wrong = private / "lab1-guest-reader.local.json"
                cfg_wrong.write_bytes(cfg.read_bytes())
                with self.assertRaises(subject.Blocked):
                    subject.validate_host_inputs(cfg_wrong, package)
                (source / "main.lua").write_bytes(b"")
                with self.assertRaises(subject.Blocked):
                    subject.validate_host_inputs(cfg, package)

    def test_stage_fails_closed_if_exe_changes_after_preflight(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            private = root / "artifacts" / "local"
            addon = root / "addon" / "AutoFight"
            package = private / "autofight-guest-live-20261105"
            addon.mkdir(parents=True)
            package.mkdir(parents=True)
            for name in subject.ADDON_NAMES:
                (addon / name).write_bytes(("addon-" + name).encode("ascii"))
            executable = package / subject.EXE
            executable.write_bytes(b"original-exe")
            cfg = private / "lab2-guest-reader.local.json"
            cfg.write_text(json.dumps(config()), encoding="utf-8")
            pin = private / "lab2-game-identity.local.json"
            pin.write_text(json.dumps(game_pin()), encoding="utf-8")

            class FakeGuest:
                def __enter__(self):
                    return self
                def __exit__(self, *_args):
                    return False
                def run(self, *_args, **_kwargs):
                    return ""

            def drift_after_validated_addon(_session, _expected):
                executable.write_bytes(b"tampered-exe")

            with patch.object(subject, "ROOT", root), \
                    patch.object(subject, "PRIVATE_ROOT", private), \
                    patch.object(subject, "configure_lab2_transport"), \
                    patch.object(subject.base, "collect_identity", return_value=("pinned",)), \
                    patch.object(subject.base, "CachedVmwareGuest", return_value=FakeGuest()), \
                    patch.object(subject, "verify_guest_addon", side_effect=drift_after_validated_addon):
                with self.assertRaises(subject.Blocked) as result:
                    subject._stage(cfg, package, pin)
            self.assertEqual(str(result.exception), "lab2-stage-source-changed-before-copy")
            self.assertEqual(list(private.glob("lab2-stage-*.local.json")), [])

    def test_extension_refuses_any_nonlisting_vmware_command(self):
        class NeverUsed: pass
        with self.assertRaises(subject.Blocked):
            subject._extra(NeverUsed(), "runProgramInGuest", r"C:\Users")

    def test_cli_rejects_mutating_action_without_explicit_private_pin(self):
        with contextlib.redirect_stdout(io.StringIO()) as output:
            result = subject.main(["--stage"])
        self.assertEqual(result, 2)
        self.assertIn("lab2-private-game-pin-required", output.getvalue())
        self.assertNotIn("a" * 64, output.getvalue())


if __name__ == "__main__":
    unittest.main()
