#!/usr/bin/env python3
"""Exact PR1156 application/Teams framing scope, not deployment authorization.

All pre-existing controller contracts still execute. Only a validated exact file
set replaces the scratch allowlist for this PR; no other branch is relaxed.
"""
from pathlib import Path
import os
import re
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[1]
BASE = 'f9371f6288b5346f6c8a7375d90983379c5a51b6'
FIRST = '2133625d5e04cc3acbd89e4e78471cf54365c367'
BRANCH = 'fix/module065-teams-delivery-app-availability-20260923'
PREPARE = 'scripts/release-test/prepare-protected-test-scope-manifests.sh'
NGINX = 'deployment/containers/web/default.conf.template'
FILES = frozenset('''
.github/workflows/module065-teams-delivery-ci.yml
deployment/containers/web/default.conf.template
deployment/teams/pulse-uat.manifest.json
docs/repairs/module065-teams-delivery.md
scripts/release-test/prepare-protected-test-scope-manifests.sh
scripts/teams/build-package.py
scripts/teams/validate-landing.py
scripts/teams/validate-package.py
src/backend/ProjectTime.Api/Modules/MicrosoftTeamsNotificationModule.cs
src/backend/ProjectTime.Api/Modules/MicrosoftTeamsNotificationProtocol.cs
src/backend/ProjectTime.Api/Modules/MicrosoftTeamsServicesSnapshot.cs
src/frontend/project-time-web/public/teams-notifications/index.html
src/frontend/project-time-web/public/teams-notifications/landing.css
src/frontend/project-time-web/public/teams-notifications/landing.js
src/frontend/project-time-web/src/MicrosoftTeamsNotificationPanel.jsx
tests/MicrosoftTeamsDeliveryTests/MicrosoftTeamsDeliveryTests.csproj
tests/MicrosoftTeamsDeliveryTests/Program.cs
tests/teams-delivery-release-scope.py
tests/test-teams-delivery-release-scope.py
'''.split())
REGISTRATION = '''
# MODULE065_TEAMS_APPLICATION_SCOPE_BEGIN
if [[ "$HEAD_BRANCH" == 'fix/module065-teams-delivery-app-availability-20260923' ]]; then
  python3 tests/teams-delivery-release-scope.py "$CIT/allowed-release-files"
  python3 tests/test-teams-delivery-release-scope.py
  node tests/validate-systemwide-image-build-controller.mjs
fi
# MODULE065_TEAMS_APPLICATION_SCOPE_END
'''
LANDING = '''    # Only the public, non-sensitive Teams landing may be framed by Teams.
    # Disable merged inherited headers here so the global self-only CSP cannot
    # contradict this narrow policy. The application and /api/ stay unchanged.
    location = /teams-notifications/index.html {
        add_header_inherit off;
        add_header X-Content-Type-Options "nosniff" always;
        add_header Referrer-Policy "no-referrer" always;
        add_header Content-Security-Policy "default-src 'none'; script-src 'self' https://res.cdn.office.net; style-src 'self'; img-src 'self' data:; connect-src https://res.cdn.office.net; object-src 'none'; base-uri 'none'; form-action 'none'; frame-ancestors https://teams.microsoft.com https://*.teams.microsoft.com https://*.cloud.microsoft" always;
        add_header Permissions-Policy "camera=(), microphone=(), geolocation=()" always;
        add_header Strict-Transport-Security "max-age=31536000" always;
        add_header Cache-Control "no-store" always;
        try_files $uri =404;
    }

'''

def require(ok, message):
    if not ok:
        raise RuntimeError(message)

def git(*args):
    p = subprocess.run(['git', '-C', str(ROOT), *args], capture_output=True, text=True, timeout=30)
    require(p.returncode == 0, 'Source provenance command failed')
    return p.stdout

def validate_identity(base, branch, number):
    require(base == BASE and branch == BRANCH and str(number) == '1156', 'Only the exact reviewed PR1156 base/branch may use this application scope')

def validate_files(changed, modes):
    require(set(changed) == FILES, 'Teams repair differs from its exact 19-file scope: ' + str(sorted(set(changed) ^ FILES)))
    require(set(modes) == FILES and all(v == '100644' for v in modes.values()), 'Every changed file must remain an ordinary non-executable file')

def validate_prepare(before, after):
    require(after == before + REGISTRATION, 'All prior source-classification logic must remain byte-for-byte unchanged')

def validate_nginx(before, after):
    anchor = '    location = /index.html {'
    require(before.count(anchor) == 1, 'The existing index location must be unambiguous')
    require(after == before.replace(anchor, LANDING + anchor, 1), 'Only the exact public Teams landing may change its frame boundary')

def main(output):
    validate_identity(os.environ.get('BASE_SHA'), os.environ.get('GITHUB_HEAD_REF'), os.environ.get('PR_NUMBER'))
    require(git('merge-base', BASE, 'HEAD').strip() == BASE, 'PR must descend from the reviewed UI release')
    changed = git('diff', '--name-only', BASE, 'HEAD').splitlines()
    modes = {}
    for name in changed:
        entry = git('ls-tree', 'HEAD', '--', name).split()
        require(len(entry) >= 4, 'Missing changed file')
        modes[name] = entry[0]
    validate_files(changed, modes)
    validate_prepare(git('show', f'{BASE}:{PREPARE}'), (ROOT / PREPARE).read_text())
    validate_nginx(git('show', f'{BASE}:{NGINX}'), (ROOT / NGINX).read_text())
    # The sender tests, schema checks and real Nginx/browser gate that already ran
    # must not be weakened to obtain admission for the framing exception.
    pinned = [name for name in FILES if name.startswith(('scripts/teams/', 'tests/MicrosoftTeamsDeliveryTests/'))]
    pinned += ['.github/workflows/module065-teams-delivery-ci.yml']
    for name in pinned:
        require(git('rev-parse', f'{FIRST}:{name}').strip() == git('rev-parse', f'HEAD:{name}').strip(), 'Functional acceptance source changed: ' + name)
    for name in changed:
        require((ROOT / name).read_bytes() == subprocess.check_output(['git', '-C', str(ROOT), 'show', f'HEAD:{name}']), 'Working source drift: ' + name)
    Path(output).write_text('\n'.join(sorted(FILES)) + '\n')
    print('TEAMS_APPLICATION_SCOPE=PASS; DEFAULT_API_FRAMING=UNCHANGED; DEPLOYMENT_CONTROLLERS=UNCHANGED')

if __name__ == '__main__':
    try:
        require(len(sys.argv) == 2, 'Provide the workflow scratch allowlist path')
        main(sys.argv[1])
    except (RuntimeError, subprocess.SubprocessError, OSError) as error:
        print('STOP: ' + str(error), file=sys.stderr)
        raise SystemExit(1)
