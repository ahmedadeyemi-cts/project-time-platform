import importlib.util
import unittest
from pathlib import Path
from unittest.mock import patch, MagicMock
from io import BytesIO

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
        self.assertEqual(len(report['checks']), 45)
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

    def test_large_finance_summary_uses_bounded_prefix(self):
        response = MagicMock()
        response.status = 200
        response.headers = {"Content-Type": "application/json"}
        stream = BytesIO(b'{"invoices":[' + b' ' * 200000 + b']}')
        response.read.side_effect = stream.read
        opener = MagicMock()
        opener.open.return_value.__enter__.return_value = response
        with patch.object(module, "build_opener", return_value=opener):
            status, body = module.request("/api/invoicing/summary", "session", parse="container")
        self.assertEqual(status, 200)
        self.assertEqual(body, {})
        response.read.assert_called_once_with(1024)
        self.assertEqual(stream.tell(), 1024)

    def test_finance_success_cannot_be_html(self):
        for content_type, prefix in [("text/html", b"<html>"), ("application/json", b"<html>"),
                                     ("application/json", b"null")]:
            response = MagicMock()
            response.status = 200
            response.headers = {"Content-Type": content_type}
            response.read.return_value = prefix
            opener = MagicMock()
            opener.open.return_value.__enter__.return_value = response
            with patch.object(module, "build_opener", return_value=opener):
                with self.assertRaises(module.CheckError):
                    module.request("/api/invoicing/summary", "session", parse="container")

    def test_accounting_positive_control_keeps_response_content_private(self):
        calls = []
        def fake(path, token='', payload=None, parse=False):
            calls.append(path)
            if path.endswith('/login'):
                return 200, {'provider': 'LOCAL', 'mustChangePassword': False, 'sessionToken': 's'*32}
            if path.endswith('/context'):
                return 200, {'roles': [{'roleCode': 'ACCOUNTING'}]}
            if path.endswith('/logout'): return 200, None
            normalized = path.lower().rstrip('/')
            if normalized in module.FINANCE_READS:
                self.assertTrue(parse)
                return 200, {'test_only_private_content': 'not-for-evidence'}
            return (410 if normalized in module.RETIRED_READS else 403), None
        report = {'checks': []}
        with patch.object(module, 'request', side_effect=fake):
            module.exercise_account('accounting', 'unused', {'ACCOUNTING'}, 'unused', report)
        self.assertEqual(len(report['checks']), 45)
        self.assertEqual(sum(c['expected']==200 for c in report['checks']), 6)
        self.assertTrue(all(c['passed'] for c in report['checks']))
        self.assertNotIn('not-for-evidence', str(report))
        self.assertEqual(calls[-1], '/api/auth/session/logout')


if __name__ == '__main__':
    unittest.main()
