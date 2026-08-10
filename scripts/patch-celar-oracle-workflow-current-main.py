from __future__ import annotations

from pathlib import Path

path = Path('.github/workflows/celar-ai-oracle-test-runtime-deploy.yml')
text = path.read_text(encoding='utf-8')
lines = text.splitlines(keepends=True)

matches = [
    index
    for index, line in enumerate(lines)
    if '"$ORACLE_EMBEDDING_ENDPOINT" | jq -e' in line
]
if len(matches) != 1:
    raise SystemExit(
        f'Expected exactly one Oracle embedding preflight pipeline, found {len(matches)}.'
    )

end = matches[0]
start = end - 2
if start < 0 or 'Celar AI Test embedding proof' not in ''.join(lines[start:end + 1]):
    raise SystemExit('The Oracle embedding preflight block did not match the guarded request.')

replacement = '''          EMBEDDING_FILE="$RUNNER_TEMP/oracle-embedding.json"
          curl -fsS --max-time 180 "${AUTH[@]}" -H 'Content-Type: application/json' \\
            -d "$(jq -nc --arg model "$ORACLE_EMBEDDING_MODEL" '{model:$model,input:["Celar AI Test embedding proof"],encoding_format:"float"}')" \\
            "$ORACLE_EMBEDDING_ENDPOINT" > "$EMBEDDING_FILE"
          python3 - "$EMBEDDING_FILE" <<'PY'
          import json
          import math
          import sys

          with open(sys.argv[1], encoding='utf-8') as handle:
              payload = json.load(handle)

          def vector(value):
              if not isinstance(value, list) or not value:
                  return None
              if any(isinstance(item, bool) or not isinstance(item, (int, float)) for item in value):
                  return None
              converted = [float(item) for item in value]
              return converted if all(math.isfinite(item) for item in converted) else None

          def vectors(value):
              if isinstance(value, dict):
                  if isinstance(value.get('data'), list):
                      ordered = {}
                      for fallback, item in enumerate(value['data']):
                          if not isinstance(item, dict):
                              return []
                          parsed = vector(item.get('embedding'))
                          index = item.get('index', fallback)
                          if parsed is None or isinstance(index, bool) or not isinstance(index, int) or index in ordered:
                              return []
                          ordered[index] = parsed
                      if sorted(ordered) != list(range(len(ordered))):
                          return []
                      return [ordered[index] for index in sorted(ordered)]
                  if 'embeddings' in value:
                      return vectors(value['embeddings'])
                  parsed = vector(value.get('embedding'))
                  return [parsed] if parsed is not None else []
              if isinstance(value, list):
                  parsed = vector(value)
                  if parsed is not None:
                      return [parsed]
                  if value and all(isinstance(item, list) for item in value):
                      result = [vector(item) for item in value]
                      return result if all(item is not None for item in result) else []
                  if value and all(isinstance(item, dict) for item in value):
                      return vectors({'data': value})
              return []

          result = vectors(payload)
          if len(result) != 1 or len(result[0]) != 768:
              raise SystemExit('Oracle embedding response did not contain one finite 768-dimensional vector.')
          print('ORACLE_EMBEDDING_RESPONSE=VALID')
          PY
'''

lines[start:end + 1] = [replacement]
updated = ''.join(lines)

old_build = 'DIGEST="$(scripts/build-pr55-acr-image.sh '
new_build = 'DIGEST="$(bash scripts/build-pr55-acr-image.sh '
if updated.count(old_build) != 1:
    raise SystemExit(
        f'Expected exactly one direct ACR build helper invocation, found {updated.count(old_build)}.'
    )
updated = updated.replace(old_build, new_build, 1)

old_loop = 'for attempt in $(seq 1 18); do'
new_loop = 'for _attempt in $(seq 1 18); do'
if updated.count(old_loop) != 1:
    raise SystemExit(
        f'Expected exactly one private-model availability loop, found {updated.count(old_loop)}.'
    )
updated = updated.replace(old_loop, new_loop, 1)

path.write_text(updated, encoding='utf-8')
print('Oracle Test activation workflow patched without logging embedding values.')
