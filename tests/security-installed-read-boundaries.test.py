import importlib.util
import unittest
from pathlib import Path
from unittest.mock import patch

root = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location('installed', root / 'scripts/security/verify-installed-read-boundaries.py')
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


class Boundaries(unittest.TestCase):
    def test_business_write_rejected_before_network(self):
        with self.assertRaisesRegex(module.CheckError, 'write_forbidden'):
            module.request('/api/admin/users', payload={})

    def test_redirect_never_followed(self):
        self.assertIsNone(module.NoRedirect().redirect_request(None, None, 302, '', {}, 'https://example.com'))

    def test_privileged_or_wrong_role_rejected(self):
        for roles in [['ENGINEER', 'ADMINISTRATOR'], ['PROJECT_TEAM_COORDINATOR']]:
            with self.assertRaises(module.CheckError):
                module.validate_context({'roles': [{'roleCode': r} for r in roles]}, {'ENGINEER'})

    def exercise(self, denied_status):
        calls = []
        def fake(path, token='', payload=None, parse=False):
            calls.append(path)
            if path.endswith('/login'):
                return 200, {'provider': 'LOCAL', 'mustChangePassword': False, 'sessionToken': 's' * 32}
            if path.endswith('/context'):
                return 200, {'roles': [{'roleCode': 'ENGINEER'}]}
            if path.endswith('/logout'):
                return 200, None
            retired = any(path.lower().rstrip('/') == p for p in module.RETIRED_READS)
            return 410 if retired else denied_status, None
        report = {'checks': []}
        with patch.object(module, 'request', side_effect=fake):
            module.exercise_account('engineer', 'unused', {'ENGINEER'}, 'unused', report)
        self.assertEqual(calls[-1], '/api/auth/session/logout')
        self.assertEqual(len(report['checks']), 39)
        self.assertNotIn('s' * 32, str(report))
        return report

    def test_valid_denial_matrix_and_session_cleanup(self):
        self.assertTrue(all(c['passed'] for c in self.exercise(403)['checks']))

    def test_expired_session_not_authorization_pass(self):
        self.assertFalse(all(c['passed'] for c in self.exercise(401)['checks']))

    def test_html_or_missing_route_not_authorization_pass(self):
        for code in (200, 302, 404, 500):
            self.assertFalse(all(c['passed'] for c in self.exercise(code)['checks']))

    def test_cleanup_after_context_failure(self):
        calls = []
        def fake(path, *args, **kwargs):
            calls.append(path)
            if path.endswith('/login'):
                return 200, {'provider': 'LOCAL', 'mustChangePassword': False, 'sessionToken': 's' * 32}
            return (200, None) if path.endswith('/logout') else (401, None)
        with patch.object(module, 'request', side_effect=fake):
            with self.assertRaisesRegex(module.CheckError, 'session_context_failed'):
                module.exercise_account('engineer', 'unused', {'ENGINEER'}, 'unused', {'checks': []})
        self.assertEqual(calls[-1], '/api/auth/session/logout')


if __name__ == '__main__':
    unittest.main()
