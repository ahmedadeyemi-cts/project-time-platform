#!/usr/bin/env python3
"""Bind a release asset to its Git tag and independently observed registry digests."""
import argparse
import importlib.util
import json
from pathlib import Path
import re
import subprocess

spec = importlib.util.spec_from_file_location("release_config", Path(__file__).with_name("release-config.py"))
release = importlib.util.module_from_spec(spec)
spec.loader.exec_module(release)


def require(value, code):
    if not value:
        raise ValueError(code)


def query(arguments):
    result = subprocess.run(arguments, capture_output=True, text=True, timeout=30, check=True)
    require(len(result.stdout) <= 65536, "provenance_response_too_large")
    return json.loads(result.stdout)


def verify(path, tag, repository, read=query):
    require(re.fullmatch(r"[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+", repository) is not None,
            "invalid_repository")
    values = release.read_manifest(path, tag)
    data = json.loads(path.read_text())
    commit = data.get("commit")
    require(isinstance(commit, str) and re.fullmatch(r"[0-9a-f]{40}", commit), "source_commit_missing")
    obj = read(["gh", "api", "repos/" + repository + "/git/ref/tags/" + tag])["object"]
    for _ in range(5):
        require(isinstance(obj, dict) and re.fullmatch(r"[0-9a-f]{40}", str(obj.get("sha", ""))),
                "invalid_tag_object")
        if obj.get("type") == "commit":
            break
        require(obj.get("type") == "tag", "invalid_tag_type")
        obj = read(["gh", "api", "repos/" + repository + "/git/tags/" + obj["sha"]])["object"]
    require(obj.get("type") == "commit" and obj.get("sha") == commit, "source_tag_mismatch")
    for component in ("web", "api"):
        image, digest = values[component.upper() + "_IMAGE"].split("@")
        require(image == "ghcr.io/" + repository.lower() + "/" + component, "foreign_image_namespace")
        observed = read(["docker", "buildx", "imagetools", "inspect", image + ":" + tag,
                         "--format", "{{json .Manifest}}"])
        require(isinstance(observed, dict) and observed.get("digest") == digest,
                "registry_digest_mismatch")
    return values


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("source", type=Path)
    parser.add_argument("--tag", required=True)
    parser.add_argument("--repository", required=True)
    args = parser.parse_args()
    verify(args.source, args.tag, args.repository)
    print("RELEASE_MANIFEST_BINDING=PASS")


if __name__ == "__main__":
    try:
        main()
    except (ValueError, KeyError, TypeError, AttributeError, OSError,
            subprocess.SubprocessError):
        raise SystemExit("Release provenance verification failed; existing release pin was preserved.")
