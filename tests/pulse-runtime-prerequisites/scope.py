"""Exact source registration for PR1243 document-runtime prerequisites.

This records reviewed Test-only source bytes. It does not grant deployment,
Production, account, or data mutation authority beyond the existing controllers.
"""
from pathlib import Path, PurePosixPath
import hashlib, json, os, subprocess

ROOT=Path(__file__).resolve().parents[2]
BASE='d81a53a68b5704b4db2838666b02e2a6dfc23618'
BRANCH='fix/pulse-document-runtime-prereqs-20261002'
REPOSITORY='ahmedadeyemi-cts/project-time-platform'
MANIFEST='tests/pulse-runtime-prerequisites/manifest.json'
ALLOWED={'deployment/pulse-services/cutover.py', 'tests/pulse_services/test_cutover.py', 'scripts/release-test/recover-pr1140-migration-retry-orphan.py', 'tests/pulse-activation-release/test_scope.py', 'src/frontend/project-time-web/scripts/validate-module-011-managed-architecture.mjs', 'scripts/release-test/validate-protected-test-controller-branches.sh', 'scripts/release-test/verify-module025-quarantine-controller.py', 'tests/laya/processed-source-scope.py', 'tests/flowhive-psa-admission.test.mjs', 'tests/security-release/admission_fixture.py', 'tests/pulse-runtime-prerequisites/recovery_registration.json', 'tests/pulse-runtime-prerequisites/test_scope.py', '.github/workflows/pr1151-uat-supersession-ci.yml', 'tests/pulse-activation-release/controller.py', '.github/workflows/flowhive-psa-release-control-ci.yml', '.github/workflows/pr1140-uat-recovery-ci.yml', 'tests/security-release/test_controller_registration.py', 'scripts/release-test/verify-pr1151-uat-supersession.py', 'tests/pulse-runtime-prerequisites/manifest.json', 'scripts/release-test/recover-pr1140-uat-orphan.py', 'scripts/release-test/recover-pr1139-uat-orphan.py', '.github/workflows/projectpulse-deploy-test.yml', 'scripts/release-test/resolve-flowhive-installed-deployment.py', '.github/workflows/uat-migration-throttle-recovery-ci.yml', 'tests/pulse-runtime-prerequisites/scope.py', 'scripts/release-test/validate-module025-governed-release.sh', 'scripts/release-test/build-and-run-celar-ai-private-runtime-migrations.sh', '.github/workflows/pr1139-uat-recovery-ci.yml', 'scripts/release-test/verify-pr1204-uat-supersession.py', 'tests/pulse_services/test_native_startup.py'}

def require(value,message):
    if not value: raise RuntimeError(message)

def git(*args):
    return subprocess.check_output(['git','-C',str(ROOT),*args],timeout=60)

def frozen(path):
    if path in ALLOWED: return False
    return (path.startswith('.github/') or path.startswith('scripts/release-test/')
            or path.startswith('database/') or path.startswith('deployment/')
            or path.startswith('tests/pulse-activation-')
            or path.startswith('tests/security-release/')
            or path == 'src/backend/ProjectTime.Api/Program.cs'
            or path == '.github/CODEOWNERS'
            or path == '.github/flowhive-psa-protected-test-candidate.json'
            or path == '.github/workflows/projectpulse-deploy-production.yml')

def verify_identity(branch,repository,base,number):
    require((branch,repository,base,str(number))==(BRANCH,REPOSITORY,'main','1243'),'Wrong source identity')

def verify_paths(paths,manifest):
    require(manifest['base']==BASE,'Wrong fixed baseline')
    require(manifest['files']==sorted(set(manifest['files'])),'Paths must be sorted and unique')
    require(set(paths)==ALLOWED,'Only the exact reviewed prerequisite source is permitted')
    require(set(paths)==set(manifest['files']),'Unexpected or missing source files')
    require(set(manifest['sha256'])==set(paths)-{MANIFEST},'Every source file must be hash-bound')
    for path in paths:
        pure=PurePosixPath(path)
        require(not pure.is_absolute() and '..' not in pure.parts,'Unsafe source path')
        require(not frozen(path),'Deployment authority changed outside the exact registered set')

def verify_content(data,digest,mode):
    require(mode in ('100644','100755'),'Symlinks and submodules are prohibited')
    require(hashlib.sha256(data).hexdigest()==digest,'Registered source bytes changed')

def main():
    verify_identity(os.getenv('GITHUB_HEAD_REF') or git('branch','--show-current').decode().strip(),
                    os.getenv('GITHUB_REPOSITORY',REPOSITORY),
                    os.getenv('GITHUB_BASE_REF','main'),
                    os.getenv('PR_NUMBER','1243'))
    if os.getenv('GITHUB_EVENT_PATH'):
        event=json.loads(Path(os.environ['GITHUB_EVENT_PATH']).read_text())
        if event.get('pull_request'):
            pr=event['pull_request']
            verify_identity(pr['head']['ref'],pr['head']['repo']['full_name'],pr['base']['ref'],event['number'])
            require(pr['base']['repo']['full_name']==REPOSITORY,'Wrong target repository')
    subprocess.run(['git','-C',str(ROOT),'merge-base','--is-ancestor',BASE,'HEAD'],check=True)
    manifest=json.loads((ROOT/MANIFEST).read_text())
    paths=git('diff','--name-only',BASE,'HEAD').decode().splitlines()
    verify_paths(paths,manifest)
    for path in paths:
        entry=git('ls-tree','HEAD','--',path).decode().split()
        require(entry and entry[0] in ('100644','100755'),'Missing or unsafe source entry')
        require(not (ROOT/path).is_symlink(),'Working source is a symlink')
        data=(ROOT/path).read_bytes()
        require(data==git('show',f'HEAD:{path}'),'Working tree differs from committed content: '+path)
        if path!=MANIFEST: verify_content(data,manifest['sha256'][path],entry[0])
    for path in git('ls-tree','-r','--name-only',BASE).decode().splitlines():
        if frozen(path):
            require((ROOT/path).is_file() and not (ROOT/path).is_symlink()
                    and (ROOT/path).read_bytes()==git('show',f'{BASE}:{path}'),
                    'Frozen deployment authority changed: '+path)
    subprocess.run(['git','-C',str(ROOT),'diff','--check',BASE,'HEAD'],check=True)
    print('PULSE_DOCUMENT_RUNTIME_PREREQUISITES_EXACT_SOURCE=PASS; production_mutation=false; account_mutation=false')

if __name__=='__main__': main()
