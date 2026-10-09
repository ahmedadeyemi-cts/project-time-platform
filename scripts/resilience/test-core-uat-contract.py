#!/usr/bin/env python3
"""Offline unit checks for the fail-closed JSON health contract."""
import importlib.util
from io import BytesIO
from pathlib import Path
from unittest.mock import patch

spec = importlib.util.spec_from_file_location('core', Path(__file__).with_name('check-core-uat-offline.py'))
core = importlib.util.module_from_spec(spec)
spec.loader.exec_module(core)

class Reply:
    def __init__(self, body, content_type, status=200):
        self.data = body.encode()
        self.headers = {'Content-Type': content_type}
        self.status = status
    def __enter__(self): return self
    def __exit__(self, *args): return False
    def read(self, n): return self.data[:n]

with patch.object(core.urllib.request, 'urlopen', return_value=Reply('{"status":"alive"}', 'application/json')):
    assert core.get('/health/live') == (200, {'status': 'alive'})
for reply in (Reply('<!doctype html>', 'text/html'), Reply('[]', 'application/json')):
    with patch.object(core.urllib.request, 'urlopen', return_value=reply):
        try:
            if core.get('/health/live')[1].get('status') == 'alive': pass
        except (RuntimeError, AttributeError):
            continue
        raise AssertionError('Invalid health payload accepted')
print('PULSE_CORE_OFFLINE_CONTRACT_TEST=PASS')
