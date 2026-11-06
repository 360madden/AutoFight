"""Fail-closed, single-session Lab 1 host telemetry configuration bootstrap.

This script does not start a receiver or reader, contact a VM, or send game
input. Its only optional side effect (--write) is one absent-only private JSON
under AutoFight/artifacts/local. Guest snapshot/identity must be independently
revalidated before host runtime acceptance.
"""
from __future__ import annotations

import argparse
from datetime import datetime, timezone
import ipaddress
import json
import os
from pathlib import Path
import re
import stat
import tempfile
from urllib.parse import urlsplit
from uuid import UUID

ROOT = Path(__file__).resolve().parents[1]
PRIVATE_ROOT = ROOT / "artifacts" / "local"
HEX32 = re.compile(r"[0-9a-f]{32}\Z")
HEX64 = re.compile(r"[0-9a-f]{64}\Z")
FILE_ATTRIBUTE_REPARSE_POINT = 0x400


class Blocked(ValueError):
    """Stable, nonsecret rejection code."""


def _blocked(code: str) -> None:
    raise Blocked(code)


def _unique_object(pairs: list[tuple[str, object]]) -> dict:
    value = {}
    used = set()
    for key, item in pairs:
        lowered = key.casefold()
        if lowered in used:
            _blocked("json-duplicate-property")
        used.add(lowered)
        value[key] = item
    return value


def _reject_constant(_value: str) -> None:
    _blocked("json-nonfinite-number")


def _regular_file(path: Path, limit: int) -> bytes:
    try:
        meta = path.lstat()
        if (not stat.S_ISREG(meta.st_mode) or
                meta.st_size < 1 or meta.st_size > limit or
                (getattr(meta, "st_file_attributes", 0) & FILE_ATTRIBUTE_REPARSE_POINT)):
            _blocked("input-file-invalid")
        with path.open("rb") as stream:
            data = stream.read(limit + 1)
        if len(data) != meta.st_size:
            _blocked("input-file-changed")
        return data
    except OSError:
        _blocked("input-file-unavailable")


def read_json(path: Path, limit: int) -> dict:
    data = _regular_file(path, limit)
    try:
        obj = json.loads(data.decode("utf-8-sig"),
                         object_pairs_hook=_unique_object,
                         parse_constant=_reject_constant)
    except (ValueError, UnicodeError):
        _blocked("json-invalid")
    if not isinstance(obj, dict):
        _blocked("json-root-invalid")
    return obj


def _integer(value: object, *, minimum: int = 1) -> int:
    if type(value) is not int or value < minimum:
        _blocked("identity-integer-invalid")
    return value


def _private_ipv4(raw: object) -> str:
    if not isinstance(raw, str):
        _blocked("ip-invalid")
    try:
        address = ipaddress.IPv4Address(raw)
    except ipaddress.AddressValueError:
        _blocked("ip-invalid")
    # Match the host C# listener's RFC1918-only rule; is_private alone
    # would also admit loopback, link-local, or unspecified addresses.
    rfc1918 = (ipaddress.IPv4Network("10.0.0.0/8"),
               ipaddress.IPv4Network("172.16.0.0/12"),
               ipaddress.IPv4Network("192.168.0.0/16"))
    if str(address) != raw or not any(address in subnet for subnet in rfc1918):
        _blocked("ip-not-private-ipv4")
    return str(address)


def create_config(snapshot: dict, guest_config: dict, pin: dict,
                  guest_ip: str, host_ip: str,
                  *, now: datetime | None = None) -> dict:
    if (pin.get("schemaVersion") != 1 or pin.get("lab") != "lab1" or
            pin.get("machine") != "RIFT-LAB-1"):
        _blocked("lab1-pin-invalid")
    process_id = _integer(pin.get("gameProcessId"))
    start_ticks = _integer(pin.get("gameProcessStartUtcTicks"))
    game_path = pin.get("gameExecutablePath")
    game_sha = pin.get("gameExecutableSha256")
    if (not isinstance(game_path, str) or
            not game_path.casefold().endswith(r"\rift_x64.exe") or
            not isinstance(game_sha, str) or not HEX64.fullmatch(game_sha)):
        _blocked("lab1-game-pin-invalid")
    if snapshot.get("schemaVersion") != 1 or snapshot.get("readerStatus") != "running":
        _blocked("reader-not-running")
    sequence = _integer(snapshot.get("sequence"))
    if sequence < 1:
        _blocked("reader-sequence-invalid")
    instance_id = snapshot.get("instanceId")
    nonce = snapshot.get("nonce")
    try:
        if not isinstance(instance_id, str) or str(UUID(instance_id)) != instance_id:
            _blocked("reader-instance-invalid")
    except ValueError:
        _blocked("reader-instance-invalid")
    if not isinstance(nonce, str) or HEX32.fullmatch(nonce) is None:
        _blocked("reader-nonce-invalid")
    stamp = snapshot.get("capturedAtUtc")
    try:
        if not isinstance(stamp, str):
            _blocked("snapshot-time-invalid")
        capture = datetime.fromisoformat(stamp.replace("Z", "+00:00"))
        if capture.tzinfo is None:
            _blocked("snapshot-time-invalid")
        capture = capture.astimezone(timezone.utc)
    except ValueError:
        _blocked("snapshot-time-invalid")
    reference = now or datetime.now(timezone.utc)
    if reference.tzinfo is None:
        _blocked("host-time-invalid")
    age = (reference.astimezone(timezone.utc) - capture).total_seconds()
    if not -5 <= age <= 30:
        _blocked("snapshot-stale-or-clock-skew")
    target = snapshot.get("target")
    if not isinstance(target, dict):
        _blocked("reader-target-unavailable")
    if (type(target.get("processId")) is not int or
            target["processId"] != process_id or
            type(target.get("processStartUtcTicks")) is not int or
            target["processStartUtcTicks"] != start_ticks or
            not isinstance(target.get("executablePath"), str) or
            target["executablePath"].casefold() != game_path.casefold() or
            not isinstance(target.get("executableSha256"), str) or
            target["executableSha256"].casefold() != game_sha or
            target.get("readerSessionNonce") != nonce):
        _blocked("reader-game-or-nonce-identity-mismatch")
    guest_address = _private_ipv4(guest_ip)
    host_address = _private_ipv4(host_ip)
    if (guest_address == host_address or
            ipaddress.IPv4Network(guest_address + "/24", strict=False) !=
            ipaddress.IPv4Network(host_address + "/24", strict=False)):
        _blocked("vmnet8-network-mismatch")
    delivery = guest_config.get("delivery")
    if guest_config.get("schemaVersion") != 1 or not isinstance(delivery, dict):
        _blocked("guest-delivery-config-invalid")
    token = delivery.get("token")
    if (not isinstance(token, str) or len(token) != 64 or
            not token.isascii() or any(not 33 <= ord(ch) <= 126 for ch in token)):
        _blocked("guest-delivery-token-invalid")
    endpoint = delivery.get("endpoint")
    try:
        parsed = urlsplit(endpoint) if isinstance(endpoint, str) else None
        valid_endpoint = (parsed is not None and
                          parsed.scheme == "http" and parsed.hostname == host_address and
                          parsed.port is not None and 1024 <= parsed.port <= 65535 and
                          parsed.path == "/observation" and
                          parsed.netloc == f"{host_address}:{parsed.port}" and
                          not parsed.query and not parsed.fragment)
    except ValueError:
        valid_endpoint = False
    if not valid_endpoint:
        _blocked("guest-delivery-endpoint-mismatch")
    return {
        "binding": {
            "lab": "lab1",
            "guestIpv4": guest_address,
            "readerInstanceId": instance_id,
            "readerNonce": nonce,
            "gameProcessId": process_id,
            "gameProcessStartUtcTicks": start_ticks,
            "gameExecutablePath": game_path,
            "gameExecutableSha256": game_sha,
        },
        "bindIpv4": host_address,
        "port": parsed.port,
        "deliveryToken": token,
    }


def write_absent_only(output: Path, config: dict,
                      *, enforce_private_root: bool = True) -> None:
    if output.suffix.lower() != ".json" or not output.name.endswith(".local.json"):
        _blocked("output-not-private-json")
    if enforce_private_root:
        try:
            if output.parent.resolve(strict=True) != PRIVATE_ROOT.resolve(strict=True):
                _blocked("output-outside-private-root")
        except OSError:
            _blocked("output-private-root-unavailable")
    if output.exists() or output.is_symlink():
        _blocked("output-already-exists")
    data = (json.dumps(config, sort_keys=True, separators=(",", ":")) + "\n").encode("utf-8")
    if len(data) > 16 * 1024:
        _blocked("output-config-too-large")
    temporary_name = None
    try:
        with tempfile.NamedTemporaryFile(
            mode="wb", prefix=".host-telemetry-", suffix=".tmp",
            dir=output.parent, delete=False
        ) as stream:
            temporary_name = stream.name
            os.chmod(temporary_name, 0o600)
            stream.write(data)
            stream.flush()
            os.fsync(stream.fileno())
        # The hard link publishes fully flushed bytes atomically and never
        # replaces any existing private configuration (including a race).
        os.link(temporary_name, output)
    except FileExistsError:
        _blocked("output-already-exists")
    except OSError:
        _blocked("private-output-write-failed")
    finally:
        if temporary_name is not None:
            try:
                Path(temporary_name).unlink(missing_ok=True)
            except OSError:
                pass


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--snapshot", required=True, type=Path)
    parser.add_argument("--guest-config", required=True, type=Path)
    parser.add_argument("--game-pin", required=True, type=Path)
    parser.add_argument("--guest-ip", required=True)
    parser.add_argument("--host-ip", required=True)
    parser.add_argument("--output", default=str(PRIVATE_ROOT / "host-lab1.local.json"),
                        type=Path)
    parser.add_argument("--write", action="store_true",
                        help="Create the absent private file (never overwrite).")
    args = parser.parse_args()
    try:
        snapshot = read_json(args.snapshot, 128 * 1024)
        source = read_json(args.guest_config, 64 * 1024)
        pin = read_json(args.game_pin, 32 * 1024)
        config = create_config(snapshot, source, pin, args.guest_ip, args.host_ip)
        if args.write:
            write_absent_only(args.output, config)
        print(json.dumps({"status": "written" if args.write else "validated-only",
                          "lab": "lab1", "port": config["port"],
                          "readerInstanceId": config["binding"]["readerInstanceId"],
                          "readerNonce": config["binding"]["readerNonce"],
                          "gameProcessId": config["binding"]["gameProcessId"],
                          "output": str(args.output)}))
        return 0
    except Blocked as exc:
        print(json.dumps({"status": "blocked", "reason": str(exc)}))
        return 2
    except BaseException:
        print(json.dumps({"status": "failed", "reason": "unexpected-error"}))
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
