"""Run the real revision helper against bounded read failures, with no cloud access."""
from pathlib import Path
import json
import os
import subprocess
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
REVISION = 'api--m1be-123-1'
IMAGE = 'registry.invalid/api@sha256:' + 'a' * 64
MOCK = r'''#!/usr/bin/env python3
import json, os, sys
from pathlib import Path
args = sys.argv[1:]
with open(os.environ['CALLS'], 'a') as log: log.write(json.dumps(args) + '\n')
revision = os.environ['REVISION']
if args[:3] == ['containerapp', 'revision', 'show']:
    print(json.dumps(dict(image=os.environ['MOCK_IMAGE'], provisioningState='Provisioned', healthState='Healthy', active=True, trafficWeight=100)))
elif args[:3] == ['containerapp', 'revision', 'list']:
    path = Path(os.environ['COUNT'])
    count = int(path.read_text()) + 1 if path.exists() else 1
    path.write_text(str(count))
    if count <= int(os.environ['FAIL_READS']):
        print('ERROR: Service Unavailable', file=sys.stderr)
        sys.exit(1)
    print(json.dumps([dict(name=revision, properties=dict(active=True))]))
elif args[:2] == ['containerapp', 'show']:
    if '--query' in args:
        print(revision)
    else:
        print(json.dumps(dict(tags=dict(environment=os.environ['ENVIRONMENT']), properties=dict(configuration=dict(activeRevisionsMode='Single'), latestRevisionName=os.environ['LATEST']))))
else:
    print('Unexpected Azure operation', file=sys.stderr)
    sys.exit(99)
'''


class RevisionReadTests(unittest.TestCase):
    def exercise(self, failures, environment='test', latest=REVISION):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            az = root / 'az'; az.write_text(MOCK); az.chmod(0o700)
            sleep = root / 'sleep'; sleep.write_text('#!/bin/sh\nexit 0\n'); sleep.chmod(0o700)
            env = {**os.environ, 'PATH': directory + ':' + os.environ['PATH'],
                   'CALLS': str(root / 'calls'), 'COUNT': str(root / 'count'),
                   'REVISION': REVISION, 'MOCK_IMAGE': IMAGE, 'FAIL_READS': str(failures),
                   'ENVIRONMENT': environment, 'LATEST': latest, 'EVIDENCE_DIR': str(root / 'evidence')}
            result = subprocess.run(['bash', str(ROOT / 'scripts/wait-containerapp-ready-revision.sh'),
                                     'test-rg', 'api', REVISION, IMAGE, '2', '1'],
                                    env=env, capture_output=True, text=True, timeout=20)
            calls = [json.loads(line) for line in (root / 'calls').read_text().splitlines()]
            self.assertFalse(any('activate' in args or 'deactivate' in args or 'update' in args for args in calls))
            count = int((root / 'count').read_text()) if (root / 'count').exists() else 0
            receipt = json.loads((root / 'evidence/module001b-revision-reconcile.json').read_text())
            return result, count, receipt

    def test_transient_reads_recover_with_exact_identity_and_convergence(self):
        for failures in (1, 2):
            with self.subTest(failures=failures):
                result, count, receipt = self.exercise(failures)
                self.assertEqual(result.returncode, 0, result.stderr)
                self.assertEqual(count, failures + 1)
                self.assertEqual(receipt['phase'], 'converged')
                self.assertEqual(receipt['expectedImage'], IMAGE)
                self.assertEqual(receipt['activeRevisions'], [REVISION])

    def test_persistent_read_failure_is_bounded_and_never_converged(self):
        result, count, receipt = self.exercise(99)
        self.assertNotEqual(result.returncode, 0)
        self.assertEqual(count, 3)
        self.assertEqual(receipt['phase'], 'observed')

    def test_environment_and_latest_revision_guards_remain_mandatory(self):
        for environment, latest in [('production', REVISION), ('test', 'api--newer-release')]:
            with self.subTest(environment=environment, latest=latest):
                result, count, receipt = self.exercise(0, environment, latest)
                self.assertNotEqual(result.returncode, 0)
                self.assertEqual(count, 0)
                self.assertNotEqual(receipt['phase'], 'converged')


if __name__ == '__main__':
    unittest.main()
