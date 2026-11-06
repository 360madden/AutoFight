"""Create a private, Lab2-only AutoFight GuestReader delivery configuration.

No guest access, game scan, input, receiver, or VM lifecycle action. The
output is an absent-only ignored .local.json in artifacts/local. Never print
the bearer token or serialize the configuration to stdout.
"""
from __future__ import annotations

import argparse
import json
import os
from pathlib import Path
import secrets
import stat
import sys

ROOT = Path(__file__).resolve().parents[1]
PRIVATE_ROOT = ROOT / "artifacts" / "local"
DEFAULT_NAME = "lab2-guest-reader.local.json"
ENDPOINT = "http://192.168.56.1:48761/observation"
FILE_ATTRIBUTE_REPARSE_POINT = 0x400


class Blocked(ValueError):
    """Stable non-secret operator diagnostic."""


def destination(path: Path, private_root: Path = PRIVATE_ROOT) -> Path:
    try:
        root = private_root.resolve(strict=True)
        info = root.lstat()
        if (not stat.S_ISDIR(info.st_mode) or
                getattr(info, "st_file_attributes", 0) & FILE_ATTRIBUTE_REPARSE_POINT):
            raise ValueError()
        candidate = path.resolve(strict=False)
        if (candidate.parent != root or
                not candidate.name.endswith(".local.json") or
                candidate.name.startswith(".") or path.is_symlink() or
                candidate.exists()):
            raise ValueError()
        return candidate
    except (OSError, ValueError):
        raise Blocked("lab2-private-output-not-available") from None


def build_config(token: str) -> dict:
    if len(token) != 64 or any(character not in "0123456789abcdef" for character in token):
        raise Blocked("lab2-private-token-invalid")
    return {
        "schemaVersion": 1,
        "processName": "rift_x64",
        "durationSeconds": 900,
        "pollIntervalMilliseconds": 500,
        "maxScanBytes": 4 * 1024 * 1024 * 1024,
        "maxScanSeconds": 10,
        "outputPath": None,
        "freshnessTimeoutSeconds": 5,
        "delivery": {"endpoint": ENDPOINT, "token": token},
    }


def create(path: Path, private_root: Path = PRIVATE_ROOT) -> Path:
    target = destination(path, private_root)
    config = build_config(secrets.token_hex(32))
    data = (json.dumps(config, sort_keys=True, separators=(",", ":")) + "\n").encode("utf-8")
    if not 0 < len(data) < 16 * 1024:
        raise Blocked("lab2-private-config-size-invalid")
    # O_EXCL prevents overwriting an existing config, even if a race occurs
    # after destination preflight. No replacement of an earlier token.
    try:
        handle = os.open(str(target), os.O_CREAT | os.O_EXCL | os.O_WRONLY, 0o600)
    except OSError:
        raise Blocked("lab2-private-output-create-failed") from None
    try:
        with os.fdopen(handle, "wb", closefd=True) as stream:
            stream.write(data)
            stream.flush()
            os.fsync(stream.fileno())
    except OSError:
        # Leave any partial file for explicit inspection; never overwrite it
        # on retry. Do not disclose content or token.
        raise Blocked("lab2-private-config-write-uncertain") from None
    return target


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Private Lab2 GuestReader config, never printed.")
    choice = parser.add_mutually_exclusive_group(required=True)
    choice.add_argument("--preflight", action="store_true")
    choice.add_argument("--create", action="store_true")
    parser.add_argument("--output", type=Path, default=PRIVATE_ROOT / DEFAULT_NAME)
    args = parser.parse_args(argv)
    try:
        target = destination(args.output)
        if args.create:
            target = create(target)
            print("created-private-config=" + str(target) + " token=[redacted]")
        else:
            print("preflight=ready output=" + str(target))
        return 0
    except Blocked as error:
        print("blocked=" + str(error))
        return 2


if __name__ == "__main__":
    sys.exit(main())
