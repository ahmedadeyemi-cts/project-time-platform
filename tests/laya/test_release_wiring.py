"""Offline regression tests: closed incremental deployment and schema wiring."""
import importlib.util
from pathlib import Path
import subprocess
import unittest

ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location('incremental', ROOT/'deployment/laya/incremental-policy.py')
policy = importlib.util.module_from_spec(spec)
spec.loader.exec_module(policy)

class WiringTests(unittest.TestCase):
    def test_only_reviewed_adapter_files_take_fast_path(self):
        self.assertTrue(policy.eligible({'deploy.sh','gateway/laya_decisions.py','gateway/wsgi_decisions.py'}))
        self.assertTrue(policy.eligible({'gateway/laya_decisions.py'}))
        self.assertFalse(policy.eligible(set()))
        for extra in ('release.json','gateway/wsgi.py','gateway/gateway.py','caddy/Caddyfile','systemd/ollama.service'):
            self.assertFalse(policy.eligible({'gateway/laya_decisions.py',extra}))

    def test_existing_runtime_lock_precedes_incremental_cutover(self):
        text=(ROOT/'deployment/oracle-celar/deploy.sh').read_text()
        self.assertLess(text.index('flock -w "$LOCK_WAIT_SECONDS" 8'),text.index('incremental-policy.py'))
        self.assertLess(text.index('incremental-policy.py'),text.index('apt-get update'))
        self.assertIn('[[ "$LAYA_INCREMENTAL_STATUS" == 10 ]]',text)
        self.assertIn('bash "$ROOT/../laya/deploy-gateway.sh" apply',text)

    def test_schema_is_mandatory_in_existing_owned_migration_job(self):
        text=(ROOT/'scripts/release-test/build-and-run-module025-retention-migration-106.sh').read_text()
        for name in ('laya-schema.sql','laya-grants.sql','laya-verify.sql','laya-runtime-role'):
            self.assertGreaterEqual(text.count(name),4)
        self.assertIn('select(.name == "PTP_DB_USER")',text)
        self.assertIn('sha256sum --check --status SHA256SUMS',text)
        self.assertIn('LAYA_DATABASE_SCHEMA_AND_API_GRANTS=APPLIED_AND_VERIFIED',text)
        self.assertIn('run-migration-job.sh',text)
        self.assertIn('laya-database-migration.json',text)
        for original in ('migration-106.sql','migration-120.sql','verified="$(psql','[[ "$verified" == true ]]'):
            self.assertIn(original,text)

    def test_shell_syntax(self):
        for name in ('deployment/oracle-celar/deploy.sh','deployment/laya/deploy-gateway.sh',
                     'scripts/release-test/build-and-run-module025-retention-migration-106.sh'):
            subprocess.run(['bash','-n',str(ROOT/name)],check=True)

if __name__=='__main__': unittest.main()
