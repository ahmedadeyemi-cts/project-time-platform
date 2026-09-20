"""Exact Module 064 provider repair scope; no deployment authorization."""
from pathlib import Path
import hashlib, subprocess
BASE = 'ec122f35a04bae088920bfb4320f19142de22c23'
EXPECTED = {'.github/workflows/admin-runtime-stability-ci.yml': 'b3b9321e31959265b52eba5510ea5d39e69bb24c73d7153746c2cd02b324a4e3', '.github/workflows/flowhive-psa-release-control-ci.yml': '5783e65aa7306f333c6b28a7769567671acf061babfe2ca23db067ba7296ebfb', '.github/workflows/module064-automatic-provider-health-ci.yml': '5c3dad5b85f5b1ea19bbe4586200a29d96e8658bd034a45bc6da96535950e5d6', 'scripts/ci/validate-celar-ai-enterprise-source-boundary.sh': '3ba7a7ad441bcc608e2674195f90a8edb0e09efa4a8c9c56375a0c34698a5656', 'scripts/release-test/build-and-run-project-planning-document-authority-migration-job.sh': 'c1349f7b1c22397cbbd0e713cee615a3cc7cecc2727d1e24f989d653178dce31', 'scripts/release-test/validate-protected-test-controller-branches.sh': 'f1d3610b60cf5dea5ce2bded203ab010b7d2c97a5f15517dc324aa460e29941c', 'src/backend/ProjectTime.Api/Ai/ProjectPulseAiHealthMonitor.cs': '4f3c85f4732ff0ab700d9f4cb2a7f485c597b1eb0ba1ccfb304f20071e6a7ba0', 'src/backend/ProjectTime.Api/Modules/AiProviderConfigurationModule.cs': '33bc46fbf0dc90eeb0cbf3bd73efce9a74caee33e22b2b9fa7c837576d0602ec', 'src/frontend/project-time-web/src/AiProviderConfigurationCenter.jsx': 'd4a2882bfe62eb0f76328f39823db9dea9c2d06d5a39ee7e8cb47d86c18c91cf', 'src/frontend/project-time-web/src/CelarAiAvailabilityCard.jsx': 'e69106da20409b0e6e083025fb8162493e451d424681ef5dffe7c7889e97438a', 'tests/EnterpriseCompletionTests/Program.cs': 'b2f28a9f33a14538d173ad6dafc7cf3410015b72dbcf7b01f92e2750291dca4b', 'tests/enterprise-completion-migrations.sh': '22c2130a5c1235fb149259bc30f56ca4e6e1282832ae316b8937ac748c5cabed', '.github/workflows/module-management-owner-drawer-ci.yml': 'd5393d9c65e1d8c3dbe2d7e58bc00c8e1cacdf8782b9886b7d6f934184f179c1'}
SELF = 'tests/module064-provider-controls-scope.py'
assert subprocess.check_output(['git','merge-base',BASE,'HEAD']).decode().strip()==BASE
actual=set(subprocess.check_output(['git','diff','--name-only',BASE]).decode().splitlines())
assert actual == set(EXPECTED) | {SELF}, 'Unexpected provider repair files'
for p,digest in EXPECTED.items():
    assert hashlib.sha256(Path(p).read_bytes()).hexdigest()==digest, 'Provider repair changed: '+p
for p in ('.github/workflows/projectpulse-deploy-test.yml','.github/workflows/projectpulse-deploy-production.yml','.github/flowhive-psa-protected-test-candidate.json'):
    assert Path(p).read_bytes()==subprocess.check_output(['git','show',BASE+':'+p]), 'Deployment authority changed'
print('MODULE064_PROVIDER_CONTROLS_SCOPE=PASS')
