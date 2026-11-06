"""Offline, synthetic acceptance for Lab 2 host telemetry config bootstrap."""
from __future__ import annotations

from contextlib import redirect_stdout
from datetime import datetime, timedelta, timezone
import io
import importlib.util
import json
from unittest.mock import patch
from pathlib import Path
from types import SimpleNamespace
import tempfile
import unittest


SCRIPT = Path(__file__).resolve().parents[2] / "scripts" / "bootstrap_lab2_host_telemetry.py"
SPEC = importlib.util.spec_from_file_location("bootstrap_lab2_host_telemetry", SCRIPT)
bootstrap = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(bootstrap)

NOW = datetime(2030, 1, 1, tzinfo=timezone.utc)
INSTANCE = "123e4567-e89b-12d3-a456-426614174000"
NONCE = "b" * 32
SHA = "a" * 64
GAME = r"C:\RIFT_TRANSFER\RIFT\rift_x64.exe"


def fixtures() -> tuple[dict, dict, dict]:
    pin = {
        "schemaVersion": 1, "lab": "lab2", "machine": "RIFT-LAB-2",
        "gameProcessId": 7164, "gameProcessStartUtcTicks": 639294897866244204,
        "gameExecutablePath": GAME, "gameExecutableSha256": SHA,
    }
    snapshot = {
        "schemaVersion": 1, "readerStatus": "running",
        "instanceId": INSTANCE, "nonce": NONCE, "sequence": 2,
        "capturedAtUtc": NOW.isoformat(),
        "target": {
            "processId": pin["gameProcessId"],
            "processStartUtcTicks": pin["gameProcessStartUtcTicks"],
            "executablePath": GAME, "executableSha256": SHA,
            "readerSessionNonce": NONCE,
        }
    }
    source = {
        "schemaVersion": 1,
        "delivery": {
            "endpoint": "http://192.168.56.1:48761/observation",
            "token": "T" * 64,
        }
    }
    return snapshot, source, pin


def build(snapshot=None, source=None, pin=None, **kwargs):
    baseline = fixtures()
    return bootstrap.create_config(
        snapshot if snapshot is not None else baseline[0],
        source if source is not None else baseline[1],
        pin if pin is not None else baseline[2],
        "192.168.56.129", "192.168.56.1", now=kwargs.get("now", NOW)
    )


class HostBootstrapSpecs(unittest.TestCase):
    def assert_blocked(self, expected, call):
        with self.assertRaises(bootstrap.Blocked) as caught:
            call()
        self.assertEqual(str(caught.exception), expected)

    def test_good_session_uses_exact_host_schema(self):
        host = build()
        self.assertEqual(set(host), {"binding", "bindIpv4", "port", "deliveryToken"})
        self.assertEqual(set(host["binding"]), {
            "lab", "guestIpv4", "readerInstanceId", "readerNonce",
            "gameProcessId", "gameProcessStartUtcTicks",
            "gameExecutablePath", "gameExecutableSha256",
        })
        self.assertEqual(host["binding"]["readerNonce"], NONCE)
        self.assertEqual(host["port"], 48761)
        self.assertEqual(host["deliveryToken"], "T" * 64)

    def test_missing_or_terminated_reader_blocked(self):
        s, _, _ = fixtures()
        s["readerStatus"] = "terminated"
        self.assert_blocked("reader-not-running", lambda: build(snapshot=s))
        s["readerStatus"] = "running"
        s["target"] = None
        self.assert_blocked("reader-target-unavailable", lambda: build(snapshot=s))

    def test_sequence_and_instance_invalid(self):
        s, _, _ = fixtures()
        s["sequence"] = 0
        self.assert_blocked("identity-integer-invalid", lambda: build(snapshot=s))
        s["sequence"] = 1
        s["instanceId"] = "0" * 32
        self.assert_blocked("reader-instance-invalid", lambda: build(snapshot=s))

    def test_stale_and_future_clock_block(self):
        s, _, _ = fixtures()
        s["capturedAtUtc"] = (NOW - timedelta(seconds=31)).isoformat()
        self.assert_blocked("snapshot-stale-or-clock-skew", lambda: build(snapshot=s))
        s["capturedAtUtc"] = (NOW + timedelta(seconds=6)).isoformat()
        self.assert_blocked("snapshot-stale-or-clock-skew", lambda: build(snapshot=s))

    def test_game_process_restarts_and_hash_drift_block(self):
        s, _, _ = fixtures()
        s["target"]["processStartUtcTicks"] += 1
        self.assert_blocked("reader-game-or-nonce-identity-mismatch",
                            lambda: build(snapshot=s))
        s, _, _ = fixtures()
        s["target"]["executableSha256"] = "c" * 64
        self.assert_blocked("reader-game-or-nonce-identity-mismatch",
                            lambda: build(snapshot=s))
        s, _, _ = fixtures()
        s["target"]["readerSessionNonce"] = "c" * 32
        self.assert_blocked("reader-game-or-nonce-identity-mismatch",
                            lambda: build(snapshot=s))

    def test_foreign_lab_pin_blocks(self):
        s, c, pin = fixtures()
        pin["lab"] = "lab1"
        self.assert_blocked("lab2-pin-invalid",
                            lambda: build(snapshot=s, source=c, pin=pin))

    def test_invalid_nonce_or_bad_hash_blocks(self):
        s, c, pin = fixtures()
        s["nonce"] = "not-a-nonce"
        self.assert_blocked("reader-nonce-invalid", lambda: build(snapshot=s))
        s, c, pin = fixtures()
        pin["gameExecutableSha256"] = "NOT-HEX"
        self.assert_blocked("lab2-game-pin-invalid",
                            lambda: build(snapshot=s, source=c, pin=pin))

    def test_endpoint_and_token_must_match(self):
        s, c, pin = fixtures()
        c["delivery"]["endpoint"] = "http://192.168.56.2:48761/observation"
        self.assert_blocked("guest-delivery-endpoint-mismatch", lambda: build(source=c))
        s, c, pin = fixtures()
        c["delivery"]["token"] = "short"
        self.assert_blocked("guest-delivery-token-invalid", lambda: build(source=c))
        s, c, pin = fixtures()
        c["delivery"]["endpoint"] = "http://192.168.56.1:48761/observation?q=token"
        self.assert_blocked("guest-delivery-endpoint-mismatch", lambda: build(source=c))
        s, c, pin = fixtures()
        c["delivery"]["endpoint"] = "http://@192.168.56.1:48761/observation"
        self.assert_blocked("guest-delivery-endpoint-mismatch", lambda: build(source=c))

    def test_ip_source_mismatch(self):
        s, c, pin = fixtures()
        self.assert_blocked(
            "ip-not-private-ipv4",
            lambda: bootstrap.create_config(s, c, pin, "8.8.8.8",
                                            "192.168.56.1", now=NOW)
        )
        self.assert_blocked(
            "lab2-vmnet8-endpoints-mismatch",
            lambda: bootstrap.create_config(s, c, pin, "192.168.55.128",
                                            "192.168.56.1", now=NOW)
        )
        for invalid in ("127.0.0.1", "169.254.0.1", "0.0.0.0"):
            self.assert_blocked(
                "ip-not-private-ipv4",
                lambda invalid=invalid: bootstrap.create_config(
                    s, c, pin, invalid, "192.168.56.1", now=NOW)
            )

    def test_lab2_exact_vmnet8_addresses_only(self):
        s, c, pin = fixtures()
        for guest in ("192.168.56.128", "192.168.56.130", "192.168.55.129"):
            self.assert_blocked(
                "lab2-vmnet8-endpoints-mismatch",
                lambda guest=guest: bootstrap.create_config(
                    s, c, pin, guest, "192.168.56.1", now=NOW))
        for host in ("192.168.56.2", "192.168.56.129"):
            self.assert_blocked(
                "lab2-vmnet8-endpoints-mismatch",
                lambda host=host: bootstrap.create_config(
                    s, c, pin, "192.168.56.129", host, now=NOW))

    def test_lab2_fixed_port_only(self):
        _, c, _ = fixtures()
        c["delivery"]["endpoint"] = "http://192.168.56.1:48762/observation"
        self.assert_blocked("guest-delivery-endpoint-mismatch",
                            lambda: build(source=c))

    def test_lab2_exact_machine_and_reader_process(self):
        s, c, pin = fixtures()
        pin["machine"] = "RIFT-LAB-1"
        self.assert_blocked("lab2-pin-invalid", lambda: build(pin=pin))
        s, c, pin = fixtures()
        s["target"]["processId"] += 1
        self.assert_blocked("reader-game-or-nonce-identity-mismatch",
                            lambda: build(snapshot=s))

    def test_strict_file_json_and_symlink_rejected(self):
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder) / "source.json"
            path.write_text('{"delivery":{},"Delivery":{}}', encoding="utf-8")
            self.assert_blocked("json-invalid", lambda: bootstrap.read_json(path, 1024))
            path.write_text('{"x":NaN}', encoding="utf-8")
            self.assert_blocked("json-invalid", lambda: bootstrap.read_json(path, 1024))
            path.write_text('{"ok":1}', encoding="utf-8")
            self.assertEqual(bootstrap.read_json(path, 1024), {"ok": 1})
            self.assert_blocked("input-file-invalid", lambda: bootstrap.read_json(path, 1))

    def test_cli_validation_never_prints_guest_delivery_token_or_writes(self):
        snapshot, source, pin = fixtures()
        snapshot["capturedAtUtc"] = datetime.now(timezone.utc).isoformat()
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            paths = [root / name for name in ("snapshot.json", "guest.local.json", "pin.json")]
            for path, value in zip(paths, (snapshot, source, pin)):
                path.write_text(json.dumps(value), encoding="utf-8")
            output = root / "host-lab2.local.json"
            argv = ["bootstrap-lab2", "--snapshot", str(paths[0]),
                    "--guest-config", str(paths[1]), "--game-pin", str(paths[2]),
                    "--guest-ip", "192.168.56.129", "--host-ip", "192.168.56.1",
                    "--output", str(output)]
            screen = io.StringIO()
            with patch("sys.argv", argv), redirect_stdout(screen):
                exit_code = bootstrap.main()
            self.assertEqual(exit_code, 0)
            status = json.loads(screen.getvalue())
            self.assertEqual(status["status"], "validated-only")
            self.assertEqual(status["lab"], "lab2")
            self.assertNotIn(source["delivery"]["token"], screen.getvalue())
            self.assertFalse(output.exists())

    def test_enforced_private_root_is_regular_directory(self):
        with tempfile.TemporaryDirectory() as folder:
            private = Path(folder) / "artifacts" / "local"
            private.mkdir(parents=True)
            output = private / "host-lab2.local.json"
            with patch.object(bootstrap, "PRIVATE_ROOT", private):
                bootstrap.write_absent_only(output, build())
            self.assertEqual(json.loads(output.read_text())["binding"]["lab"], "lab2")

    def test_reparse_directory_attribute_is_rejected_without_symlink_privilege(self):
        # Windows junctions and Cloud Files can carry the reparse flag even
        # where ordinary unprivileged symlink creation is denied.
        with tempfile.TemporaryDirectory() as folder:
            private = Path(folder) / "artifacts" / "local"
            private.mkdir(parents=True)
            native_lstat = Path.lstat

            def reparse_lstat(path):
                metadata = native_lstat(path)
                if path == private:
                    return SimpleNamespace(
                        st_mode=metadata.st_mode,
                        st_file_attributes=bootstrap.FILE_ATTRIBUTE_REPARSE_POINT)
                return metadata

            with patch.object(Path, "lstat", reparse_lstat), patch.object(
                    bootstrap, "PRIVATE_ROOT", private):
                self.assert_blocked(
                    "output-private-root-reparse",
                    lambda: bootstrap.write_absent_only(
                        private / "host-lab2.local.json", build()))
            self.assertFalse((private / "host-lab2.local.json").exists())

    def test_real_symlinked_private_directory_rejected(self):
        with tempfile.TemporaryDirectory() as folder:
            base = Path(folder)
            real = base / "destination"
            real.mkdir()
            alias = base / "artifacts" / "local"
            alias.parent.mkdir()
            try:
                alias.symlink_to(real, target_is_directory=True)
            except OSError as exc:
                if getattr(exc, "winerror", None) in (5, 1314):
                    self.skipTest("Windows symlink creation privilege unavailable")
                raise
            output = alias / "host-lab2.local.json"
            with patch.object(bootstrap, "PRIVATE_ROOT", alias):
                self.assert_blocked(
                    "output-private-root-reparse",
                    lambda: bootstrap.write_absent_only(output, build()))
            self.assertFalse((real / "host-lab2.local.json").exists())

    def test_private_output_absent_only(self):
        host = build()
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder) / "host-lab2.local.json"
            bootstrap.write_absent_only(path, host, enforce_private_root=False)
            self.assertEqual(json.loads(path.read_text())["binding"]["readerNonce"], NONCE)
            before = path.read_bytes()
            self.assert_blocked(
                "output-already-exists",
                lambda: bootstrap.write_absent_only(path, host,
                                                    enforce_private_root=False)
            )
            self.assertEqual(path.read_bytes(), before)

    def test_reject_output_not_private(self):
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder) / "host.json"
            self.assert_blocked(
                "output-not-private-json",
                lambda: bootstrap.write_absent_only(path, build(),
                                                    enforce_private_root=False)
            )


if __name__ == "__main__":
    unittest.main(verbosity=2)
