"""Exercise the actual managed-adapter verifier and failed-cutover restoration."""
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
SOURCE = (ROOT/'deployment/laya/deploy-gateway.sh').read_text()
BLOCK = SOURCE.split("<<'LAYA_MANAGED'\n", 1)[1].split('\nLAYA_MANAGED\n', 1)[0]
namespace = {'__name__': 'managed_adapter_fixture'}
exec(compile(BLOCK, 'actual-managed-adapter-verifier', 'exec'), namespace)
VERIFY = namespace['verify_managed']
NAMES = namespace['NAMES']


class ManagedTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix='laya-managed-test-')
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.repo = self.root/'repo'; self.repo.mkdir()
        self.target = self.root/'target'; self.target.mkdir()
        self.manifest = self.target/'laya-managed-release.json'
        subprocess.run(['git', 'init', '-q', str(self.repo)], check=True)
        self.git('config', 'user.name', 'Isolated Test')
        self.git('config', 'user.email', 'isolated@example.invalid')
        directory = self.repo/'deployment/oracle-celar/gateway'; directory.mkdir(parents=True)
        for name in NAMES:
            (directory/name).write_text('# old reviewed adapter\n')
            (self.target/name).write_text('# old reviewed adapter\n')
            (self.target/name).chmod(0o640)
        self.git('add', '.')
        self.git('commit', '-qm', 'initial reviewed adapter')
        self.old = self.git('rev-parse', 'HEAD').strip()
        for name in NAMES: (directory/name).write_text('# new reviewed adapter\n')
        self.git('commit', '-qam', 'reviewed adapter upgrade')
        self.new = self.git('rev-parse', 'HEAD').strip()

    def git(self, *args):
        return subprocess.check_output(['git', '-C', str(self.repo), *args], text=True)

    def evidence(self):
        value = {'version': 1, 'source_commit': self.old,
                 'adapters': {name: hashlib.sha256((self.target/name).read_bytes()).hexdigest() for name in NAMES}}
        self.manifest.write_text(json.dumps(value)); self.manifest.chmod(0o640)
        return value

    def verify(self, mode='apply'):
        return VERIFY(self.repo, self.target, self.manifest, mode, self.old, os.getuid())

    def test_exact_legacy_install_can_be_adopted_and_upgraded(self):
        self.assertEqual(self.verify(), self.old)
        self.assertNotEqual((self.target/NAMES[0]).read_bytes(), (self.repo/'deployment/oracle-celar/gateway'/NAMES[0]).read_bytes())

    def test_recorded_previous_release_allows_reviewed_upgrade(self):
        self.evidence()
        self.assertEqual(self.verify(), self.old)

    def test_unmanaged_legacy_file_is_rejected(self):
        (self.target/NAMES[0]).write_text('# arbitrary unrecorded file\n')
        with self.assertRaises(RuntimeError): self.verify()

    def test_tampered_manifest_or_adapter_is_rejected(self):
        value = self.evidence()
        value['adapters'][NAMES[0]] = '0'*64
        self.manifest.write_text(json.dumps(value))
        with self.assertRaises(RuntimeError): self.verify()
        self.evidence(); (self.target/NAMES[0]).write_text('# changed installed file\n')
        with self.assertRaises(RuntimeError): self.verify()

    def test_symlink_and_writable_evidence_are_rejected(self):
        self.evidence(); self.manifest.chmod(0o666)
        with self.assertRaises(RuntimeError): self.verify()
        self.manifest.chmod(0o640)
        path = self.target/NAMES[0]; content = self.root/'external.py'; path.rename(content); path.symlink_to(content)
        with self.assertRaises(RuntimeError): self.verify()

    def test_recorded_revision_must_match_git_source(self):
        value = self.evidence(); value['source_commit'] = self.new
        self.manifest.write_text(json.dumps(value))
        with self.assertRaises(RuntimeError): self.verify()

    def test_newer_install_cannot_be_silently_downgraded(self):
        for name in NAMES: (self.target/name).write_text('# new reviewed adapter\n')
        value = self.evidence(); value['source_commit'] = self.new; self.manifest.write_text(json.dumps(value))
        self.git('checkout', '--detach', self.old)
        with self.assertRaises(subprocess.CalledProcessError): self.verify()
        self.assertEqual(self.verify('rollback'), self.new)

    def test_failed_cutover_restores_previous_adapter_and_manifest(self):
        self.evidence()
        stage = self.root/'stage'; stage.mkdir()
        dropin = self.root/'dropin'; dropin.write_text('prior drop-in\n'); dropin.chmod(0o640)
        import shutil
        for name in NAMES:
            shutil.copy2(self.target/name, stage/('original-' + name))
            (self.target/name).write_text('broken replacement\n')
        shutil.copy2(dropin, stage/'original-dropin')
        shutil.copy2(self.manifest, stage/'original-manifest')
        original_manifest = self.manifest.read_bytes()
        self.manifest.write_text('broken manifest\n'); dropin.write_text('broken drop-in\n')
        binary = self.root/'bin'; binary.mkdir()
        log = self.root/'service.log'
        command = binary/'systemctl'; command.write_text('#!/bin/sh\nprintf "%s\\n" "$*" >> "$SERVICE_LOG"\nexit 0\n'); command.chmod(0o700)
        function = SOURCE[SOURCE.index('cleanup() {'):SOURCE.index('\ntrap cleanup EXIT')]
        env = {**os.environ, 'STAGE': str(stage), 'TARGET': str(self.target), 'DROPIN': str(dropin),
               'MANIFEST': str(self.manifest), 'CHANGED': '1', 'EXISTING': '1', 'HAD_MANIFEST': '1',
               'STARTED_LAYA': '0', 'MODE': 'apply', 'PATH': str(binary)+os.pathsep+os.environ['PATH'], 'SERVICE_LOG': str(log)}
        result = subprocess.run(['bash'], input=function+'\nfalse\ncleanup\n', text=True, capture_output=True, env=env)
        self.assertEqual(result.returncode, 1, result.stderr)
        self.assertIn('LAYA_GATEWAY_RESTORE=PASS', result.stdout)
        for name in NAMES:
            self.assertEqual((self.target/name).read_text(), '# old reviewed adapter\n')
            self.assertEqual((self.target/name).stat().st_mode & 0o777, 0o640)
        self.assertEqual(self.manifest.read_bytes(), original_manifest)
        self.assertEqual(dropin.read_text(), 'prior drop-in\n')
        self.assertIn('restart celar-ai-gateway.service', log.read_text())
        self.assertNotIn('ollama', log.read_text()); self.assertNotIn('caddy', log.read_text())


if __name__ == '__main__': unittest.main()
