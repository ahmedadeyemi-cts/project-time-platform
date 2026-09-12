"""Behavioral tests for server-confirmed installed-release resolution."""
from __future__ import annotations

import importlib.util
from pathlib import Path
import unittest


ROOT = Path(__file__).parents[1]
SPEC = importlib.util.spec_from_file_location(
    "resolve_flowhive_installed_deployment",
    ROOT / "scripts/release-test/resolve-flowhive-installed-deployment.py",
)
assert SPEC and SPEC.loader
RESOLVER = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(RESOLVER)


class InstalledResolutionTests(unittest.TestCase):
    def test_successful_run_remains_accepted(self):
        self.assertEqual(
            RESOLVER.deployment_disposition(
                {"status": "completed", "conclusion": "success"}, {}, None
            ),
            "successful",
        )

    def test_failed_run_is_accepted_only_after_sealed_identity_and_health(self):
        identity = {"status": "identity_inputs_sealed", "applicationSha": "a" * 40}
        health = {
            "deploymentHealthVerified": True,
            "productionMutation": False,
            "sourceCommit": "a" * 40,
        }
        self.assertEqual(
            RESOLVER.deployment_disposition(
                {"status": "completed", "conclusion": "failure"}, identity, health
            ),
            "failed_after_identity_sealed",
        )

    def test_failed_run_before_identity_is_not_an_installed_release(self):
        with self.assertRaises(RESOLVER.ResolutionError) as error:
            RESOLVER.deployment_disposition(
                {"status": "completed", "conclusion": "failure"},
                {"status": "identity_inputs_pending", "applicationSha": "a" * 40},
                {"deploymentHealthVerified": True, "productionMutation": False, "sourceCommit": "a" * 40},
            )
        self.assertEqual(str(error.exception), "failed_before_identity_sealed")

    def test_failed_run_without_health_or_matching_source_is_rejected(self):
        identity = {"status": "identity_inputs_sealed", "applicationSha": "a" * 40}
        with self.assertRaises(RESOLVER.ResolutionError) as no_health:
            RESOLVER.deployment_disposition(
                {"status": "completed", "conclusion": "failure"}, identity, None
            )
        self.assertEqual(str(no_health.exception), "failed_without_deployment_health")
        with self.assertRaises(RESOLVER.ResolutionError) as mismatch:
            RESOLVER.deployment_disposition(
                {"status": "completed", "conclusion": "failure"},
                identity,
                {"deploymentHealthVerified": True, "productionMutation": False, "sourceCommit": "b" * 40},
            )
        self.assertEqual(str(mismatch.exception), "failed_deployment_health_source_mismatch")

    def test_nonterminal_run_is_never_accepted(self):
        with self.assertRaises(RESOLVER.ResolutionError) as error:
            RESOLVER.deployment_disposition({"status": "in_progress", "conclusion": None}, {}, None)
        self.assertEqual(str(error.exception), "selected_run_not_terminal")


if __name__ == "__main__":
    unittest.main()
