"""Execute the real packaging wrapper offline; all Azure/SQL boundaries are synthetic."""
from pathlib import Path
import os
import shutil
import subprocess
import tempfile

ROOT = Path(__file__).resolve().parents[1]
SCRIPT = ROOT / 'scripts/release-test/build-and-run-project-planning-document-authority-migration-job.sh'
SOURCE = SCRIPT.read_text()
inside = SOURCE.split("<<'ENTRYPOINT'\n", 1)[1].split('\nENTRYPOINT\n', 1)[0]
outside = SOURCE.replace(inside, '')
assert '\npsql ' not in outside, 'Database commands must execute only in the private-network image'
assert 'sha256sum --check' not in outside, 'Packaged checksums are checked inside their image'
for label in ('sequential', 'automatic'):
    assert f'sha256sum --check --status database/flowhive-{label}.sha256' in inside
assert inside.index('/121_flowhive_') < inside.index('/122_flowhive_')

with tempfile.TemporaryDirectory(prefix='flowhive-package-') as directory:
    fixture = Path(directory); release = fixture/'release'; commands = fixture/'bin'; commands.mkdir()
    (release/'database').mkdir(parents=True)
    (release/'database/migrations').symlink_to(ROOT/'database/migrations', target_is_directory=True)
    scripts = release/'scripts/release-test'; scripts.mkdir(parents=True)
    for name in ('reconcile-module-catalog.mjs', 'verify-flowhive-task-notifications.sql',
                 'verify-flowhive-sequential-checkpoints.sql', 'verify-flowhive-automatic-first-draft.sql'):
        (scripts/name).symlink_to(ROOT/'scripts/release-test'/name)
    runner = scripts/'run-project-planning-document-authority-migration-job.sh'
    runner.write_text('#!/bin/bash\nset -euo pipefail\n[[ "$RELIABILITY_MIGRATION_IMAGE" == *"@sha256:"* ]]\n[[ "${FAIL_PRIVATE_JOB:-0}" != 1 ]]\necho PRIVATE_JOB_VERIFIED\n')
    az = commands/'az'
    az.write_text('''#!/usr/bin/env python3
import os, pathlib, shutil, sys
args=sys.argv[1:]
if args[:2]==['acr','build']:
    target=pathlib.Path(os.environ['CAPTURE_PACKAGE'])
    shutil.copytree(args[-1],target,dirs_exist_ok=True)
elif args[:3]==['acr','repository','show']: print('sha256:'+'a'*64)
else: raise SystemExit('Unexpected Azure call in offline fixture')
'''); az.chmod(0o755)
    psql = commands/'psql'; psql.write_text('#!/bin/sh\necho HOST_SQL_FORBIDDEN >&2\nexit 91\n'); psql.chmod(0o755)
    captured = fixture/'packaged'
    env = {**os.environ, 'PROJECTPULSE_RELEASE_ROOT':str(release), 'AZURE_ACR_NAME':'fixtureacr',
           'RELIABILITY_RELEASE_COMMIT':'a'*40, 'GITHUB_RUN_ID':'123', 'GITHUB_RUN_ATTEMPT':'1',
           'RUNNER_TEMP':str(fixture), 'EVIDENCE_DIR':str(fixture/'evidence'), 'CAPTURE_PACKAGE':str(captured),
           'PATH':str(commands)+os.pathsep+os.environ['PATH']}
    def run(fail=False):
        return subprocess.run(['bash',str(SCRIPT)],env={**env,'FAIL_PRIVATE_JOB':'1' if fail else '0'},
                              text=True,capture_output=True,timeout=30)
    passed=run(); assert passed.returncode==0, passed.stderr
    for number, name in ((121,'FLOWHIVE_SEQUENTIAL_CHECKPOINTS'),(122,'FLOWHIVE_AUTOMATIC_FIRST_DRAFT')):
        marker=f'MIGRATION_{number}_{name}=APPLIED_AND_VERIFIED'
        assert marker in passed.stdout and passed.stdout.index('PRIVATE_JOB_VERIFIED')<passed.stdout.index(marker)
    for label in ('notifications','sequential','automatic'):
        subprocess.run(['sha256sum','--check','--status',f'database/flowhive-{label}.sha256'],cwd=captured,check=True)
    assert (captured/'entrypoint.sh').read_text()==inside+'\n'
    failed=run(True); assert failed.returncode!=0
    assert 'MIGRATION_121_FLOWHIVE_SEQUENTIAL_CHECKPOINTS=APPLIED_AND_VERIFIED' not in failed.stdout
    assert 'MIGRATION_122_FLOWHIVE_AUTOMATIC_FIRST_DRAFT=APPLIED_AND_VERIFIED' not in failed.stdout
    for label in ('sequential','automatic'):
        manifest=captured/f'database/flowhive-{label}.sha256'
        target=captured/manifest.read_text().splitlines()[0].split()[-1]
        original=target.read_bytes(); target.chmod(0o644); target.write_bytes(original+b'\n-- changed package\n')
        assert subprocess.run(['sha256sum','--check','--status',str(manifest)],cwd=captured).returncode!=0
        target.write_bytes(original)
print('FLOWHIVE_MIGRATION_PACKAGE=PASS host_sql=forbidden private_failure=propagated checksums=verified tampering=rejected')
