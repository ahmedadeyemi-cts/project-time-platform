"""Exact Private generation release registration; no deployment or permission changes."""
from pathlib import Path
import hashlib, os, subprocess
BASE = 'a851956b6babaa085fe124ff17846bea922613e9'
EXPECTED = {'.github/workflows/celar-ai-oracle-gitops-ci.yml': '01758bbcc936cb8cbe13b37eceba1fee7be12e28fb073c7f786a1c3b3e4cf573', '.github/workflows/flowhive-psa-release-control-ci.yml': '7bbad69ebb928867a10266ffb38227c18f607567f190f02e2039583cbbcca1a1', '.github/workflows/group5-financial-operations-recovery-ci.yml': '1448de7c29b27b7674d317fc2d20b03208cb5002c5da84b6b4fdcf4f60a9c0b5', '.github/workflows/module033-project-forge-ci.yml': '27e1d4fc2804288091873bf344e532e3144a9d0ae3d28e5ef3693888c2f9f173', '.github/workflows/modules010065-microsoft-runtime-ci.yml': '6bcca4e2362f03f1674eaf136207c075167501b1eaa3764b198f0471e233bd07', '.github/workflows/projectpulse-deploy-test.yml': '1cc5edea1386ca586f7fc91b072950559611758ff8b38d7a66f5a1f0b279efc0', '.github/workflows/pulse-ai-help-chat-usability-ci.yml': 'd4ca285fed9153246668559672530428a2f2c2221cadb90a18ffb4f3f3e5afc4', 'deployment/oracle-celar/gateway/wsgi.py': 'bfbba74732121741373085f9d0c09f2bd4db08af539d2d9c036af1c77b55ca9e', 'deployment/oracle-celar/release.json': '951d3fa63929882978e759805d8af46cfb15043339195041be1f8da19dbd306c', 'docs/releases/2026-09-19-module025-private-deadlines.md': '30c0ab32f87518a0cfdbefbe15ca23f1af07a19bc192595a58e5a798a6141a3b', 'scripts/ci/validate-celar-ai-enterprise-source-boundary.sh': '65fc6ad4bd351d49f8122453d9fe5b92c48832002b887f9711ede698f6e2f762', 'scripts/ci/validate-module030-source-boundary.sh': 'fa57cb35dcba3426eebed6eee61f984d04beb2af41f118c807c125343a37bf42', 'scripts/release-test/run-module025-installed-sa-uat.py': 'edc5015d67a97880679ec908bef3fc06751db8f6bd159f1390463919320c77e2', 'scripts/release-test/validate-module025-governed-release.sh': 'd7d81027e5bf844a92e19b694d838d1e14f2f38e914e5a6708674a52ab1fb32b', 'scripts/release-test/validate-protected-test-controller-branches.sh': '141defa11e11f5b1e560a15662e1c9bc6aafb752367801ef4282c885dc3d73f9', 'scripts/release-test/verify-oracle-sow-runtime.py': '3c5173331af01ee09d2ba3ed9b1a15837583a0a9c27e2b7c90d8359a33d71b93', 'src/backend/ProjectTime.Api/Ai/CelarAiCapabilityRouting.cs': '978922aaebc50b6597c903d2fb107ca0b1932084cb3ed9ff2bf5836aa737c5c4', 'src/backend/ProjectTime.Api/Ai/Module025GenerationEngine.cs': '2f613cffa63430e320b21eb04d067524459d30d950535f6c8e343af9339b651e', 'src/backend/ProjectTime.Api/Ai/PulseAiPrivateModelClient.cs': 'c31e1a30b275e837e1548dbb54e31785a948a88933a5b36c7c99693a88a2219c', 'src/backend/ProjectTime.Api/Ai/PulseAiPrivateRagService.cs': '1a8ee214416f83b104438e9444027e33d66ff63c2eb023d1936c3b6023b9c1bf', 'tests/FlowHiveDetailedPlannerTests/Module025GenerationEngineTests.cs': '6901bb1cee2ad8d404c9f608eb787a5c3518c81b932eb486cead9dbb835a7a9d', 'tests/flowhive-psa-admission.test.mjs': '3c4a153ae3bf81a2fb35b3128641a29ffea6b103cd2d2d899e4a7e9313730f29', 'tests/test-celar-sow-runtime-deadlines.py': '4b9a1e02849f8401a6d4d90ae09fdcfca1190567c5ac1ed0f9df2b9c408d1ba5', 'tests/test-oracle-runtime-preflight.py': '0c2421356baf19e5f595284b765c577b7db5286bdb05757e210f4f5592f28461', 'tests/validate-celar-ai-pr630-consolidated.mjs': 'e0ce0ae6b528468d80c090dd5aefce11f79fdacb2c0467dc33e622ff0ec97a27'}
SELF = "tests/module025-private-generation-scope.py"
def validate_files(actual):
    assert set(actual) == set(EXPECTED) | {SELF}, "Private generation release contains missing or unexpected files"
def validate_bytes(name, content):
    assert hashlib.sha256(content).hexdigest() == EXPECTED[name], f"Private generation release content changed: {name}"
def main():
    branch = os.environ.get("GITHUB_HEAD_REF") or os.environ.get("GITHUB_REF_NAME")
    assert branch == "fix/module025-private-generation-recovery", "Not the registered Private generation candidate"
    root = Path(__file__).resolve().parents[1]
    os.chdir(root)
    def git(*args): return subprocess.check_output(["git", *args])
    assert git("merge-base", BASE, "HEAD").decode().strip() == BASE, "Wrong release base"
    controller = '.github/workflows/projectpulse-deploy-test.yml'
    original = git('show', BASE + ':' + controller).decode()
    assert Path(controller).read_text() == original.replace('        timeout-minutes: 35', '        timeout-minutes: 50').replace("MODULE025_GENERATION_TIMEOUT_SECONDS: '1500'", "MODULE025_GENERATION_TIMEOUT_SECONDS: '2520'"), 'Controller changes exceed acceptance timing'
    validate_files(git("diff", "--name-only", BASE).decode().splitlines())
    for name in EXPECTED: validate_bytes(name, Path(name).read_bytes())
    # Exercise rejection of unexpected/missing files and changed payloads.
    def rejected(fn):
        try: fn()
        except AssertionError: return
        raise AssertionError("Negative scope test did not reject")
    exact = set(EXPECTED) | {SELF}
    rejected(lambda: validate_files(exact | {".github/workflows/projectpulse-deploy-production.yml"}))
    for name in exact: rejected(lambda: validate_files(exact - {name}))
    name = next(iter(EXPECTED))
    rejected(lambda: validate_bytes(name, Path(name).read_bytes() + b"unexpected"))
    print(f"PRIVATE_GENERATION_RELEASE_SCOPE=PASS files={len(exact)} negative_tests={len(exact)+2}")
if __name__ == "__main__": main()
