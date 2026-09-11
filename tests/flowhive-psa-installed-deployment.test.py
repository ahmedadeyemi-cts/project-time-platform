import importlib.util
import io
import json
from pathlib import Path
import tempfile
import unittest
import zipfile
from unittest.mock import patch


ROOT = Path(__file__).parents[1]
SPEC = importlib.util.spec_from_file_location(
    "resolver", ROOT / "scripts/release-test/resolve-flowhive-installed-deployment.py")
resolver = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(resolver)


class InstalledDeploymentBinding(unittest.TestCase):
    def archive(self, identity):
        output = io.BytesIO()
        with zipfile.ZipFile(output, "w") as bundle:
            bundle.writestr("systemwide/deployment-identity.json", json.dumps(identity))
        return output.getvalue()

    def run_resolver(self, run, identity, artifacts=None):
        with tempfile.TemporaryDirectory() as directory:
            output = Path(directory) / "context.json"
            responses = {
                "/repos/test/repo/actions/runs/123": run,
                "/repos/test/repo/actions/runs/123/artifacts?per_page=100": {
                    "artifacts": artifacts or [{"id": 77, "name": "systemwide-enterprise-reliability-test-evidence-123-1", "expired": False}]
                },
            }
            with patch.object(resolver, "api_json", side_effect=lambda path, token: responses[path]), \
                 patch.object(resolver, "api_bytes", return_value=self.archive(identity)), \
                 patch.dict(resolver.os.environ, {
                     "OUTPUT_CONTEXT": str(output), "GH_TOKEN": "offline", "GITHUB_REPOSITORY": "test/repo",
                     "GITHUB_SHA": "a" * 40, "DEPLOYMENT_RUN_ID": "123",
                 }, clear=False), patch.object(resolver.sys, "argv", ["resolver"]):
                result = resolver.main()
            return result, json.loads(output.read_text()) if output.exists() else None

    def identity(self):
        return {
            "environment": "test", "applicationSha": "95abbb0aa2445a33fda68e9de542f9446c3e2204",
            "deploymentRunId": "123", "deploymentAttempt": "1", "controllerSha": "a" * 40,
            "apiRevision": "api-revision", "webRevision": "web-revision",
            "apiImage": "registry/api@sha256:" + "1" * 64,
            "webImage": "registry/web@sha256:" + "2" * 64, "productionMutation": False,
        }

    def make_run(self, **overrides):
        value = {
            "workflow_id": 315562561, "event": "workflow_dispatch", "head_branch": "main",
            "head_sha": "a" * 40, "status": "completed", "conclusion": "success",
        }
        value.update(overrides)
        return value

    def test_server_confirmed_run_and_candidate_manifest_are_required(self):
        result, context = self.run_resolver(self.make_run(), self.identity())
        self.assertEqual(result, 0)
        self.assertEqual(context["applicationSha"], "95abbb0aa2445a33fda68e9de542f9446c3e2204")
        self.assertEqual(context["deploymentRunId"], "123")

    def test_wrong_workflow_or_event_and_unimmutable_images_are_rejected(self):
        for mutation in ({"workflow_id": 123}, {"event": "push"}, {"head_sha": "b" * 40}, {"status": "queued"}):
            result, context = self.run_resolver(self.make_run(**mutation), self.identity())
            self.assertNotEqual(result, 0)
            self.assertIsNone(context)
        bad = self.identity()
        bad["apiImage"] = "registry/api:latest"
        result, context = self.run_resolver(self.make_run(), bad)
        self.assertNotEqual(result, 0)
        self.assertIsNone(context)

    def test_artifact_is_single_use_and_ambiguous_artifact_is_rejected(self):
        duplicate = [
            {"id": 77, "name": "systemwide-enterprise-reliability-test-evidence-123-1", "expired": False},
            {"id": 78, "name": "systemwide-enterprise-reliability-test-evidence-123-2", "expired": False},
        ]
        result, context = self.run_resolver(self.make_run(), self.identity(), duplicate)
        self.assertNotEqual(result, 0)
        self.assertIsNone(context)


if __name__ == "__main__":
    unittest.main()
