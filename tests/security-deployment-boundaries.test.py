"""Offline exploit regressions. No credentials, cloud access or deployment."""
import importlib.util
import json
import os
from pathlib import Path
import subprocess
import shutil
import hashlib
import re
import secrets
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
            script = ROOT / 'scripts/apply-admin-experience-008-009-test-migration.sh'
            authorized = re.search(r'EXPECTED_RELEASE_COMMIT="([0-9a-f]{40})"', script.read_text()).group(1)
            (root / '.projectpulse-release-commit').write_text(authorized)
            migrations = root / 'database/migrations'; migrations.mkdir(parents=True)
            name = '048_admin_audit_and_manager_team_scope.sql'
            content = b'-- isolated test-only migration\n'
            (migrations / name).write_bytes(content)
            (migrations / 'SHA256SUMS').write_text(hashlib.sha256(content).hexdigest() + '  ' + name + '\n')
            invoked = root / 'psql-invoked'
            stub = root / 'psql'; stub.write_text('#!/bin/sh\n: > "$SECURITY_STUB_MARKER"\nexit 99\n'); stub.chmod(0o700)
            secret = secrets.token_hex(32)
            env = {**os.environ, 'PATH': directory + ':' + os.environ['PATH'],
                   'SECURITY_STUB_MARKER': str(invoked),
                   'PROJECTPULSE_TEST_DATABASE_URL': 'postgresql://user:' + secret + '@db.invalid/test'}
            result = subprocess.run(['bash', str(ROOT / 'scripts/apply-admin-experience-008-009-test-migration.sh'), directory], env=env, capture_output=True, text=True)
            self.assertNotEqual(result.returncode, 0)
            self.assertTrue(invoked.exists(), 'The fixture must reach psql, not stop at the release guard')
            self.assertNotIn(secret, result.stdout + result.stderr)
            self.assertNotIn('::add-mask::', result.stdout + result.stderr)

    def release_fixture(self, directory):
        root = Path(directory)
        scripts = root / 'scripts/verity'; scripts.mkdir(parents=True)
        (root / '.verity').mkdir()
        for name in ('pin-digests.sh', 'release-config.py', 'validate-runtime-config.py', 'verify-release-provenance.py'):
            shutil.copyfile(ROOT / 'scripts/verity' / name, scripts / name)
        shutil.copyfile(ROOT / 'deploy.sh', root / 'deploy.sh')
        binary = root / 'bin'; binary.mkdir()
        gh = binary / 'gh'
        gh.write_text('#!/usr/bin/env python3\nimport os,sys,shutil,json\nif sys.argv[1:3] == ["release","view"]: print(os.environ["SECURITY_RELEASE_TAG"])\nelif sys.argv[1:3] == ["repo","view"]: print("Owner/Repo")\nelif sys.argv[1] == "api": print(json.dumps({"object":{"type":"commit","sha":os.environ.get("SECURITY_TAG_SOURCE","c"*40)}}))\nelif sys.argv[1:3] == ["release","download"]:\n shutil.copyfile(os.environ["SECURITY_RELEASE_MANIFEST"],os.path.join(sys.argv[sys.argv.index("--dir")+1],"release-digests.json"))\nelse: sys.exit(99)\n')
        gh.chmod(0o700)
        docker = binary / 'docker'
        docker.write_text('#!/usr/bin/env python3\nimport sys,json\ncomponent=sys.argv[4].split("/")[-1].split(":")[0]\nprint(json.dumps({"digest":"sha256:"+("a" if component=="web" else "b")*64}))\n')
        docker.chmod(0o700)
        return root, {**os.environ, 'PATH': str(binary)+':'+os.environ['PATH'],
                      'SECURITY_RELEASE_TAG': 'v1.2.3',
                      'SECURITY_RELEASE_MANIFEST': str(root / 'manifest.json')}

    def test_actual_pinning_script_rejects_hostile_release_metadata_without_execution(self):
        with tempfile.TemporaryDirectory() as directory:
            root, env = self.release_fixture(directory)
            manifest = dict(tag='v1.2.3', version='1.2.3', commit='c'*40,
                            images=dict(web='ghcr.io/owner/repo/web@sha256:'+'a'*64,
                                        api='ghcr.io/owner/repo/api@sha256:'+'b'*64))
            source = root / 'manifest.json'
            source.write_text(json.dumps(manifest))
            command = ['bash', str(root/'scripts/verity/pin-digests.sh')]
            valid = subprocess.run(command, env=env, capture_output=True, text=True)
            self.assertEqual(valid.returncode, 0, valid.stderr)
            output = root / '.verity/release.env'
            original = output.read_bytes()
            self.assertEqual(output.stat().st_mode & 0o777, 0o600)
            marker = root / 'executed'
            payloads = [f'$(touch {marker})', f'v1.2.3; touch {marker}; #',
                        f'v1.2.3|touch {marker}', 'v1.2.3\nEXTRA=1', '{a,b}']
            for field in ('tag', 'version', 'web', 'api'):
                for payload in payloads:
                    with self.subTest(field=field, payload=payload):
                        value = json.loads(json.dumps(manifest))
                        if field in ('web', 'api'): value['images'][field] = payload
                        else: value[field] = payload
                        source.write_text(json.dumps(value))
                        bad_env = {**env, 'SECURITY_RELEASE_TAG': payload if field == 'tag' else 'v1.2.3'}
                        result = subprocess.run(command, env=bad_env, capture_output=True, text=True)
                        self.assertNotEqual(result.returncode, 0)
                        self.assertFalse(marker.exists())
                        self.assertEqual(output.read_bytes(), original)
            print('RELEASE_METADATA_EXPLOITS=PASS cases=20')

    def test_actual_pinning_preserves_existing_pin_for_tampered_provenance(self):
        with tempfile.TemporaryDirectory() as directory:
            root, env = self.release_fixture(directory)
            data = dict(tag='v1.2.3', version='1.2.3', commit='c'*40,
                        images=dict(web='ghcr.io/owner/repo/web@sha256:'+'a'*64,
                                    api='ghcr.io/owner/repo/api@sha256:'+'b'*64))
            manifest = root / 'manifest.json'
            manifest.write_text(json.dumps(data))
            command = ['bash', str(root/'scripts/verity/pin-digests.sh')]
            valid = subprocess.run(command, env=env, capture_output=True, text=True)
            self.assertEqual(valid.returncode, 0, valid.stderr)
            output = root / '.verity/release.env'
            original = output.read_bytes()
            for field in ('commit', 'web', 'api'):
                changed = json.loads(json.dumps(data))
                if field == 'commit': changed['commit'] = 'f'*40
                else: changed['images'][field] = 'ghcr.io/owner/repo/'+field+'@sha256:'+'e'*64
                manifest.write_text(json.dumps(changed))
                bad = subprocess.run(command, env=env, capture_output=True, text=True)
                self.assertNotEqual(bad.returncode, 0)
                self.assertEqual(output.read_bytes(), original)
                self.assertIn('provenance verification failed', bad.stderr)
            print('RELEASE_PIN_PROVENANCE_TAMPERING=PASS cases=3')

    def test_actual_deployer_refuses_executable_release_file_before_any_docker_call(self):
        with tempfile.TemporaryDirectory() as directory:
            root, env = self.release_fixture(directory)
            docker = root / 'bin/docker'
            docker.write_text('#!/bin/sh\n: > "$SECURITY_DOCKER_MARKER"\nexit 99\n')
            docker.chmod(0o700)
            invoked = root / 'docker-invoked'
            marker = root / 'executed'
            # Valid operator config ensures the downloaded release file is the guard under test.
            (root / '.verity/deploy.env').write_text('RUNTIME_DB_PASSWORD='+secrets.token_hex(32)+'\nPOSTGRES_DB=ProjectPulse\nPOSTGRES_USER=projectpulse\n')
            for payload in (f'$(touch {marker})', f'v1.2.3; touch {marker}; #', 'v1.2.3\nPATH=/tmp'):
                value = self.values(); value['RELEASE_TAG'] = payload
                (root / '.verity/release.env').write_text(release.render(value))
                result = subprocess.run(['bash', str(root/'deploy.sh')],
                                        env={**env, 'SECURITY_DOCKER_MARKER': str(invoked)},
                                        capture_output=True, text=True)
                self.assertNotEqual(result.returncode, 0)
                self.assertIn('Release configuration rejected', result.stderr)
                self.assertFalse(marker.exists())
                self.assertFalse(invoked.exists())

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

    def test_incomplete_revision_checks_publish_only_summary_without_installation_receipt(self):
        for phase in evidence.INCOMPLETE_REVISION_PHASES:
            with self.subTest(phase=phase), tempfile.TemporaryDirectory() as directory:
                source = Path(directory) / 'private'; source.mkdir()
                target = Path(directory) / 'public'
                name = 'module001b-revision-reconcile.json'
                (source / name).write_text(json.dumps({
                    'phase': phase, 'expectedRevisionActive': True,
                    'expectedImage': 'SENTINEL_private_unverified_identity',
                    'productionMutation': False,
                }))
                evidence.publish(source, target)
                self.assertFalse((target / name).exists())
                summary = json.loads((target / 'security-safe-uat-summary.json').read_text())
                self.assertEqual(summary['reports'][name]['status'], 'unclassified')
                self.assertNotIn('SENTINEL', (target / 'security-safe-uat-summary.json').read_text())
                self.assertFalse(summary['rawResponsesPublished'])

    def test_unknown_or_invalid_converged_revision_receipts_still_fail_closed(self):
        for receipt in ({'phase': 'unrecognized'}, {'phase': 'converged', 'expectedImage': 'registry.invalid/api:latest'}):
            with self.subTest(receipt=receipt), tempfile.TemporaryDirectory() as directory:
                source = Path(directory) / 'private'; source.mkdir()
                target = Path(directory) / 'public'
                (source / 'module001b-revision-reconcile.json').write_text(json.dumps(receipt))
                with self.assertRaises(ValueError): evidence.publish(source, target)
                self.assertFalse(target.exists())

if __name__ == '__main__': unittest.main()
