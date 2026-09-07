#!/usr/bin/env python3
"""Verify only selected memory settings in the running Ollama daemon.

Never print process environment values or exception text. Invoked by the existing
root-owned deployment health check after the daemon has been restarted.
"""
import json
from pathlib import Path
import sys

KEYS = {'OLLAMA_MAX_LOADED_MODELS': 'ollamaMaxLoadedModels', 'OLLAMA_NUM_PARALLEL': 'ollamaNumParallel'}

def evaluate(raw, manifest):
    if not isinstance(manifest, dict) or len(raw) > 65536:
        return False
    actual = {}
    for entry in raw.split(b'\0'):
        key, _, value = entry.partition(b'=')
        if key.decode('ascii', errors='replace') in KEYS:
            key = key.decode('ascii')
            if key in actual or value != b'1':
                return False
            actual[key] = 1
    return len(actual) == len(KEYS) and all(type(manifest.get(field)) is int and manifest[field] == actual[key] == 1
                                          for key, field in KEYS.items())

def main():
    try:
        manifest = json.loads(Path(sys.argv[1]).read_text())
        pid = int(sys.argv[2])
        if pid <= 1 or Path(f'/proc/{pid}/comm').read_text().strip() != 'ollama':
            raise ValueError()
        with Path(f'/proc/{pid}/environ').open('rb') as source:
            valid = evaluate(source.read(65537), manifest)
    except (OSError, ValueError, IndexError, TypeError):
        print('OLLAMA_MEMORY_POLICY=UNAVAILABLE')
        return 1
    print('OLLAMA_MEMORY_POLICY=PASS' if valid else 'OLLAMA_MEMORY_POLICY=MISMATCH')
    return 0 if valid else 1

if __name__ == '__main__':
    raise SystemExit(main())
