"""Exercise the actual gateway route with a clock and failing local models."""
import ast
import json
import os
from pathlib import Path
from types import SimpleNamespace

root = Path(__file__).resolve().parents[1]
source = root / 'deployment/oracle-celar/gateway/wsgi.py'
tree = ast.parse(source.read_text())
selected = [node for node in tree.body if isinstance(node, ast.FunctionDef) and node.name in {'_budgets', '_sow_completion', '_local_chat_completions'}]
manifest = json.loads((root / 'deployment/oracle-celar/release.json').read_text())
clock = [0.0]
attempts = []
class Response:
    def __init__(self, body):
        self.body = body
        self.headers = {}

def post(path, payload, timeout, maximum_bytes):
    attempts.append((payload['model'], timeout))
    clock[0] += timeout
    return {'error': {'code': 'private_runtime_timeout'}}, 504

payload = {'model': 'gemma3:4b', 'messages': [{'role': 'user', 'content': 'synthetic test'}], 'max_tokens': 8192}
gateway = SimpleNamespace(CHAT_TIMEOUT_SECONDS=manifest['chatTimeoutSeconds'], MAX_GATEWAY_RESPONSE_BYTES=1000000,
    _bounded_request_json=lambda: payload, _ollama_post=post, _error=lambda code, status: (code, status))
request = SimpleNamespace(headers={})
ns = dict(Any=object, os=os, gateway=gateway, request=request, jsonify=Response,
    time=SimpleNamespace(monotonic=lambda: clock[0]), CONTRACT_MODEL='gemma3:4b',
    STRUCTURED_FEATURES={'sow_gsd_planning', 'project_flowhive_plan'},
    STRUCTURED_ORDER=manifest['structuredGenerationOrder'], GENERAL_ORDER=manifest['generalGenerationOrder'],
    STRUCTURED_ATTEMPT_SECONDS=manifest['structuredModelAttemptSeconds'], GENERAL_ATTEMPT_SECONDS=manifest['generalModelAttemptSeconds'],
    SOW_CONTEXT_TOKENS=manifest['sowContextTokens'], SOW_ATTEMPT_SECONDS=manifest['sowModelAttemptSeconds'], SOW_TIMEOUT_SECONDS=manifest['sowTimeoutSeconds'],
    APPROVED_GENERATION_MODELS=set(manifest['localGenerationModels']))
exec(compile(ast.Module(body=selected, type_ignores=[]), str(source), 'exec'), ns)
for feature, expected in [('sow_gsd_planning',[3000,600]), ('project_flowhive_plan',[150,60,20]), ('help_assistant',[140,70,20])]:
    clock[0] = 0
    attempts.clear()
    request.headers = {'X-Pulse-AI-Feature':feature}
    response, status = ns['_local_chat_completions']()
    assert status == 504
    assert [seconds for _, seconds in attempts] == expected, attempts
    expected_order = manifest['generalGenerationOrder'] if feature == 'help_assistant' else manifest['structuredGenerationOrder']
    assert [model for model, _ in attempts] == expected_order[:len(attempts)]
# Refusal remains terminal: no attempt at a second local model.
attempts.clear()
def refusal(path, payload, timeout, maximum_bytes):
    attempts.append(payload['model'])
    return {'error': {'code': 'safety_refusal'}}, 403
gateway._ollama_post = refusal
request.headers = {'X-Pulse-AI-Feature':'sow_gsd_planning'}
_, status = ns['_local_chat_completions']()
assert status == 403 and len(attempts) == 1
try:
    ns['_budgets']('TEST_UNSET_BUDGET', [420,120,90], 3, 600)
    raise AssertionError('Oversized route accepted')
except RuntimeError:
    pass
# DeepSeek plus bounded Oracle work fits within durable SOW acceptance.
assert 120 + 3620 < 65 * 60
# Fail fast models leave the shared deadline available for the next candidate.
clock[0] = 0
attempts.clear()
def quick_failure(path, payload, timeout, maximum_bytes):
    attempts.append((payload['model'], timeout))
    clock[0] += 1
    return {'error': {'code': 'private_runtime_unavailable'}}, 502
gateway._ollama_post = quick_failure
_, status = ns['_local_chat_completions']()
assert status == 502 and len(attempts) == 3
assert [budget for _, budget in attempts] == [3000,3000,3000]
# Native SOW conversion keeps token/context options and explicit finish reason.
def native_success(path, native, timeout, maximum_bytes):
    assert path == '/api/chat' and native['stream'] is False and native['format'] == 'json'
    assert native['options'] == {'num_ctx':16384, 'num_predict':8192}
    return {'model':native['model'], 'done':True, 'done_reason':'length',
            'message':{'role':'assistant', 'content':'{"partial":true}'}}, 200
gateway._ollama_post = native_success
response, status = ns['_local_chat_completions']()
assert status == 200 and response.body['choices'][0]['finish_reason'] == 'length'
assert response.headers['X-Celar-Local-Model'] == 'gemma3:4b'
# A model identity mismatch cannot acquire a trusted local-model attestation.
def wrong_model(path, native, timeout, maximum_bytes):
    return {'model':'unreviewed', 'done':True, 'done_reason':'stop',
            'message':{'role':'assistant', 'content':'{}'}}, 200
gateway._ollama_post = wrong_model
_, status = ns['_local_chat_completions']()
assert status == 502
assert ns['_budgets']('TEST_UNSET_BUDGET', [3000]*3, 3, 3600, shared_deadline=True) == [3000]*3
print('CELAR_SOW_RUNTIME_DEADLINES=PASS')

# The coupled runtime gate must also run on manual and squash/rebase rollouts.
controller = (root / '.github/workflows/projectpulse-deploy-test.yml').read_text()
gate = controller.split('      - name: Verify the matching Oracle SOW runtime before API rollout', 1)[1].split('      - name:', 1)[0]
assert '\n        if:' not in gate
assert 'sowContextTokens == 16384' in gate
