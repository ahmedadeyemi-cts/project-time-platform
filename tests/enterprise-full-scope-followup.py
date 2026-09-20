"""Exact PR1115 application repair registration; does not grant deployment authority."""
from pathlib import Path
import hashlib, os, subprocess
BASE = 'fc10591e4550243603b84af1a42153634228972d'
SELF = 'tests/enterprise-full-scope-followup.py'
EXPECTED = {'.github/workflows/modules010065-microsoft-runtime-ci.yml': '4498ca798fd46779996ab2c0ddb9efeff22f760b6522b61abf27a9be2b04c84d', 'docs/repairs/enterprise-full-scope-followup-20260920.md': '5dc4511db47b2a031d3ef4bf5a30698cbfe7804d65e9863aef03c8ee96f9bbba', 'scripts/release-test/validate-module025-governed-release.sh': 'ac135df0706668e71439a64631b6e11cf92d035042c11174831e5ef6abc17e62', 'scripts/release-test/validate-protected-test-controller-branches.sh': 'a8e5f688406e0cd05e1a09466facb76844ea93aac36282e45fd9448ad1a6349f', 'src/backend/ProjectTime.Api/Modules/ContractsPrepaidModule.cs': '02964df420a710ce75c221324d1aed6c063215bbe86bf7efcf7c19ec0c752dae', 'src/backend/ProjectTime.Api/Modules/EnterpriseNotificationRecipientResolver.cs': 'f652ab667ec9c5f94dc7a9ee3fea1302ee0bd3521cc2b758f94a622c5c31cd04', 'src/backend/ProjectTime.Api/Modules/ModuleAvailabilityModule.cs': 'fac0ed433386403418cfa33c6afe20bff028307c460094bd9633988b4bcfa511', 'src/backend/ProjectTime.Api/Modules/ProjectBudgetAssessment.cs': '675891aae4a1df7ae99c4aa112f58eb3951994bb7d2384d84982f33c54099115', 'src/backend/ProjectTime.Api/Modules/ProjectFinancialTruthModule.cs': 'd86c1fa9a3f061b0372b0a09a97bac871c89871de1880816eb24fd57f5877501', 'src/backend/ProjectTime.Api/Modules/ProjectNotificationFinancialSnapshotLoader.cs': 'e250775d0893790dda46fdc02d436490758bfd4baf32aef70308b9803e843183', 'src/backend/ProjectTime.Api/ProjectFinancialTruthModule.g.cs': '285c531fefa2af95ae86b912a24959922c378184d5bb1aea5d23b020d84ad582', 'src/frontend/project-time-web/src/App.jsx': 'ed2b4f434a71f8207770d80e899173c1e1cdd0ab610f7633793b6acf4b258ada', 'src/frontend/project-time-web/src/CostOverrunAlertCenter.jsx': 'db70bfa45be6e6ed1c0d24d19f7bcbf0ecf4581bf18bdc01b9b48405e7cc19f5', 'src/frontend/project-time-web/src/CustomerSourceAuthorityPortal.jsx': 'f24515f0908812142a505b5773177d39dcce09f9fcf73648c462a5743aa5dcb0', 'src/frontend/project-time-web/src/InvoiceBillingCenter.jsx': 'b2b6c680cb083582bff052b2302c9d142f12e449e0e0037150a422f79e7be54a', 'src/frontend/project-time-web/src/RateCardAdministrationCenter.jsx': '71b25ac2d94adeb389700bc3e9659c3fe90f9652249f2ac89651027477329bfc', 'src/frontend/project-time-web/src/UnifiedProjectFinancialWorkspace.jsx': 'a77a459f91336fafcda59e3ad2c5e818700db90dc67cecba6ebfd214c0e3111a', 'src/frontend/project-time-web/src/WorkIntakeCreationCenter.jsx': 'd88efbb1cb5c376ac32028fd51e965bd966b25f247ea3b9cf72911a336f357d1', 'src/frontend/project-time-web/src/enterprise/SalesDeliveryWorkflowCenter.jsx': 'af43dc25edcddaad0294ef7b10e366a288ccdf15d3d0497b7d631fdf047b7325', 'src/frontend/project-time-web/src/invoice-billing-enhancements.css': '4d31821ee70ec383ef6ad31dd56673bc33ba4c803ff3a8abfcb9789c2c57442f', 'src/frontend/project-time-web/src/microsoft-integration-portal.css': '6b339fa1fbf04d61d4dc864fc02710fdf0f51269174639bad2076bbffbf90930', 'src/frontend/project-time-web/src/project-financial-portfolio.js': '6cdd4781f82d58fc78012d7b56b8610ace3b223ad5562b3813fb121c6d622ea9', 'tests/ModuleAvailabilityHttp/ModuleAvailabilityHttp.csproj': 'e05367cf0645986353500109352897243d19f8ab9db50a9ebf4677eb0e334362', 'tests/ModuleAvailabilityHttp/Program.cs': '7ff3fc825506b4685d37755cf9c94c899efb1a541c310c83d20338bdae1ebafd', 'tests/ProjectBudgetAssessment/Program.cs': '636681ce79f0d99267fe5b7218154b738e2b620bb77ddb62217b5114702bbadf', 'tests/ProjectBudgetAssessment/ProjectBudgetAssessment.csproj': '7f1dd59dbd0034aea80d1085e9a66acf0d9c965c94a6242b3a2375b6a3737f87', 'tests/project-financial-portfolio.test.mjs': '2ec54f7be295f1e7b143e6d0d132931de4903a28a79486241bacc288348dbc4c', 'tests/validate-systemwide-enterprise-reliability.mjs': 'b42328a0f6a99b2b2117262872a6d4e5943dd746cfbb70bbed3a82f18d0d3b86'}

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
    assert branch == 'fix/complete-module-repairs-20260920', 'Unregistered branch'
    if os.environ.get('PR_NUMBER'): assert os.environ['PR_NUMBER'] == '1115'
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
