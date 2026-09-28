#!/usr/bin/env python3
"""Validate release metadata as data. Never evaluate a release asset as shell."""
import argparse
import json
import os
from pathlib import Path
import re
import tempfile

VERSION = re.compile(r"v?[0-9]+\.[0-9]+\.[0-9]+(?:[-+][A-Za-z0-9.-]+)?", re.ASCII)
IMAGE = re.compile(r"[a-z0-9.-]+(?::[0-9]+)?(?:/[a-z0-9._-]+)+@sha256:[0-9a-f]{64}", re.ASCII)
KEYS = ("RELEASE_TAG", "RELEASE_VERSION", "WEB_IMAGE", "API_IMAGE")


def validate(values, allow_local=False):
    if set(values) != set(KEYS):
        raise ValueError("Release configuration must contain exactly the four supported keys")
    # Offline builds use immutable local image IDs. Downloaded manifests never
    # enter this path; they must contain fully qualified registry digests.
    if allow_local and values.get("RELEASE_TAG") == "v0.0.0-local" and values.get("RELEASE_VERSION") == "0.0.0-local":
        if all(isinstance(values[key], str) and re.fullmatch(r"sha256:[0-9a-f]{64}", values[key])
               for key in ("WEB_IMAGE", "API_IMAGE")):
            return values
    for key, value in values.items():
        grammar = IMAGE if key.endswith("_IMAGE") else VERSION
        if not isinstance(value, str) or not grammar.fullmatch(value):
            raise ValueError("Invalid release field: " + key)
    if values["RELEASE_TAG"].removeprefix("v") != values["RELEASE_VERSION"].removeprefix("v"):
        raise ValueError("Release tag and version differ")
    return values


def read_env(path):
    if path.stat().st_size > 8192:
        raise ValueError("Release configuration is too large")
    values = {}
    for line in path.read_text().splitlines():
        if not line or line.startswith("#"):
            continue
        key, separator, value = line.partition("=")
        if not separator or key in values or key not in KEYS:
            raise ValueError("Invalid or duplicate release key")
        values[key] = value
    return validate(values, allow_local=True)


def read_manifest(path, tag):
    if path.stat().st_size > 8192:
        raise ValueError("Release manifest is too large")
    def unique(pairs):
        result = {}
        for key, value in pairs:
            if key in result:
                raise ValueError("Duplicate manifest key")
            result[key] = value
        return result
    data = json.loads(path.read_text(), object_pairs_hook=unique)
    if data.get("tag") != tag:
        raise ValueError("Manifest does not match the requested release tag")
    return validate(dict(zip(KEYS, (tag, data.get("version"),
        data.get("images", {}).get("web"), data.get("images", {}).get("api")))))


def render(values):
    return "".join(f"{key}={values[key]}\n" for key in KEYS)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("mode", choices=("manifest", "env"))
    parser.add_argument("source", type=Path)
    parser.add_argument("--tag")
    parser.add_argument("--output", type=Path)
    args = parser.parse_args()
    values = read_manifest(args.source, args.tag) if args.mode == "manifest" else read_env(args.source)
    content = render(values)
    if args.output:
        # A random 0600 file and atomic replacement avoid partial or symlink writes.
        fd, name = tempfile.mkstemp(prefix=".release-", dir=args.output.parent)
        try:
            with os.fdopen(fd, "w") as stream:
                stream.write(content)
            os.replace(name, args.output)
        finally:
            if os.path.exists(name):
                os.unlink(name)
    else:
        print(content, end="")


if __name__ == "__main__":
    try:
        main()
    except (ValueError, KeyError, TypeError, AttributeError, OSError) as error:
        raise SystemExit("Release configuration rejected: " + str(error))
