"""Validate the restoration payload without GitHub or deployment mutations."""
import json
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[1]


class ProtectionRestorationTests(unittest.TestCase):
    def test_branch_controls(self):
        config = json.loads((ROOT / 'docs/security/github-protection-restoration-20261010.json').read_text())
        self.assertEqual(config['repository'], 'ahmedadeyemi-cts/project-time-platform')
        self.assertEqual(config['branch'], 'main')
        branch = config['branchProtection']
        self.assertTrue(branch['enforce_admins'])
        self.assertTrue(branch['required_status_checks']['strict'])
        self.assertEqual(branch['required_status_checks']['contexts'], ['validate', 'repository-security-posture'])
        reviews = branch['required_pull_request_reviews']
        self.assertTrue(reviews['dismiss_stale_reviews'])
        self.assertTrue(reviews['require_code_owner_reviews'])
        self.assertTrue(reviews['require_last_push_approval'])
        self.assertEqual(reviews['required_approving_review_count'], 1)
        self.assertTrue(branch['required_conversation_resolution'])
        self.assertFalse(branch['allow_force_pushes'])
        self.assertFalse(branch['allow_deletions'])

    def test_existing_test_reviewer_and_no_bypass(self):
        config = json.loads((ROOT / 'docs/security/github-protection-restoration-20261010.json').read_text())
        expected = json.loads((ROOT / '.github/flowhive-psa-protected-cutover.json').read_text())['environment']
        environment = config['testEnvironment']
        self.assertEqual(environment['reviewers'], [{'type': 'User', 'id': expected['requiredReviewerId']}])
        self.assertEqual(environment['prevent_self_review'], expected['preventSelfReview'])
        self.assertFalse(environment['can_admins_bypass'])
        self.assertEqual(environment['deployment_branch_policy'], {'protected_branches': True, 'custom_branch_policies': False})
        self.assertNotIn('production', config)


if __name__ == '__main__':
    unittest.main()
