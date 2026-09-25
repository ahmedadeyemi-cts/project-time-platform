"""Content-bound, control-only compatibility repair for the accepted migration125 release.
This is source validation, not a bypass of reviews, CI, concurrency or Test protection.
"""
from pathlib import Path
import hashlib
import os
import subprocess

ROOT=Path(__file__).resolve().parents[1]
BASE='b0334754cfc52e5c7e99300aa26b0a165fae947a'
BRANCH='control/uat-migration125-guard-compatibility-20260925'
SELF='tests/uat-migration125-compatibility-scope.py'
TEST='tests/test-uat-migration125-compatibility.py'
TEST_SHA256='fd2ac669e8d38e4a8d4fac0bdf35829069b350af4c36423aa19a7ee05d88cfc1'
REPLACEMENTS={'.github/workflows/module025-protected-uat-control.yml': [['git diff --quiet '
                                                            "'045b66ca01baa68b2f5b3f6eb9e063c23c335981' HEAD "
                                                            '-- '
                                                            '.github/workflows/projectpulse-deploy-test.yml',
                                                            'python3 '
                                                            'scripts/release-test/verify-module025-quarantine-controller.py '
                                                            '--base '
                                                            "'045b66ca01baa68b2f5b3f6eb9e063c23c335981'"]],
 '.github/workflows/pr1139-uat-recovery-ci.yml': [['test "$(git rev-parse '
                                                   'HEAD:.github/workflows/projectpulse-deploy-test.yml)" = '
                                                   '634983f88d5ce3161b626010c3e20c41a80e3758',
                                                   '[[ "$(git rev-parse '
                                                   'HEAD:.github/workflows/projectpulse-deploy-test.yml)" =~ '
                                                   '^(634983f88d5ce3161b626010c3e20c41a80e3758|be0296f7ad5ac5839fb52ee9aac2502973e60cdb)$ '
                                                   ']]']],
 '.github/workflows/pr1140-uat-recovery-ci.yml': [['test "$(git rev-parse '
                                                   'HEAD:.github/workflows/projectpulse-deploy-test.yml)" = '
                                                   '634983f88d5ce3161b626010c3e20c41a80e3758',
                                                   '[[ "$(git rev-parse '
                                                   'HEAD:.github/workflows/projectpulse-deploy-test.yml)" =~ '
                                                   '^(634983f88d5ce3161b626010c3e20c41a80e3758|be0296f7ad5ac5839fb52ee9aac2502973e60cdb)$ '
                                                   ']]']],
 'scripts/release-test/recover-pr1139-uat-orphan.py': [['DEPLOYMENT_BLOB = '
                                                        '"634983f88d5ce3161b626010c3e20c41a80e3758"',
                                                        'DEPLOYMENT_BLOB = '
                                                        '"634983f88d5ce3161b626010c3e20c41a80e3758"\n'
                                                        '# PR1158 is already merged: the only additions are '
                                                        'migration125 artifact/evidence entries.\n'
                                                        '# Retain the historical identity and recognize only '
                                                        'the complete reviewed new controller.\n'
                                                        'MIGRATION125_DEPLOYMENT_BLOB = '
                                                        '"be0296f7ad5ac5839fb52ee9aac2502973e60cdb"'],
                                                       ['and git("rev-parse", f"{current}:{DEPLOYMENT}") == '
                                                        'DEPLOYMENT_BLOB,',
                                                        'and git("rev-parse", f"{current}:{DEPLOYMENT}") in\n'
                                                        '            (DEPLOYMENT_BLOB, '
                                                        'MIGRATION125_DEPLOYMENT_BLOB),'],
                                                       ['"deployment_controller_unchanged": True',
                                                        '"deployment_guard_unchanged": True']],
 'scripts/release-test/recover-pr1140-migration-retry-orphan.py': [['DEPLOYMENT_BLOB = '
                                                                    '"634983f88d5ce3161b626010c3e20c41a80e3758"',
                                                                    'DEPLOYMENT_BLOB = '
                                                                    '"634983f88d5ce3161b626010c3e20c41a80e3758"\n'
                                                                    '# PR1158 is already merged: the only '
                                                                    'additions are migration125 '
                                                                    'artifact/evidence entries.\n'
                                                                    '# Retain the historical identity and '
                                                                    'recognize only the complete reviewed '
                                                                    'new controller.\n'
                                                                    'MIGRATION125_DEPLOYMENT_BLOB = '
                                                                    '"be0296f7ad5ac5839fb52ee9aac2502973e60cdb"'],
                                                                   ['and git("rev-parse", '
                                                                    'f"{current}:{DEPLOYMENT}") == '
                                                                    'DEPLOYMENT_BLOB,',
                                                                    'and git("rev-parse", '
                                                                    'f"{current}:{DEPLOYMENT}") in\n'
                                                                    '            (DEPLOYMENT_BLOB, '
                                                                    'MIGRATION125_DEPLOYMENT_BLOB),'],
                                                                   ['"deployment_controller_unchanged": True',
                                                                    '"deployment_guard_unchanged": True']],
 'scripts/release-test/recover-pr1140-uat-orphan.py': [['DEPLOYMENT_BLOB = '
                                                        '"634983f88d5ce3161b626010c3e20c41a80e3758"',
                                                        'DEPLOYMENT_BLOB = '
                                                        '"634983f88d5ce3161b626010c3e20c41a80e3758"\n'
                                                        '# PR1158 is already merged: the only additions are '
                                                        'migration125 artifact/evidence entries.\n'
                                                        '# Retain the historical identity and recognize only '
                                                        'the complete reviewed new controller.\n'
                                                        'MIGRATION125_DEPLOYMENT_BLOB = '
                                                        '"be0296f7ad5ac5839fb52ee9aac2502973e60cdb"'],
                                                       ['and git("rev-parse", f"{current}:{DEPLOYMENT}") == '
                                                        'DEPLOYMENT_BLOB,',
                                                        'and git("rev-parse", f"{current}:{DEPLOYMENT}") in\n'
                                                        '            (DEPLOYMENT_BLOB, '
                                                        'MIGRATION125_DEPLOYMENT_BLOB),'],
                                                       ['"deployment_controller_unchanged": True',
                                                        '"deployment_guard_unchanged": True']],
 'scripts/release-test/validate-protected-test-controller-branches.sh': [['elif [[ "$HEAD_BRANCH" == '
                                                                          "'fix/automatic-document-admission-laya-20260923' "
                                                                          ']]; then',
                                                                          'elif [[ "$HEAD_BRANCH" == '
                                                                          "'control/uat-migration125-guard-compatibility-20260925' "
                                                                          ']]; then\n'
                                                                          '  python3 '
                                                                          'tests/uat-migration125-compatibility-scope.py\n'
                                                                          '  python3 '
                                                                          'tests/test-uat-migration125-compatibility.py\n'
                                                                          '  node '
                                                                          'tests/validate-systemwide-image-build-controller.mjs\n'
                                                                          'elif [[ "$HEAD_BRANCH" == '
                                                                          "'fix/automatic-document-admission-laya-20260923' "
                                                                          ']]; then']],
 'scripts/release-test/verify-module025-quarantine-controller.py': [['"c15ef12d5ce1bc54c15d8b31c87a50daa94bad17"), '
                                                                     'default=BASE)',
                                                                     '"c15ef12d5ce1bc54c15d8b31c87a50daa94bad17", '
                                                                     '"045b66ca01baa68b2f5b3f6eb9e063c23c335981"), '
                                                                     'default=BASE)']],
 'scripts/release-test/verify-pr1151-uat-supersession.py': [['DEPLOYMENT_BLOB = '
                                                             '"634983f88d5ce3161b626010c3e20c41a80e3758"',
                                                             'DEPLOYMENT_BLOB = '
                                                             '"634983f88d5ce3161b626010c3e20c41a80e3758"\n'
                                                             '# PR1158 is already merged: the only additions '
                                                             'are migration125 artifact/evidence entries.\n'
                                                             '# Retain the historical identity and recognize '
                                                             'only the complete reviewed new controller.\n'
                                                             'MIGRATION125_DEPLOYMENT_BLOB = '
                                                             '"be0296f7ad5ac5839fb52ee9aac2502973e60cdb"'],
                                                            ['    for revision in (OLD_SHA, current):\n'
                                                             '        require(git("rev-parse", '
                                                             'f"{revision}:{DEPLOYMENT}") == '
                                                             'DEPLOYMENT_BLOB,\n'
                                                             '                "The pinned pre-Azure '
                                                             'deployment guard changed")',
                                                             '    require(git("rev-parse", '
                                                             'f"{OLD_SHA}:{DEPLOYMENT}") == '
                                                             'DEPLOYMENT_BLOB,\n'
                                                             '            "The pinned historical deployment '
                                                             'controller changed")\n'
                                                             '    require(git("rev-parse", '
                                                             'f"{current}:{DEPLOYMENT}") in\n'
                                                             '            (DEPLOYMENT_BLOB, '
                                                             'MIGRATION125_DEPLOYMENT_BLOB),\n'
                                                             '            "The deployment controller is not '
                                                             'an exact reviewed version")'],
                                                            ['"deployment_controller_unchanged": True',
                                                             '"deployment_guard_unchanged": True']],
 'tests/pr1140-uat-recovery-scope.py': [['assert (ROOT / '
                                         "'scripts/release-test/recover-pr1140-uat-orphan.py').read_text() "
                                         '== expected',
                                         'assert '
                                         "read_source('scripts/release-test/recover-pr1140-uat-orphan.py') "
                                         '== expected']],
 'tests/test-pr1139-uat-recovery.py': [['data = (ROOT / recovery.SUPERVISOR).read_text()',
                                        'data = subprocess.check_output(["git", "show", '
                                        '"b0334754cfc52e5c7e99300aa26b0a165fae947a:" + recovery.SUPERVISOR], '
                                        'cwd=ROOT, text=True)']],
 'tests/test-pr1140-migration-retry-recovery.py': [('data=(ROOT/current.SUPERVISOR).read_text()',
                                                    'data=previous.subprocess.check_output(["git","show","436010a2838c94e7b94251fbdada16453b917dcf:"+current.SUPERVISOR],cwd=ROOT,text=True)')],
 'tests/test-pr1140-uat-recovery.py': [['(ROOT / scope.SUPERVISOR).read_text(), scope.SUPERVISOR_ANCHOR',
                                        'scope.git("show", "b0334754cfc52e5c7e99300aa26b0a165fae947a:" + '
                                        'scope.SUPERVISOR) + "\\n", scope.SUPERVISOR_ANCHOR']]}

def git(*args):
    return subprocess.check_output(['git','-C',str(ROOT),*args],text=True)

def expected_source(path):
    source=git('show',BASE+':'+path)
    for before,after in REPLACEMENTS[path]:
        assert source.count(before)==1, 'Ambiguous reviewed replacement: '+path
        source=source.replace(before,after,1)
    return source

def verify_scope(actual):
    assert set(actual)==set(REPLACEMENTS)|{SELF,TEST}, 'Incomplete or unrelated control-only change set'

def main():
    assert (os.environ.get('GITHUB_HEAD_REF') or git('branch','--show-current').strip())==BRANCH
    assert os.environ.get('GITHUB_REPOSITORY','ahmedadeyemi-cts/project-time-platform')=='ahmedadeyemi-cts/project-time-platform'
    assert git('merge-base','origin/main','HEAD').strip()==BASE, 'Reconcile and review newer main before release'
    rows=git('diff','--name-status',BASE,'HEAD').splitlines()
    assert all(row.split('\t')[0] in ('A','M') for row in rows), 'No removal or rename authorized'
    verify_scope([row.split('\t',1)[1] for row in rows])
    for path in REPLACEMENTS:
        assert (ROOT/path).read_text()==expected_source(path), 'Unreviewed control content: '+path
    assert hashlib.sha256((ROOT/TEST).read_bytes()).hexdigest()==TEST_SHA256, 'Executable compatibility tests changed'
    assert git('rev-parse','HEAD:.github/workflows/projectpulse-deploy-test.yml').strip()=='be0296f7ad5ac5839fb52ee9aac2502973e60cdb'
    subprocess.run(['git','-C',str(ROOT),'diff','--exit-code',BASE,'HEAD','--','src','database',
                    '.github/workflows/projectpulse-deploy-test.yml','.github/workflows/projectpulse-deploy-production.yml',
                    '.github/CODEOWNERS','scripts/release-test/flowhive-psa-admission.mjs'],check=True)
    subprocess.run(['git','-C',str(ROOT),'diff','--exit-code'],check=True)
    subprocess.run(['git','-C',str(ROOT),'diff','--check',BASE,'HEAD'],check=True)
    for bad in (set(REPLACEMENTS),set(REPLACEMENTS)|{SELF,TEST,'src/unauthorized.cs'}):
        try:verify_scope(bad)
        except AssertionError:pass
        else:raise AssertionError('Negative scope test accepted unrelated or partial changes')
    print('UAT_MIGRATION125_COMPATIBILITY_SCOPE=PASS; application_sql_deployment_workflow_unchanged=true')

if __name__=='__main__':main()
