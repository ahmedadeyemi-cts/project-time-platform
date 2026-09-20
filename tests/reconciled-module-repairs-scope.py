"""Exact reconciled module application repair registration; does not grant deployment authority."""
from pathlib import Path
import hashlib, os, subprocess
BASE = '18f192c9d3737e630308321e8067dedfc580f0e9'
SELF = 'tests/reconciled-module-repairs-scope.py'
EXPECTED = {'.github/workflows/enterprise-completion-ci.yml': '374de9c2e1504903ea33c991ab3433bcb320b677e210bf0d5413b1f26bbef982', '.github/workflows/modules010065-microsoft-runtime-ci.yml': 'be2d76541be5c79f406964e46bd23059c1d3dc47d4c717e44bbc1a5d0d71511c', 'docs/repairs/reconciled-module-repairs-20260920.md': '25acea4a2ccd3b4a73a1ae4072d85973aa62519b173bd459282de5000bced501', 'scripts/release-test/validate-module025-governed-release.sh': 'd6572e9dd4c3f1c640ad89ab3012310725cea0261247850859900d013687ca38', 'scripts/release-test/validate-protected-test-controller-branches.sh': 'f173e8b66cbabcd6461927fbfd23143cfa4eea8107693997099ffc5b34f7185b', 'src/backend/ProjectTime.Api/Modules/ContractsPrepaidModule.cs': '279d9d28c55518a5ba20b01fb664bcacf2bbaa2d0d7afa76e3d56e256bbdf014', 'src/backend/ProjectTime.Api/Modules/EnterpriseNotificationRecipientResolver.cs': '1b61ef6eb641c23331967d482194cfceef61affca7d4eb1bcdd9d077f75dd713', 'src/backend/ProjectTime.Api/Modules/ModuleAvailabilityModule.cs': 'fac0ed433386403418cfa33c6afe20bff028307c460094bd9633988b4bcfa511', 'src/backend/ProjectTime.Api/Modules/ProjectBudgetAssessment.cs': '675891aae4a1df7ae99c4aa112f58eb3951994bb7d2384d84982f33c54099115', 'src/backend/ProjectTime.Api/Modules/ProjectFinancialTruthModule.cs': '030c7397f4ffc87b0cd0da44ab3765a3cafe9f3ab9091bf129b86dc9d56287b7', 'src/backend/ProjectTime.Api/Modules/ProjectNotificationFinancialSnapshotLoader.cs': '430ed8000a40bdb66841e46ed45a4f57cc709d09eac0a782f16d678534d27f57', 'src/backend/ProjectTime.Api/ProjectFinancialTruthModule.g.cs': '3eb4737fb70835af05f02a0bc2fcd619444aa5d6b54ecbe320e6f172376056cc', 'src/frontend/project-time-web/src/App.jsx': 'ed2b4f434a71f8207770d80e899173c1e1cdd0ab610f7633793b6acf4b258ada', 'src/frontend/project-time-web/src/CostOverrunAlertCenter.jsx': '2b27bc019887e5b1db48435da0654960481f8703d07b91a704788e66d739e6ad', 'src/frontend/project-time-web/src/CustomerSourceAuthorityPortal.jsx': 'f24515f0908812142a505b5773177d39dcce09f9fcf73648c462a5743aa5dcb0', 'src/frontend/project-time-web/src/InvoiceBillingCenter.jsx': '54b89ce72d47c36fa70bb9c9d24196766df1537ba398ef5a9cfdbe6cf9e020e6', 'src/frontend/project-time-web/src/RateCardAdministrationCenter.jsx': '71b25ac2d94adeb389700bc3e9659c3fe90f9652249f2ac89651027477329bfc', 'src/frontend/project-time-web/src/UnifiedProjectFinancialWorkspace.jsx': 'a77a459f91336fafcda59e3ad2c5e818700db90dc67cecba6ebfd214c0e3111a', 'src/frontend/project-time-web/src/WorkIntakeCreationCenter.jsx': 'd88efbb1cb5c376ac32028fd51e965bd966b25f247ea3b9cf72911a336f357d1', 'src/frontend/project-time-web/src/enterprise/SalesDeliveryWorkflowCenter.jsx': 'af43dc25edcddaad0294ef7b10e366a288ccdf15d3d0497b7d631fdf047b7325', 'src/frontend/project-time-web/src/invoice-billing-enhancements.css': '4d31821ee70ec383ef6ad31dd56673bc33ba4c803ff3a8abfcb9789c2c57442f', 'src/frontend/project-time-web/src/microsoft-integration-portal.css': '6b339fa1fbf04d61d4dc864fc02710fdf0f51269174639bad2076bbffbf90930', 'src/frontend/project-time-web/src/project-financial-portfolio.js': '6cdd4781f82d58fc78012d7b56b8610ace3b223ad5562b3813fb121c6d622ea9', 'tests/ModuleAvailabilityHttp/ModuleAvailabilityHttp.csproj': 'e05367cf0645986353500109352897243d19f8ab9db50a9ebf4677eb0e334362', 'tests/ModuleAvailabilityHttp/Program.cs': '7ff3fc825506b4685d37755cf9c94c899efb1a541c310c83d20338bdae1ebafd', 'tests/ProjectBudgetAssessment/Program.cs': '636681ce79f0d99267fe5b7218154b738e2b620bb77ddb62217b5114702bbadf', 'tests/ProjectBudgetAssessment/ProjectBudgetAssessment.csproj': '7f1dd59dbd0034aea80d1085e9a66acf0d9c965c94a6242b3a2375b6a3737f87', 'tests/TimesheetNotificationRecipients/Program.cs': '4436a3366e530a4a582466d5c6d6a83e6ad42f41bb139497e88f0dd0282abeb0', 'tests/TimesheetNotificationRecipients/TimesheetNotificationRecipients.csproj': 'e8698f618bf8cc7115e92d8c695a54d68ff423d53f6d4dd5c82db2d29d886bf0', 'tests/project-financial-portfolio.test.mjs': '2ec54f7be295f1e7b143e6d0d132931de4903a28a79486241bacc288348dbc4c', 'tests/validate-systemwide-enterprise-reliability.mjs': 'b42328a0f6a99b2b2117262872a6d4e5943dd746cfbb70bbed3a82f18d0d3b86'}

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
    assert branch == 'fix/reconcile-module-repairs-20260920', 'Unregistered branch'
    if os.environ.get('PR_NUMBER'): assert os.environ['PR_NUMBER'] == '1118', 'Wrong PR'
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
