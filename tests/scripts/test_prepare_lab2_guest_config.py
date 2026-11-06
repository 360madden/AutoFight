"""Deterministic no-network tests for Lab2 private GuestReader configuration."""
from __future__ import annotations

import json
from pathlib import Path
import re
import sys
import tempfile
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "scripts"))
import prepare_lab2_guest_config as module


class PrivateLab2ConfigTests(unittest.TestCase):
    def setUp(self) -> None:
        self.directory = tempfile.TemporaryDirectory()
        self.addCleanup(self.directory.cleanup)
        self.root = Path(self.directory.name) / "artifacts" / "local"
        self.root.mkdir(parents=True)
        self.target = self.root / "lab2-guest-reader.local.json"

    def test_valid_destination_only_in_private_root(self) -> None:
        self.assertEqual(module.destination(self.target, self.root), self.target)

    def test_parent_escape_is_blocked(self) -> None:
        with self.assertRaises(module.Blocked):
            module.destination(self.root.parent / "escaped.local.json", self.root)

    def test_invalid_filename_is_blocked(self) -> None:
        for name in (".secret.local.json", "reader.json", "reader.local.txt"):
            with self.subTest(name=name), self.assertRaises(module.Blocked):
                module.destination(self.root / name, self.root)

    def test_existing_config_is_not_overwritten(self) -> None:
        self.target.write_text("prior-token", encoding="utf-8")
        with self.assertRaises(module.Blocked):
            module.create(self.target, self.root)
        self.assertEqual(self.target.read_text(encoding="utf-8"), "prior-token")

    def test_config_conforms_to_guest_csharp_contract(self) -> None:
        token = "abcdef0123456789" * 4
        config = module.build_config(token)
        self.assertEqual(config["schemaVersion"], 1)
        self.assertEqual(config["processName"], "rift_x64")
        self.assertEqual(config["delivery"]["endpoint"], module.ENDPOINT)
        self.assertEqual(config["delivery"]["token"], token)
        self.assertEqual(config["maxScanBytes"], 4 * 1024 * 1024 * 1024)
        self.assertGreaterEqual(config["durationSeconds"], 510)
        self.assertIsNone(config["outputPath"])

    def test_token_validation(self) -> None:
        for value in ("a" * 32, "x" * 64, "A" * 64, ""):
            with self.subTest(value=value[:6]), self.assertRaises(module.Blocked):
                module.build_config(value)

    def test_created_file_contains_unique_64_hex_token(self) -> None:
        module.create(self.target, self.root)
        data = json.loads(self.target.read_text(encoding="utf-8"))
        self.assertRegex(data["delivery"]["token"], r"^[0-9a-f]{64}$")
        self.assertEqual(data["delivery"]["endpoint"], module.ENDPOINT)
        self.assertGreater(self.target.stat().st_size, 80)
        with self.assertRaises(module.Blocked):
            module.create(self.target, self.root)

    def test_separate_configs_use_independent_tokens(self) -> None:
        second = self.root / "second.local.json"
        module.create(self.target, self.root)
        module.create(second, self.root)
        a = json.loads(self.target.read_text(encoding="utf-8"))
        b = json.loads(second.read_text(encoding="utf-8"))
        self.assertNotEqual(a["delivery"]["token"], b["delivery"]["token"])

    def test_preflight_does_not_write(self) -> None:
        module.destination(self.target, self.root)
        self.assertFalse(self.target.exists())


if __name__ == "__main__":
    unittest.main()
