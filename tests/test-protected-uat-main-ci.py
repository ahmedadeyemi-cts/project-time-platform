"""The auto-release gate must reject failed, stale and missing evidence."""
import importlib.util
from pathlib import Path
import unittest
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location("gate", ROOT / "scripts/release-test/wait-protected-uat-main-ci.py")
gate = importlib.util.module_from_spec(spec)
spec.loader.exec_module(gate)
SHA = "a" * 40


def runs():
    return [dict(id=i, name=name, event="push", head_branch="main", head_sha=SHA,
                 status="completed", conclusion="success") for i, name in enumerate(gate.REQUIRED, 1)]


class GateTests(unittest.TestCase):
    def test_exact_success(self):
        self.assertEqual(gate.assess(runs(), SHA), [])

    def test_missing_or_unfinished_never_passes(self):
        for status in ("queued", "in_progress", "waiting"):
            data = runs(); data[0]["status"] = status
            self.assertEqual(gate.assess(data, SHA), [gate.REQUIRED[0]])
        self.assertEqual(gate.assess([], SHA), list(gate.REQUIRED))

    def test_stale_other_branch_or_other_event_cannot_satisfy_gate(self):
        for key, value in (("head_sha", "b" * 40), ("head_branch", "feature"), ("event", "pull_request")):
            data = runs(); data[0][key] = value
            self.assertEqual(gate.assess(data, SHA), [gate.REQUIRED[0]])

    def test_failed_latest_run_blocks_even_with_older_success(self):
        for result in ("failure", "cancelled", "skipped", "timed_out", None):
            data = runs(); data.append(data[0] | dict(id=100, conclusion=result))
            with self.assertRaises(RuntimeError): gate.assess(data, SHA)

    def test_main_drift_stops_without_dispatch(self):
        with patch.dict(gate.os.environ, {"GITHUB_SHA": SHA, "GITHUB_REPOSITORY": gate.REPOSITORY,
                                        "GITHUB_REF": "refs/heads/main"}, clear=True), \
                patch.object(gate, "read", return_value={"object": {"sha": "b" * 40}}) as api:
            with self.assertRaisesRegex(RuntimeError, "Main moved"): gate.main()
            api.assert_called_once_with("git/ref/heads/main")


if __name__ == "__main__":
    unittest.main()
