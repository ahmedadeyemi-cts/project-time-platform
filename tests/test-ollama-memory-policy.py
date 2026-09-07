import importlib.util
import json
from pathlib import Path

root = Path(__file__).resolve().parents[1]
p = root / 'deployment/oracle-celar/verify-ollama-memory-policy.py'
spec = importlib.util.spec_from_file_location('memory_policy', p)
m = importlib.util.module_from_spec(spec)
spec.loader.exec_module(m)
manifest = json.loads((root / 'deployment/oracle-celar/release.json').read_text())
valid = b'OLLAMA_MAX_LOADED_MODELS=1\0OLLAMA_NUM_PARALLEL=1\0UNRELATED_SECRET=never-print-this\0'
assert m.evaluate(valid, manifest)
assert not m.evaluate(valid, [])
assert not m.evaluate(valid.replace(b'MODELS=1', b'MODELS=2'), manifest)
assert not m.evaluate(valid.replace(b'PARALLEL=1', b'PARALLEL=2'), manifest)
assert not m.evaluate(b'OLLAMA_MAX_LOADED_MODELS=1\0', manifest)
assert not m.evaluate(valid + b'OLLAMA_MAX_LOADED_MODELS=2\0', manifest)
assert not m.evaluate(valid, dict(manifest, ollamaMaxLoadedModels=2))
assert not m.evaluate(valid, dict(manifest, ollamaMaxLoadedModels=True))
assert not m.evaluate(b'x' * 65537, manifest)
# Preserve exhaustive scope capacity and specialist inventory; control residence.
assert manifest['sowContextTokens'] == 16384
assert manifest['localGenerationModels'] == ['gemma3:4b', 'qwen3:4b-instruct', 'llama3.2:3b']
assert manifest['ollamaMaxLoadedModels'] == manifest['ollamaNumParallel'] == 1
script = (root / 'deployment/oracle-celar/deploy.sh').read_text()
assert 'Environment="OLLAMA_MAX_LOADED_MODELS=$OLLAMA_MAX_LOADED_MODELS"' in script
assert '"$ROOT/verify-ollama-memory-policy.py"' in script
assert script.index('systemctl restart ollama.service') < script.index('"$INSTALL_ROOT/health-check.sh"')
health = (root / 'deployment/oracle-celar/health-check.sh').read_text()
assert '--property=MainPID --value' in health and 'verify-ollama-memory-policy.py' in health
print('OLLAMA_SINGLE_MODEL_MEMORY_POLICY=PASS')
