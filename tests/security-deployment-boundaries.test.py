"""Offline exploit regressions. No credentials, cloud access or deployment."""
import importlib.util
import json
import os
from pathlib import Path
import subprocess
import tempfile
import unittest
import yaml

ROOT = Path(__file__).resolve().parents[1]

def module(name, relative):
    spec = importlib.util.spec_from_file_location(name, ROOT / relative)
    result = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(result)
    return result

release = module('release', 'scripts/verity/release-config.py')
evidence = module('evidence', 'scripts/security/publish-safe-uat-evidence.py')
resolution = module('resolution', 'tests/flowhive-installed-resolution.test.py')

class ReleaseBoundaryTests(unittest.TestCase):
    def test_release_actions_are_pinned_and_checkout_does_not_persist_credentials(self):
        data = yaml.safe_load((ROOT / '.github/workflows/release.yml').read_text())
        steps = data['jobs']['build-scan-publish']['steps']
        for step in steps:
            if 'uses' in step:
                self.assertRegex(step['uses'], r'^[^@]+@[0-9a-f]{40}$')
            if step.get('uses', '').startswith('actions/checkout@'):
                self.assertIs(step['with']['persist-credentials'], False)
        scans = [step['uses'] for step in steps if step.get('uses', '').startswith('aquasecurity/trivy-action@')]
        self.assertEqual(scans, ['aquasecurity/trivy-action@915b19bbe73b92a6cf82a1bc12b087c9a19a5fe2'] * 2)

    def values(self):
        return dict(RELEASE_TAG='v1.2.3', RELEASE_VERSION='1.2.3',
                    WEB_IMAGE='registry.invalid/web@sha256:' + 'a' * 64,
                    API_IMAGE='registry.invalid/api@sha256:' + 'b' * 64)

    def test_literal_release_roundtrip_and_private_mode(self):
        with tempfile.TemporaryDirectory() as directory:
            source, output = Path(directory) / 'manifest.json', Path(directory) / 'release.env'
            source.write_text(json.dumps(dict(tag='v1.2.3', version='1.2.3', images=dict(web=self.values()['WEB_IMAGE'], api=self.values()['API_IMAGE']))))
            subprocess.run(['python3', str(ROOT / 'scripts/verity/release-config.py'), 'manifest', str(source), '--tag', 'v1.2.3', '--output', str(output)], check=True)
            self.assertEqual(release.read_env(output), self.values())
            self.assertEqual(output.stat().st_mode & 0o777, 0o600)

    def test_every_release_field_rejects_executable_or_malformed_data(self):
        for key in self.values():
            for payload in ('$(touch /tmp/must-not-execute)', '`id`', 'value\nEVIL=1', 'value;id', 'v1.2.3\x00', '', 'registry.invalid/api:latest'):
                with self.subTest(key=key, payload=payload):
                    value = self.values(); value[key] = payload
                    with self.assertRaises(ValueError): release.validate(value)
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / 'release.env'
            for extra in ('PATH=/tmp\n', 'RELEASE_TAG=v1.2.3\n', 'touch /tmp/no\n'):
                path.write_text(release.render(self.values()) + extra)
                with self.assertRaises(ValueError): release.read_env(path)
        self.assertNotIn('. .verity/release.env', (ROOT / 'deploy.sh').read_text())

    def test_local_build_ids_are_immutable_and_not_accepted_from_release_manifests(self):
        values = dict(RELEASE_TAG='v0.0.0-local', RELEASE_VERSION='0.0.0-local', WEB_IMAGE='sha256:' + 'a' * 64, API_IMAGE='sha256:' + 'b' * 64)
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / 'release.env'; path.write_text(release.render(values))
            self.assertEqual(release.read_env(path), values)
            with self.assertRaises(ValueError): release.validate(values)
            values['API_IMAGE'] = 'projectpulse/api:local'
            path.write_text(release.render(values))
            with self.assertRaises(ValueError): release.read_env(path)

    def test_container_migration_failure_does_not_print_database_secret(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / '.projectpulse-release-commit').write_text('not-the-authorized-release')
            stub = root / 'psql'; stub.write_text('#!/bin/sh\nexit 99\n'); stub.chmod(0o700)
            secret = 'SENTINEL_percent%25_password'
            env = {**os.environ, 'PATH': directory + ':' + os.environ['PATH'],
                   'PROJECTPULSE_TEST_DATABASE_URL': 'postgresql://user:' + secret + '@db.invalid/test'}
            result = subprocess.run(['bash', str(ROOT / 'scripts/apply-admin-experience-008-009-test-migration.sh'), directory], env=env, capture_output=True, text=True)
            self.assertNotEqual(result.returncode, 0)
            self.assertNotIn(secret, result.stdout + result.stderr)
            self.assertNotIn('::add-mask::', result.stdout + result.stderr)

    def test_manifest_duplicate_and_mismatched_tag_rejected(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / 'manifest.json'
            path.write_text('{"tag":"v1.2.3","tag":"v1.2.4"}')
            with self.assertRaises(ValueError): release.read_manifest(path, 'v1.2.4')
            path.write_text('{"tag":"v1.2.4"}')
            with self.assertRaises(ValueError): release.read_manifest(path, 'v1.2.3')

    def test_dispatch_text_is_passed_by_environment_not_shell_source(self):
        count = 0
        for path in (ROOT / '.github/workflows').glob('*.yml'):
            data = yaml.safe_load(path.read_text())
            for job in data.get('jobs', {}).values():
                for step in job.get('steps', []):
                    script = step.get('run', '')
                    # Cover the two free-text fields used by all affected lanes.
                    self.assertNotRegex(script, r'\$\{\{\s*inputs\.(confirmation|release_commit)\b', str(path))
                    for key in ('DISPATCH_CONFIRMATION', 'DISPATCH_RELEASE_COMMIT'):
                        if '$' + key in script:
                            count += 1
                            self.assertIn(key, step.get('env', {}), str(path))
        self.assertGreaterEqual(count, 60)
        with tempfile.TemporaryDirectory() as directory:
            marker = Path(directory) / 'executed'
            for payload in (f"'; touch {marker}; #", f'$(touch {marker})', f'`touch {marker}`', 'approved\ntrue'):
                result = subprocess.run(['bash', '-eu', '-c', '[[ "$DISPATCH_CONFIRMATION" == "approved" ]]'], env={**os.environ, 'DISPATCH_CONFIRMATION': payload}, capture_output=True)
                self.assertNotEqual(result.returncode, 0)
                self.assertFalse(marker.exists())

class BackupBoundaryTests(unittest.TestCase):
    def script(self):
        data = (ROOT / 'ops/projectpulse/scripts/projectpulse-backup.sh').read_text()
        return data[data.index('load_backup_data() {'):data.index('SFTP_UPLOAD_STATUS=')]

    def run_config(self, path, directory):
        config = Path(directory) / 'config'
        config.write_text("PROJECTPULSE_BACKUP_SFTP_HOST='backup.example.invalid'\nPROJECTPULSE_BACKUP_SFTP_USER='backup'\nPROJECTPULSE_BACKUP_SFTP_REMOTE_PATH=" + path + '\n')
        return subprocess.run(['bash', '-eu', '-c', self.script() + '\nload_backup_data "$1" SFTP\nsftp_argument "$PROJECTPULSE_BACKUP_SFTP_REMOTE_PATH"', 'test', str(config)], capture_output=True, text=True)

    def test_batch_paths_cannot_add_shell_commands(self):
        with tempfile.TemporaryDirectory() as directory:
            for path in ("'/\n!id'", "'/\r!id'", "'/../etc'", "'!id'", "'/ok'\nEVIL='yes'"):
                self.assertNotEqual(self.run_config(path, directory).returncode, 0)
            valid = self.run_config("'/Project backups/September'", directory)
            self.assertEqual(valid.returncode, 0, valid.stderr)
            self.assertEqual(valid.stdout, '"/Project backups/September"')

    def test_literal_substitution_is_never_executed(self):
        with tempfile.TemporaryDirectory() as directory:
            marker = Path(directory) / 'executed'
            result = self.run_config("'/$(touch " + str(marker) + ")'", directory)
            self.assertEqual(result.returncode, 0, result.stderr)
            self.assertFalse(marker.exists())
            self.assertIn('$(touch ', result.stdout)
            result = self.run_config("'/quoted \"folder\"'", directory)
            self.assertEqual(result.stdout, '"/quoted \\"folder\\""')

class EvidenceBoundaryTests(unittest.TestCase):
    def test_raw_customer_data_and_screenshots_never_published(self):
        with tempfile.TemporaryDirectory() as directory:
            source = Path(directory) / 'private'; source.mkdir()
            target = Path(directory) / 'public'
            secret = 'SENTINEL_customer_financial_email_token'
            (source / 'uat-summary.json').write_text(json.dumps({'status':'passed','productionMutation':False,'customer':secret,'financial':12345,'rows':[secret]}))
            (source / 'unexpected-response.json').write_text(secret)
            (source / 'my-role-failure.png').write_text(secret)
            evidence.publish(source, target)
            self.assertEqual({p.name for p in target.iterdir()}, {'security-safe-uat-summary.json'})
            data = (target / 'security-safe-uat-summary.json').read_text()
            self.assertNotIn(secret, data)
            self.assertNotIn('12345', data)
            self.assertEqual(json.loads(data)['reports']['uat-summary.json']['status'], 'passed')
            self.assertEqual(target.stat().st_mode & 0o777, 0o700)

    def test_preexisting_output_and_symlink_inputs_fail_closed(self):
        with tempfile.TemporaryDirectory() as directory:
            source = Path(directory) / 'private'; source.mkdir()
            target = Path(directory) / 'public'; target.mkdir()
            with self.assertRaises(ValueError): evidence.publish(source, target)
            target.rmdir()
            (source / 'uat-summary.json').symlink_to(ROOT / 'package.json')
            # A dangling link must also be rejected, not silently ignored.
            with self.assertRaises(ValueError): evidence.publish(source, target)
            self.assertFalse(target.exists())

    def test_projected_receipts_preserve_real_installed_release_verification(self):
        for factory in (resolution.case, resolution.standard_main_case):
            data = factory(); run, jobs, manifest, receipts = data
            with tempfile.TemporaryDirectory() as directory:
                source = Path(directory) / 'private'; source.mkdir()
                target = Path(directory) / 'public'
                for name, receipt in receipts.items():
                    (source / name).write_text(json.dumps({**receipt, 'privateResponse': 'SENTINEL'}))
                evidence.publish(source, target)
                projected = {name: json.loads((target / name).read_text()) for name in receipts}
                result = resolution.validate((run, jobs, manifest, projected))
                self.assertTrue(result['installationVerified'])
                self.assertFalse(result['functionalAcceptanceVerified'])
                self.assertNotIn('SENTINEL', ''.join(p.read_text() for p in target.iterdir()))
                projected['deployment-identity.json']['apiImage'] = 'registry.invalid/api:latest'
                with self.assertRaises(resolution.resolver.ResolutionError):
                    resolution.validate((run, jobs, manifest, projected))

    def test_invalid_receipt_identity_never_reaches_upload_directory(self):
        with tempfile.TemporaryDirectory() as directory:
            source = Path(directory) / 'private'; source.mkdir()
            target = Path(directory) / 'public'
            (source / 'deployment-identity.json').write_text(json.dumps({'apiImage':'customer@example.invalid'}))
            with self.assertRaises(ValueError): evidence.publish(source, target)
            self.assertFalse(target.exists())

if __name__ == '__main__': unittest.main()
