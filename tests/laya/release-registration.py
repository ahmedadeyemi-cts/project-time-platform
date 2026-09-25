"""Content-bound release ownership for the document-admission repair.

This validates source, not approval. Native reviews, environment protection,
all owning module contracts, builds, migration tests, and live UAT still apply.
The application never receives permission to edit protected release sources.
"""
from pathlib import Path
import argparse
import hashlib
import os
import subprocess

ROOT = Path(__file__).resolve().parents[2]
CONTROL = 'control/pr1153-source-registration-20260923'
APPLICATION = 'fix/automatic-document-admission-laya-20260923'
REVIEW_BASE = '14f9850a12868ed7f821fb96d2edf715a3ab92b1'
RELEASE_PATCH = 'e2315413b61cc0893c3771b40f428b6fc61bd53f'
CORE = frozenset('''
.github/workflows/projectpulse-deploy-test.yml
database/migrations/125_automatic_document_admission_laya.sql
docs/production-readiness/foundation/initialization-review.json
scripts/release-test/build-and-run-project-planning-document-authority-migration-job.sh
tests/module064-migration-rollout.test.py
'''.split())
# Exact complete contents of the small, additive dispatch-table edits. These
# seals include all pre-existing branches, owner checks, build steps and guards;
# they are not hashes of a list of filenames.
GATE_BLOBS = {
    '.github/workflows/module-management-owner-drawer-ci.yml': '507ba3b7eb03a2c79f5f313bc41a6dd82bb80d01',
    'scripts/ci/validate-celar-ai-enterprise-source-boundary.sh': 'bea9c4d14e76e19ca8308c256ad83ad7d07c861a',
    'scripts/release-test/validate-protected-test-controller-branches.sh': '425e702b13cfe982fa23db2274c5c3864f537dbc',
    '.github/workflows/flowhive-psa-release-control-ci.yml': 'a66a3c7f67cac18fc8e45df372f4013b5b888bb7',
    '.github/workflows/flowhive-enterprise-psa-ci.yml': 'b3f63304b3a7d6e322c56669cd848ab403187b0a',
    '.github/workflows/celar-laya-integration.yml': 'fbebf7749df46e7387a3313e021ecf82dac5309e',
    '.github/workflows/pr1140-uat-recovery-ci.yml': '5932050759784b19aae136c0cde337fcb87e0c34',
    '.github/workflows/pr1151-uat-supersession-ci.yml': '8f99a4ba72d802d95b183265d26ef8c6b8f97f7f',
    '.github/workflows/uat-migration-throttle-recovery-ci.yml': '0ed9a29b7aeca0b0f516160d4e11aedd1e358b4a',
    'tests/flowhive-psa-release-workflow.test.py': '9ed0b01edd404d03598518ad226d23fe74bb48d5',
    'tests/pr1140-uat-recovery-scope.py': 'd164cfd1059b4d928a176d143553d0f570d81f7c',
    'tests/test-pr1140-uat-recovery.py': '72999c53daefa3efe4b23b9d52902baf944bdc5b',
    'tests/test-pr1151-uat-supersession.py': 'd33b2ee8548c5dc3eccc2c33acff93e8b01c224d',
    'scripts/release-test/verify-module025-quarantine-controller.py': '84263240e4bc8b41777e7958a4af6dbdb70fff12',
    'tests/laya/test_release_registration.py': '5d2ab30f6a7d1d7b0669f480d71b6321dc3817b9',
}
FIXTURE = 'tests/laya/release-admission-fixture.py'
FIXTURE_BLOB = 'bc84334a045f396738b54f60cb688a2ba13424c6'
SELF = 'tests/laya/release-registration.py'
CONTROL_FILES = CORE | set(GATE_BLOBS) | {FIXTURE, SELF}
APPLICATION_FILES = frozenset('''
.github/workflows/laya-processed-source-ci.yml
docs/implementation/automatic-document-admission-laya-20260923.md
src/backend/ProjectTime.Api/Ai/LayaAutomaticClassificationRepository.cs
src/backend/ProjectTime.Api/Ai/LayaAutomaticClassificationWorker.cs
src/backend/ProjectTime.Api/Ai/LayaProcessedSourceReader.cs
src/backend/ProjectTime.Api/Ai/LayaWorkerLease.cs
src/backend/ProjectTime.Api/Ai/ProjectPulseAiServiceCollectionExtensions.cs
src/backend/ProjectTime.Api/Ai/PulseAiDocumentIndexAuthorization.cs
src/backend/ProjectTime.Api/Ai/PulseAiPrivateDocumentRuntimeRepository.cs
src/backend/ProjectTime.Api/Ai/PulseAiPrivateDocumentRuntimeService.cs
src/backend/ProjectTime.Api/Ai/PulseAiPrivateRuntimeSourceResolver.cs
src/backend/ProjectTime.Api/Modules/LayaDecisionModule.cs
src/backend/ProjectTime.Api/Modules/ProjectIntakeModule.cs
src/backend/ProjectTime.Api/Modules/ProjectPlanningDocumentPreparation.cs
src/frontend/project-time-web/src/ai/LayaDecisionPanel.jsx
src/frontend/project-time-web/src/ai/laya-processing-state.js
src/frontend/project-time-web/tests/laya-processing-state.test.mjs
tests/FlowHivePreparationTests/Program.cs
tests/LayaLeaseTests/LayaLeaseTests.csproj
tests/LayaLeaseTests/Program.cs
tests/LayaProcessedSourceTests/Fakes.cs
tests/LayaProcessedSourceTests/LayaProcessedSourceTests.csproj
tests/LayaProcessedSourceTests/Program.cs
tests/LayaProcessedSourceTests/fixture.sql
tests/LayaIndexAuthorizationTests/LayaIndexAuthorizationTests.csproj
tests/LayaIndexAuthorizationTests/Program.cs
tests/laya/backend/Fakes.cs
tests/laya/backend/Program.cs
tests/laya/processed-source-scope.py
tests/laya/release-scope.py
'''.split())
IMMUTABLE = frozenset('''
scripts/release-test/recover-pr1139-uat-orphan.py
scripts/release-test/recover-pr1140-uat-orphan.py
scripts/release-test/recover-pr1140-migration-retry-orphan.py
scripts/release-test/verify-pr1151-uat-supersession.py
tests/flowhive-psa-admission.test.mjs
.github/CODEOWNERS
.github/flowhive-psa-protected-test-candidate.json
.github/flowhive-psa-protected-cutover.json
.github/flowhive-psa-release-control-files.txt
.github/workflows/module025-protected-uat-control.yml
.github/workflows/flowhive-psa-protected-test-admission.yml
scripts/release-test/dispatch-flowhive-psa-test.mjs
scripts/release-test/flowhive-psa-admission.mjs
src/backend/ProjectTime.Api/Ai/LayaDecisionContract.cs
src/backend/ProjectTime.Api/Ai/LayaDecisionTransport.cs
src/backend/ProjectTime.Api/Modules/AiProviderConfigurationModule.cs
'''.split())


def require(ok, message):
    if not ok:
        raise AssertionError(message)


def git(*args):
    return subprocess.check_output(['git', '-C', str(ROOT), *args])


def git_blob(content):
    return hashlib.sha1(b'blob ' + str(len(content)).encode() + b'\0' + content).hexdigest()


def verify_bytes(path, actual, expected):
    require(actual == expected, 'Unreviewed protected content changed: ' + path)


def verify_scope(changed, allowed, exact):
    require(bool(changed), 'Empty source registration cannot authorize a release')
    require(not changed - allowed, 'Unexpected source paths: ' + ','.join(sorted(changed - allowed)))
    if exact:
        require(changed == allowed, 'The control repair is missing part of its reviewed content inventory')


def branch():
    return os.environ.get('GITHUB_HEAD_REF') or git('branch', '--show-current').decode().strip()


def context():
    require(os.environ.get('GITHUB_REPOSITORY', 'ahmedadeyemi-cts/project-time-platform') ==
            'ahmedadeyemi-cts/project-time-platform', 'Unexpected repository')
    require(os.environ.get('GITHUB_BASE_REF', 'main') == 'main', 'Expected the main PR target')
    base = git('merge-base', 'origin/main', 'HEAD').decode().strip()
    git('merge-base', '--is-ancestor', REVIEW_BASE, base)
    rows = git('diff', '--name-status', base, 'HEAD').decode().splitlines()
    require(all(row.split('\t')[0] in {'A', 'M'} for row in rows), 'No removal, rename, or type change is admitted')
    return base, {row.split('\t', 1)[1] for row in rows}


CANONICAL_REVIEW_ENTRY = {'path': 'database/migrations/125_module025_canonical_references.sql', 'sha256': '8ffcada76170f9447c090cf2009c14c4c9cd0517e515dfd83e9b5a852abac070', 'signals': ['data_mutation'], 'review_default': 'review_required', 'decision': 'review_required', 'rationale': 'Tracks the canonical-reference SQL already merged in PR1159. This entry is review inventory, not execution or production approval.'}
CATALOG_PATH = 'docs/production-readiness/foundation/initialization-review.json'


def expected_core(path):
    original = git('show', RELEASE_PATCH + ':' + path)
    if path != CATALOG_PATH:
        return original
    import json
    catalog = json.loads(original)
    require(all(row['path'] != CANONICAL_REVIEW_ENTRY['path'] for row in catalog['scripts']),
            'Canonical catalog entry must be a single additive registration')
    catalog['scripts'].append(CANONICAL_REVIEW_ENTRY)
    catalog['scripts'].sort(key=lambda row: row['path'])
    return (json.dumps(catalog, indent=2) + '\n').encode()


def check_core():
    git('merge-base', '--is-ancestor', RELEASE_PATCH, 'HEAD')
    for path in CORE:
        verify_bytes(path, (ROOT / path).read_bytes(), expected_core(path))
    canonical_source = git('show', '82e54cd8b7eefaac04f9df77162504a4c628a104:' + CANONICAL_REVIEW_ENTRY['path'])
    verify_bytes(CANONICAL_REVIEW_ENTRY['path'], (ROOT / CANONICAL_REVIEW_ENTRY['path']).read_bytes(), canonical_source)
    require(hashlib.sha256(canonical_source).hexdigest() == CANONICAL_REVIEW_ENTRY['sha256'],
            'Merged canonical SQL identity changed')
    for path, expected in GATE_BLOBS.items():
        require(git_blob((ROOT / path).read_bytes()) == expected,
                'Control wiring differs from its complete reviewed contents: ' + path)
    require(git_blob((ROOT / FIXTURE).read_bytes()) == FIXTURE_BLOB,
            'Historical fixture adapter changed outside this reviewed content')
    # The metadata is a review requirement, never a fabricated approval.
    import json
    catalog = json.loads((ROOT / 'docs/production-readiness/foundation/initialization-review.json').read_text())
    def items(value):
        if isinstance(value, dict):
            yield value
            for nested in value.values():
                yield from items(nested)
        elif isinstance(value, list):
            for nested in value:
                yield from items(nested)
    row = next(entry for entry in items(catalog)
               if entry.get('path') == 'database/migrations/125_automatic_document_admission_laya.sql')
    require(row.get('decision') == 'review_required' and row.get('review_default') == 'review_required',
            'Source registration must not claim independent SQL review approval')


def check_immutable(base, paths):
    for path in paths:
        verify_bytes(path, (ROOT / path).read_bytes(), git('show', base + ':' + path))


def check_control():
    require(branch() == CONTROL, 'Release content is admitted only on its independent control PR')
    base, changed = context()
    verify_scope(changed, CONTROL_FILES, exact=True)
    check_core()
    check_immutable(base, IMMUTABLE)
    git('diff', '--check', base, 'HEAD')


def check_application():
    require(branch() == APPLICATION, 'Unexpected application repair branch')
    base, changed = context()
    # The accepted base must already contain migration125 and its release wiring.
    # The application cannot self-authorize any protected-file difference.
    require(git('show', base + ':database/migrations/125_automatic_document_admission_laya.sql') ==
            git('show', RELEASE_PATCH + ':database/migrations/125_automatic_document_admission_laya.sql'),
            'The independently reviewed release dependency is not incorporated in main')
    verify_scope(changed, APPLICATION_FILES, exact=False)
    check_immutable(base, IMMUTABLE | CORE | set(GATE_BLOBS) | {FIXTURE, SELF})
    git('diff', '--check', base, 'HEAD')


def check_protected(paths):
    require(branch() == CONTROL, 'Application branches cannot alter a protected deployment surface')
    require(paths and set(paths) <= CORE, 'Protected changes exceed the content-bound release proposal')
    check_control()
    # Re-evaluate every requested path. A filename match never establishes safety.
    for path in paths:
        verify_bytes(path, (ROOT / path).read_bytes(), expected_core(path))


def self_test():
    def rejected(action, name):
        try:
            action()
        except AssertionError:
            return
        raise AssertionError('Negative registration test accepted ' + name)
    require('.github/workflows/projectpulse-deploy-test.yml' not in APPLICATION_FILES,
            'Application inventory includes protected deployment ownership')
    require(not (APPLICATION_FILES & set(GATE_BLOBS)), 'Application may not change its own persistent release gates')
    rejected(lambda: verify_scope({'unexpected.txt'}, CONTROL_FILES, True), 'unrelated source')
    rejected(lambda: verify_scope(CONTROL_FILES - {FIXTURE}, CONTROL_FILES, True), 'partial control patch')
    rejected(lambda: verify_scope(APPLICATION_FILES | {'.github/workflows/projectpulse-deploy-test.yml'}, APPLICATION_FILES, False),
             'application deployment edit')
    rejected(lambda: verify_bytes('same-approved-name', b'environment: production', b'environment: test'),
             'changed bytes behind the same approved filename')
    rejected(lambda: verify_bytes('same-approved-name', b'can_admins_bypass: true', b'can_admins_bypass: false'),
             'native protection mutation')
    rejected(lambda: verify_bytes('same-approved-name', b'ON_ERROR_STOP=0', b'ON_ERROR_STOP=1'),
             'private migration failure bypass')
    require(all('*' not in path and '..' not in path and not path.startswith('/') for path in CONTROL_FILES | APPLICATION_FILES),
            'Unsafe or wildcard source inventory')
    print('RELEASE_REGISTRATION_NEGATIVE_TESTS=PASS; approval_not_asserted=true')


def main():
    parser = argparse.ArgumentParser()
    mode = parser.add_mutually_exclusive_group(required=True)
    mode.add_argument('--control', action='store_true')
    mode.add_argument('--application', action='store_true')
    mode.add_argument('--protected-list')
    mode.add_argument('--self-test', action='store_true')
    args = parser.parse_args()
    self_test()
    if args.control:
        check_control()
    elif args.application:
        check_application()
    elif args.protected_list is not None:
        check_protected(args.protected_list.splitlines())
    print('DOCUMENT_ADMISSION_SOURCE_OWNERSHIP=PASS; deployment_performed=false; native_review_required_unchanged=true')


if __name__ == '__main__':
    main()
