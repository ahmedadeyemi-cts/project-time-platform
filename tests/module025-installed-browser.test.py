#!/usr/bin/env python3
"""Exercise the actual installed browser verifier against the complete production-built application.

Only synthetic local HTTP fixtures are used. No model or Test service is called.
"""
import asyncio
import base64
import hashlib
import importlib.util
import io
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import zipfile
from unittest.mock import patch
from urllib.request import Request, urlopen
from playwright.async_api import BrowserType, Page

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location('installed_uat', ROOT / 'scripts/release-test/run-module025-installed-sa-uat.py')
runner = importlib.util.module_from_spec(spec)
spec.loader.exec_module(runner)

def document(name):
    data = io.BytesIO()
    with zipfile.ZipFile(data, 'w') as package:
        package.writestr(name, '<synthetic>Retained document regression content</synthetic>')
    return data.getvalue()

async def main():
    documents = {'sow': document('word/document.xml'), 'gsd': document('xl/workbook.xml')}
    environment = os.environ | {'MODULE025_TEST_' + label.upper(): base64.b64encode(data).decode() for label, data in documents.items()}
    server = subprocess.Popen(['node', 'tests/module025-installed-browser-harness.mjs'], cwd=ROOT, env=environment,
        stdout=subprocess.PIPE, text=True)
    try:
        for line in server.stdout:
            if line.startswith('MODULE025_HARNESS_READY='):
                runner.ORIGIN = json.loads(line.split('=', 1)[1])['origin']
                break
            print(line.rstrip(), file=sys.stderr)
        else:
            raise RuntimeError('Local browser fixture exited before readiness')
        report = {'confirmedVersion': {label + 'Sha256': hashlib.sha256(data).hexdigest() for label, data in documents.items()}}
        def state():
            with urlopen(runner.ORIGIN + '/__state') as response:
                return json.load(response)
        def reset(status, deny=False, denied_module=False):
            with urlopen(Request(runner.ORIGIN + '/__reset', method='POST',
                data=json.dumps({'status': status, 'denyDownload': 'true' if deny else '', 'deniedModule': denied_module}).encode())) as response:
                response.read()
        session = {'sessionToken': 'synthetic-session-only', 'username': 'synthetic.local', 'loginMethod': 'local', 'provider': 'LOCAL', 'expiresAt': '2099-01-01T00:00:00Z', 'mustChangePassword': False}
        with tempfile.TemporaryDirectory() as directory:
            try:
                launch = BrowserType.launch
                async def test_launch(browser_type, **kwargs):
                    if os.environ.get('MODULE025_TEST_CHROMIUM'):
                        kwargs['executable_path'] = os.environ['MODULE025_TEST_CHROMIUM']
                    return await launch(browser_type, **kwargs)
                with patch.object(BrowserType, 'launch', test_launch):
                    reset('draft')
                    await runner.browser_lifecycle(session, 'SOW-TEST-025', '', report, Path(directory), preflight=True)
                    assert report['browserPreflight']['status'] == 'passed'
                    assert report['browserPreflight']['entryState']['visibleWorkspaces'] == 1
                    assert report['browserPreflight']['entryState']['route'] == 'sow-generator'
                    assert all(request['method'] == 'GET' for request in state()['requests'] if request['path'].startswith('/api/module025/'))
                    assert not list(Path(directory).iterdir()), 'Preflight must not download documents or write a record'
                    reset('confirmed')
                    await runner.browser_lifecycle(session, 'SOW-TEST-025', 'Synthetic reload marker', report, Path(directory))
                    assert report['browser']['status'] == 'passed'
                    assert set(report['browser']['confirmedDownloads']) == {'sow', 'gsd'}
                    assert all(value['repeatBytesVerified'] for value in report['browser']['confirmedDownloads'].values())
                    saved = state()
                    assert 'Synthetic reload marker' in saved['engagement']['serviceOverview']
                    assert [(r['method'], r['path'].rsplit('/', 1)[-1]) for r in saved['requests'] if r['method'] != 'GET' and r['path'].startswith('/api/module025/')] == [('POST', 'reopen'), ('PUT', 'fixture-025')]
                    reset('confirmed', deny=True)
                    timeout = Page.set_default_timeout
                    expect_download = Page.expect_download
                    with patch.object(Page, 'expect_download', lambda page: expect_download(page, timeout=2000)):
                        try:
                            await runner.browser_lifecycle(session, 'SOW-TEST-025', '', report, Path(directory))
                            raise AssertionError('Denied document request must fail')
                        except runner.AcceptanceError as error:
                            assert str(error) == 'module025_browser_timeout_download_sow', str(error)
                    assert report['browser']['status'] == 'failed'
                    assert report['browser']['failedResponses'] == [{'endpoint': 'sow_download', 'status': 401}]
                    reset('draft', denied_module=True)
                    with patch.object(Page, 'set_default_timeout', lambda page, _: timeout(page, 4000)):
                        try:
                            await runner.browser_lifecycle(session, 'SOW-TEST-025', '', report, Path(directory), preflight=True)
                            raise AssertionError('A denied module must not pass browser preflight')
                        except runner.AcceptanceError as error:
                            assert str(error) == 'module025_browser_timeout_workspace', str(error)
                    assert report['browserPreflight']['entryState']['module025Denied'] is True
                    assert report['browserPreflight']['entryState']['route'] == 'dashboard'
                    assert report['browserPreflight']['browserWriteCount'] == 0
                    assert 'synthetic-session-only' not in json.dumps(report)
                    assert not any(r['path'].endswith('/generate') for r in state()['requests'])
            finally:
                print(json.dumps(report))
        print('MODULE025_INSTALLED_BROWSER_VERIFIER=PASS')
    finally:
        server.terminate()
        server.wait(timeout=15)

asyncio.run(main())
