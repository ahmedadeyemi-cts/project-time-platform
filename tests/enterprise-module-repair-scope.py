"""Exact PR1111 application repair registration; does not grant deployment authority."""
from pathlib import Path
import hashlib, os, subprocess
BASE = '7cd57991450de3fca26dafd2e8961bb231d404f0'
SELF = 'tests/enterprise-module-repair-scope.py'
EXPECTED = {'.github/workflows/modules010065-microsoft-runtime-ci.yml': 'd15959bd0d1f9d5c7282580c4b7864b8cec00c332835b78819b7b96bf2e5b1b5', 'docs/repairs/enterprise-module-review-20260919.md': '2478f08d5677b6aaedb9398d66d7913904a2400b08f29419d0451a8461a6eabc', 'scripts/release-test/validate-module025-governed-release.sh': '7eaf4a6e2fd044983d1069b0cbdc43b695c9be423af1b985018c4716858e8556', 'scripts/release-test/validate-protected-test-controller-branches.sh': '0d4a17ea09aa124d93a4481f6a4f621347f7024e4267d3d292306860e7265a9c', 'src/backend/ProjectTime.Api/Modules/ContractsModule.cs': 'c08d706d92abf6f30d003247a4cc531265dbdfcb1201b14f885ccea50d43fd57', 'src/backend/ProjectTime.Api/Modules/ContractsPrepaidModule.cs': '8cf9d246c635e279645c5f53b2009feecd2a2072d83d87975011d06cb67eaf29', 'src/backend/ProjectTime.Api/Modules/EnterpriseNotificationRecipientResolver.cs': 'c4052782ca9cb00c49443d5279d8fc79eb1a0bd3a9ec26bd05e1785fdfac3d95', 'src/backend/ProjectTime.Api/Modules/MicrosoftMailTransportTestModule.cs': '6cfb01a08f73e50f1bbf31c701a175eb7906735f89610b426c1cebdc62ca1d7b', 'src/backend/ProjectTime.Api/Modules/ProjectFinancialTruthModule.cs': '7fd9dbfc4792ef70f35ed7bd9079943d8ea1ea025a35feac168c49a755b3ca7d', 'src/backend/ProjectTime.Api/ProjectFinancialTruthModule.g.cs': '5f5d88f3d309a101cbc251fc733a0dfbb27ad3c5825151cd5254f661d898922f', 'src/frontend/project-time-web/scripts/generate-module-001-integrated-app.mjs': '625706a3aae1cb8492992669fddde325d8f6ed9f02f6b249a95372720a5e8a22', 'src/frontend/project-time-web/scripts/validate-celar-ai-production-readiness.mjs': '635cd9c2853d6f80a904462b647497749ead0d41f63e5843bf873cf9a479d5f2', 'src/frontend/project-time-web/src/App.jsx': '7b79a74b75fc86f6ccee2ebc2dce9f733a3fee1c36ab35342dfaaf8c3d267d8e', 'src/frontend/project-time-web/src/BillingReadinessCenter.jsx': 'a04bb2091e3d2490e3bafc56e48b486dd5ea48e0843560f2361e6a47069412c6', 'src/frontend/project-time-web/src/CelarAiCapabilityRoutingPanel.jsx': '0b72b79a1b18b374e2c46d62fbd4b60c80280527c0a51a6665af73fa31d476f9', 'src/frontend/project-time-web/src/CostOverrunAlertCenter.jsx': 'c4dfb0ba21390e8d8d80e2dadd37a1c6c28bb1bdeec8a5509e0c37e40911b5d6', 'src/frontend/project-time-web/src/CustomerDirectoryCenter.jsx': '9df73df14019c3bdd7021b0051342a22466967162eef9167012e7ed2628e96b3', 'src/frontend/project-time-web/src/CustomerSourceAuthorityPortal.jsx': 'c1bb9017a08676de9aec479719685397d0fdbcb50fba24d92bcdc77ad8b62955', 'src/frontend/project-time-web/src/InvoiceBillingCenter.jsx': '8ab631924977c755c517ae5ea0bdae15eb3f72a4df2925d874072a201e63f051', 'src/frontend/project-time-web/src/MicrosoftMailTransportReadinessPanel.jsx': '6b313bf473a37804b68e3fe368923fd17d5a8d7e9ea21812207d6c7c9fe06cbf', 'src/frontend/project-time-web/src/RateCardAdministrationCenter.jsx': 'd4b1a6ed7af42a822c20a926048828cc50ef539a98fda231be133b7fcb2796dc', 'src/frontend/project-time-web/src/SalesInsightsDashboard.jsx': 'e6cbb27b79a7096f6db99e454961c760cef6f20d2466191e729a7a33836076c5', 'src/frontend/project-time-web/src/UnifiedProjectFinancialWorkspace.jsx': '94697b11c06b8a4b7a1e8a3942039443f609d66a7a1b560006b9269146993617', 'src/frontend/project-time-web/src/customer-source-authority.css': '281a24c1e826bd5a2cb19d1f5ee83d9605449b5e945a28fe5ac09aad43b0fcb9', 'src/frontend/project-time-web/src/enterprise/SalesDeliveryWorkflowCenter.jsx': 'a90242f7416f0310f21fca12ef471262e92d4ca5b4cca33156bc528b49320b81', 'src/frontend/project-time-web/src/main.jsx': '82894fdc3ab9b9e5db0f5ac8cb0234ebf3c6586db8f3592e44a86b06136d4563', 'src/frontend/project-time-web/src/module001/timesheet-draft-writer.js': '1b4ac4e636c00403b713630a8045ea8d363252a2e4aae65ad59de12207c6045b', 'src/frontend/project-time-web/src/styles.css': 'e85521489aaa170174e7b203412638b60dd26bbc62b591cb0c2801cbb31f29ca', 'src/frontend/project-time-web/vite.config.js': '0f7dddfb204e2109205477f4457887efebe8a03f43adf139c1375c590b925627', 'tests/timesheet-autosave-integration.test.mjs': 'f44c388e7463132ff10d5cbdb3ebf93e82156f7a7af781d71523f8c9a04eb8ab', 'tests/timesheet-draft-writer.test.mjs': '4e465ead5501c692905e3be60d13cd8cf42b1aa6e548774c9d09110d9a8b3118'}

def check_files(actual):
    assert len(actual) == len(set(actual)), 'Duplicate file'
    assert set(actual) == set(EXPECTED) | {SELF}, 'Missing or unexpected repair file'
def check_bytes(name, value):
    assert hashlib.sha256(value).hexdigest() == EXPECTED[name], 'Repair content changed: ' + name
def rejected(action):
    try: action()
    except AssertionError: return
    raise AssertionError('Negative scope test failed')
def main():
    os.chdir(Path(__file__).resolve().parents[1])
    git = lambda *args: subprocess.check_output(['git', *args], text=True).strip()
    branch = os.environ.get('GITHUB_HEAD_REF') or os.environ.get('GITHUB_REF_NAME')
    assert branch == 'fix/enterprise-module-repairs-20260919', 'Unregistered branch'
    if os.environ.get('PR_NUMBER'): assert os.environ['PR_NUMBER'] == '1111'
    assert git('merge-base', BASE, 'HEAD') == BASE, 'Wrong base'
    assert git('merge-base', 'origin/main', 'HEAD') == BASE, 'Main advanced; revalidate'
    check_files(git('diff', '--name-only', BASE).splitlines())
    for name in EXPECTED: check_bytes(name, Path(name).read_bytes())
    actual = list(EXPECTED) + [SELF]
    for name in actual: rejected(lambda: check_files([p for p in actual if p != name]))
    for name in ['.github/workflows/projectpulse-deploy-test.yml', '.github/workflows/projectpulse-deploy-production.yml', 'database/migrations/unreviewed.sql']:
        rejected(lambda: check_files(actual + [name]))
    for name in EXPECTED: rejected(lambda: check_bytes(name, Path(name).read_bytes() + b'changed'))
    assert git('diff', '--name-only', BASE, '--', 'deployment', 'database', '.github/workflows/projectpulse-deploy-test.yml', '.github/workflows/projectpulse-deploy-production.yml', '.github/flowhive-psa-protected-test-candidate.json') == '', 'Deployment authority must remain unchanged'
    subprocess.run(['git', 'diff', '--check', BASE], check=True)
    print(f'ENTERPRISE_REPAIR_EXACT_SCOPE=PASS files={len(actual)} deployment_authority=unchanged')
if __name__ == '__main__': main()
