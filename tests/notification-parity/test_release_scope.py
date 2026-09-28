"""Negative source-identity/scope cases plus immutable private-job packaging checks."""
import importlib.util
import json
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location('scope', ROOT/'tests/notification-parity/release_scope.py')
scope = importlib.util.module_from_spec(spec); spec.loader.exec_module(scope)
manifest = json.loads(scope.MANIFEST.read_text())

class ReleaseScopeTests(unittest.TestCase):
    def test_source_identity(self):
        scope.identity(scope.BASE, scope.BRANCH, 1204)
        for base, branch, number in [('0'*40,scope.BRANCH,1204),(scope.BASE,'main',1204),(scope.BASE,scope.BRANCH,1205)]:
            with self.assertRaises(RuntimeError): scope.identity(base,branch,number)
    def test_no_extra_missing_or_protected_path(self):
        scope.changed_files(manifest['files'],manifest)
        for paths in [manifest['files'][:-1],manifest['files']+['src/other.cs'],manifest['files']+['.github/workflows/projectpulse-deploy-test.yml']]:
            with self.assertRaises(RuntimeError): scope.changed_files(paths,manifest)
    def test_control_amendment_is_exact(self):
        before='one\nanchor\nlast\n'; replacements=[{'anchor':'anchor\n','replacement':'bounded\nanchor\n'}]
        scope.amended(before,'one\nbounded\nanchor\nlast\n',replacements,'fixture')
        for after in ['one\nunbounded\nanchor\nlast\n','one\nbounded\nanchor\n','anything']:
            with self.assertRaises(RuntimeError): scope.amended(before,after,replacements,'fixture')
    def test_reviewed_content_must_match(self):
        digest=scope.hashlib.sha256(b'reviewed').hexdigest()
        scope.content(b'reviewed',digest,'fixture')
        with self.assertRaises(RuntimeError): scope.content(b'changed',digest,'fixture')
    def test_private_job_packages_applies_verifies_and_receipts_after_success(self):
        source=(ROOT/'scripts/release-test/build-and-run-project-planning-document-authority-migration-job.sh').read_text()
        entry=source.split("<<'ENTRYPOINT'\n",1)[1].split('\nENTRYPOINT\n',1)[0]
        for name in ['126_module065_power_automate_teams_delivery','128_module065_email_teams_notification_parity','129_enterprise_reminder_delivery_sources']:
            self.assertIn(name,source.split("<<'ENTRYPOINT'\n",1)[0])
            self.assertIn(f'psql -X -v ON_ERROR_STOP=1 --file "$ROOT/database/migrations/{name}.sql"',entry)
        self.assertLess(entry.index('sha256sum --check --status database/module065-notification-parity.sha256'),entry.index('database/migrations/128_'))
        self.assertLess(entry.index('database/migrations/129_'),entry.index('--file "$ROOT/database/verify-module065-notification-parity.sql"'))
        self.assertGreater(source.index('# MODULE065_PARITY_EVIDENCE_BEGIN'),source.index('bash "$MIGRATION_RUNNER"'))
        self.assertIn('liveNotificationActivation:false',source)
    def test_verifier_never_activates_or_sends(self):
        sql=(ROOT/'scripts/release-test/verify-module065-notification-parity.sql').read_text()
        self.assertNotRegex(sql,r'(?i)\b(INSERT|UPDATE|DELETE|TRUNCATE)\s+(INTO|FROM|enterprise_notification|module065)')
        self.assertIn("delivery_boundary IN ('test_only','locked')",sql)
        self.assertIn('RAISE EXCEPTION',sql)
    def test_new_reminders_default_to_no_live_delivery(self):
        sql=(ROOT/'database/migrations/129_enterprise_reminder_delivery_sources.sql').read_text()
        self.assertEqual(sql.count("'enterprise-reminder-v1','scanner',FALSE)"),4)
        self.assertEqual(sql.count("'test_only'"),4)
        self.assertIn('ON CONFLICT(policy_code) DO NOTHING',sql)

if __name__ == '__main__': unittest.main()
