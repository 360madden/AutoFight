"""Synthetic, no-VM tests for read-only pinned Lab1 snapshot fetch."""
from __future__ import annotations

from contextlib import contextmanager
from datetime import datetime, timedelta, timezone
import json
from pathlib import Path
import sys
import tempfile
import unittest
from unittest import mock

SCRIPTS = Path(__file__).resolve().parents[2] / "scripts"
sys.path.insert(0, str(SCRIPTS))
import fetch_lab1_guest_reader_snapshot as fetcher  # noqa: E402

NOW = datetime(2030, 1, 1, tzinfo=timezone.utc)
INSTANCE = "123e4567-e89b-12d3-a456-426614174000"
NONCE = "b" * 32
SHA = "a" * 64
GAME_PATH = r"C:\RIFT_TRANSFER\RIFT\rift_x64.exe"


def fixtures():
    game = {
        "schemaVersion": 1, "lab": "lab1", "machine": "RIFT-LAB-1",
        "gameProcessId": 7164, "gameProcessStartUtcTicks": 639294897866244204,
        "gameExecutablePath": GAME_PATH, "gameExecutableSha256": SHA,
    }
    snapshot = {
        "schemaVersion": 1, "readerStatus": "running",
        "sequence": 9, "instanceId": INSTANCE, "nonce": NONCE,
        "capturedAtUtc": NOW.isoformat(),
        "target": {
            "processId": 7164, "processStartUtcTicks": 639294897866244204,
            "executablePath": GAME_PATH, "executableSha256": SHA,
            "readerSessionNonce": NONCE,
        }
    }
    return game, snapshot


class FakeGuest:
    def __init__(self, snapshot, fail_second_gate=False):
        self.snapshot = snapshot
        self.actions = []
        self.fail_second_gate = fail_second_gate

    def __enter__(self):
        return self

    def __exit__(self, *_):
        return None

    def run(self, method, *args, **_):
        self.actions.append((method, args))
        if method == "copyFileFromGuestToHost":
            assert args[0] == fetcher.DEFAULT_SNAPSHOT
            Path(args[1]).write_text(json.dumps(self.snapshot), encoding="utf-8")
        elif method == "runProgramInGuest":
            assert args[0] == fetcher.deploy.POWERSHELL
            assert args[1:4] == ("-NoProfile", "-NonInteractive", "-Command")
            if self.fail_second_gate and sum(
                action[0] == "runProgramInGuest" for action in self.actions
            ) == 2:
                raise fetcher.deploy.Blocked("guest-operation-failed:runProgramInGuest")
        else:
            raise AssertionError("unexpected guest operation " + method)


class FetchSpecs(unittest.TestCase):
    def check_blocked(self, code, action):
        with self.assertRaises(fetcher.FetchBlocked) as caught:
            action()
        self.assertEqual(str(caught.exception), code)

    def test_exact_process_assertion_only_reads(self):
        pin, _ = fixtures()
        script = fetcher.identity_powershell(1357, 123456789, pin, "c" * 64)
        for required in (
            "Get-Process", "Get-FileHash", "reader-not-unique", "game-hash-mismatch",
            "Rift.Automation.GuestReader.exe", "competing-rr2-reader",
            "123456789", "1357", "7164"
        ):
            self.assertIn(required, script)
        for excluded in ("Start-Process", "Set-Content", "Remove-Item", "SendKeys",
                         "Set-Item", "Set-Process", "Stop-Process"):
            self.assertNotIn(excluded, script)

    def test_invalid_reader_or_game_pin_rejected(self):
        pin, _ = fixtures()
        self.check_blocked("reader-process-pin-invalid",
                           lambda: fetcher.identity_powershell(0, 2, pin, SHA))
        self.check_blocked("reader-process-pin-invalid",
                           lambda: fetcher.identity_powershell(1, 0, pin, SHA))
        self.check_blocked("reader-process-pin-invalid",
                           lambda: fetcher.identity_powershell(1, 2, pin, "bad"))
        pin["gameExecutablePath"] = r"C:\RIFT_TRANSFER\RIFT\rift_x64.exe';Stop-Process"
        self.check_blocked("game-process-pin-invalid",
                           lambda: fetcher.identity_powershell(1, 2, pin, SHA))

    def test_valid_snapshot_fields(self):
        pin, snap = fixtures()
        report = fetcher.verify_snapshot(snap, pin, now=NOW)
        self.assertEqual(report["readerNonce"], NONCE)
        self.assertEqual(report["readerInstanceId"], INSTANCE)
        self.assertEqual(report["sequence"], 9)

    def test_reader_termination_and_game_change_rejected(self):
        pin, snap = fixtures()
        snap["readerStatus"] = "terminated"
        self.check_blocked("reader-not-running",
                           lambda: fetcher.verify_snapshot(snap, pin, now=NOW))
        pin, snap = fixtures()
        snap["target"]["processId"] += 1
        self.check_blocked("snapshot-game-identity-mismatch",
                           lambda: fetcher.verify_snapshot(snap, pin, now=NOW))
        pin, snap = fixtures()
        snap["target"]["readerSessionNonce"] = "c" * 32
        self.check_blocked("snapshot-game-or-reader-identity-mismatch",
                           lambda: fetcher.verify_snapshot(snap, pin, now=NOW))

    def test_stale_and_unavailable_rejected(self):
        pin, snap = fixtures()
        snap["capturedAtUtc"] = (NOW - timedelta(seconds=45)).isoformat()
        self.check_blocked("snapshot-stale-or-clock-skew",
                           lambda: fetcher.verify_snapshot(snap, pin, now=NOW))
        pin, snap = fixtures()
        snap["target"] = None
        self.check_blocked("reader-target-missing",
                           lambda: fetcher.verify_snapshot(snap, pin, now=NOW))

    def test_fetch_copies_one_snapshot_and_rechecks_identity(self):
        pin, snap = fixtures()
        # Inject timestamp freshly relative to now for an exact non-live test.
        snap["capturedAtUtc"] = datetime.now(timezone.utc).isoformat()
        guest = FakeGuest(snap)
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            output = root / "lab1-guest-latest-fake.local.json"
            identity = ("synthetic-lab1-identity",)
            with mock.patch.object(fetcher, "PRIVATE", root), \
                 mock.patch.object(fetcher.deploy, "collect_identity", return_value=identity), \
                 mock.patch.object(fetcher.deploy, "CachedVmwareGuest", return_value=guest):
                report = fetcher.fetch(1357, 123456789, pin, SHA, output)
            self.assertEqual(report["readerNonce"], NONCE)
            self.assertTrue(output.is_file())
            self.assertEqual([action[0] for action in guest.actions],
                             ["runProgramInGuest", "copyFileFromGuestToHost",
                              "runProgramInGuest"])

    def test_second_game_gate_failure_leaves_no_final_file(self):
        pin, snap = fixtures()
        snap["capturedAtUtc"] = datetime.now(timezone.utc).isoformat()
        guest = FakeGuest(snap, fail_second_gate=True)
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            output = root / "snapshot.local.json"
            with mock.patch.object(fetcher, "PRIVATE", root), \
                 mock.patch.object(fetcher.deploy, "collect_identity", return_value=("vm",)), \
                 mock.patch.object(fetcher.deploy, "CachedVmwareGuest", return_value=guest):
                with self.assertRaises(fetcher.deploy.Blocked):
                    fetcher.fetch(1357, 123456789, pin, SHA, output)
            self.assertFalse(output.exists())

    def test_reject_existing_final_output(self):
        pin, _ = fixtures()
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            output = root / "snapshot.local.json"
            output.write_text("preserve", encoding="utf-8")
            with mock.patch.object(fetcher, "PRIVATE", root):
                self.check_blocked(
                    "output-already-exists-or-not-private",
                    lambda: fetcher.fetch(1357, 123456789, pin, SHA, output)
                )
            self.assertEqual(output.read_text(), "preserve")


if __name__ == "__main__":
    unittest.main(verbosity=2)
