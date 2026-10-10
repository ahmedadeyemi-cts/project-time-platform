"""Release asset binding: deterministic registry/tag fixtures, no credentials or network."""
import importlib.util
import json
import subprocess
import sys
import os
from unittest.mock import patch
from pathlib import Path
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location("binding", ROOT / "scripts/verity/verify-release-provenance.py")
binding = importlib.util.module_from_spec(spec)
spec.loader.exec_module(binding)


class BindingTests(unittest.TestCase):
    def verify(self, change=None, commit=None, digests=None, annotated=False):
        data = {"tag": "v1.2.3", "version": "1.2.3", "commit": "c"*40,
                "images": {c: "ghcr.io/owner/repo/"+c+"@sha256:"+d*64
                           for c, d in (("web", "a"), ("api", "b"))}}
        if change:
            change(data)
        calls = []
        def read(command):
            calls.append(command)
            if command[0] == "gh":
                if annotated and "/git/ref/" in command[-1]:
                    return {"object": {"type": "tag", "sha": "d"*40}}
                return {"object": {"type": "commit", "sha": commit or "c"*40}}
            component = command[4].split("/")[-1].split(":")[0]
            return {"digest": (digests or {}).get(component, "sha256:"+("a" if component=="web" else "b")*64)}
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "manifest.json"
            path.write_text(json.dumps(data))
            result = binding.verify(path, "v1.2.3", "Owner/Repo", read)
        return result, calls

    def test_matching_lightweight_and_annotated_tags(self):
        for annotated in (False, True):
            result, calls = self.verify(annotated=annotated)
            self.assertEqual(result["RELEASE_TAG"], "v1.2.3")
            self.assertEqual(sum(c[0]=="docker" for c in calls), 2)
            self.assertTrue(all(c[-1]=="{{json .Manifest}}" for c in calls if c[0]=="docker"))

    def test_tampered_manifest_digest_is_rejected(self):
        for component in ("web", "api"):
            with self.subTest(component=component), self.assertRaisesRegex(ValueError, "registry_digest_mismatch"):
                self.verify(change=lambda d: d["images"].update({component: "ghcr.io/owner/repo/"+component+"@sha256:"+"e"*64}))

    def test_retargeted_tag_is_rejected(self):
        with self.assertRaisesRegex(ValueError, "source_tag_mismatch"):
            self.verify(commit="f"*40)

    def test_missing_or_malformed_commit_is_rejected(self):
        for commit in (None, "", "main", "x"*40):
            with self.subTest(commit=commit), self.assertRaisesRegex(ValueError, "source_commit_missing"):
                self.verify(change=lambda d: d.update(commit=commit))

    def test_foreign_namespace_is_rejected(self):
        with self.assertRaisesRegex(ValueError, "foreign_image_namespace"):
            self.verify(change=lambda d: d["images"].update(web="ghcr.io/attacker/repo/web@sha256:"+"a"*64))

    def test_posture_rejects_commented_write_permission(self):
        workflow = ROOT / ".github/workflows/security-negative-commented-write.yml"
        self.assertFalse(workflow.exists())
        try:
            workflow.write_text("name: Negative fixture\non: pull_request\npermissions:\n  contents: write # permission must still be rejected\njobs: {}\n")
            result = subprocess.run([sys.executable, str(ROOT / "scripts/security/validate-repository-security-posture.py")],
                                    capture_output=True, text=True)
            self.assertNotEqual(result.returncode, 0)
            self.assertIn("security-negative-commented-write.yml: unapproved contents: write permission",
                          result.stdout + result.stderr)
        finally:
            workflow.unlink(missing_ok=True)

    def test_posture_rejects_mutable_release_action_reference(self):
        workflow = ROOT / ".github/workflows/release.yml"
        original = workflow.read_bytes()
        try:
            text = original.decode()
            import re
            text, count = re.subn(r"(uses: actions/checkout)@[0-9a-f]{40}", r"\1@v5", text, count=1)
            self.assertEqual(count, 1)
            workflow.write_text(text)
            result = subprocess.run([sys.executable, str(ROOT / "scripts/security/validate-repository-security-posture.py")],
                                    capture_output=True, text=True)
            self.assertNotEqual(result.returncode, 0)
            self.assertRegex(result.stdout + result.stderr,
                             r"release.yml:[0-9]+: action reference is not pinned to a full commit SHA")
        finally:
            workflow.write_bytes(original)

    def test_registry_authentication_is_private_and_removed_after_failure(self):
        original = "/test-only-existing-docker-store"
        seen = []
        def run(command, **options):
            self.assertEqual(command, ["docker", "login", "ghcr.io", "--username", "fixture-user", "--password-stdin"])
            self.assertEqual(options["input"], "fixture-token-only-invalid\n")
            self.assertNotIn("fixture-token-only-invalid", repr(command))
            directory = Path(os.environ["DOCKER_CONFIG"])
            self.assertEqual(directory.stat().st_mode & 0o777, 0o700)
            (directory / "config.json").write_text("fixture-only-private-credential")
            seen.append(directory)
            return subprocess.CompletedProcess(command, 0, stdout="", stderr="")
        with patch.dict(os.environ, {"GHCR_READ_TOKEN":"fixture-token-only-invalid",
                                     "GHCR_READ_USERNAME":"fixture-user", "DOCKER_CONFIG":original}):
            with patch.object(binding.subprocess, "run", side_effect=run):
                with self.assertRaisesRegex(ValueError, "test-only verification failure"):
                    with binding.authenticated_registry():
                        self.assertNotEqual(os.environ["DOCKER_CONFIG"], original)
                        raise ValueError("test-only verification failure")
            self.assertEqual(os.environ["DOCKER_CONFIG"], original)
        self.assertTrue(seen)
        self.assertTrue(all(not directory.exists() for directory in seen))

    def test_registry_login_failure_cleans_up_and_preserves_existing_store(self):
        seen = []
        def fail(command, **options):
            seen.append(Path(os.environ["DOCKER_CONFIG"]))
            raise subprocess.CalledProcessError(1, command, stderr="test-only authorization denied")
        with patch.dict(os.environ, {"GHCR_READ_TOKEN":"fixture-token-only-invalid",
                                     "GHCR_READ_USERNAME":"fixture-user", "DOCKER_CONFIG":"/test-only-store"}):
            with patch.object(binding.subprocess, "run", side_effect=fail):
                with self.assertRaises(subprocess.CalledProcessError):
                    with binding.authenticated_registry():
                        self.fail("Failed login cannot permit registry inspection")
            self.assertEqual(os.environ["DOCKER_CONFIG"], "/test-only-store")
        self.assertTrue(all(not directory.exists() for directory in seen))

    def test_unavailable_registry_fails_closed(self):
        def read(command):
            if command[0]=="gh":
                return {"object":{"type":"commit","sha":"c"*40}}
            raise OSError("offline")
        with tempfile.TemporaryDirectory() as directory:
            p=Path(directory)/"manifest.json"
            p.write_text(json.dumps({"tag":"v1.2.3","version":"1.2.3","commit":"c"*40,
                                     "images":{c:"ghcr.io/owner/repo/"+c+"@sha256:"+d*64 for c,d in (("web","a"),("api","b"))}}))
            with self.assertRaises(OSError):
                binding.verify(p,"v1.2.3","Owner/Repo",read)


if __name__ == "__main__":
    unittest.main()
