"""Run the real read-only register verifier against the complete built UI."""
import asyncio
import base64
import importlib.util
import io
import json
import os
from pathlib import Path
import subprocess
import sys
from unittest.mock import patch
from urllib.request import Request, urlopen
import zipfile
from playwright.async_api import BrowserType

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location('register_verifier', ROOT / 'tests/module025-sow-register-browser.py')
runner = importlib.util.module_from_spec(spec)
spec.loader.exec_module(runner)

def document(name):
    data = io.BytesIO()
    with zipfile.ZipFile(data, 'w') as package:
        package.writestr(name, '<synthetic>Retained document</synthetic>')
    return base64.b64encode(data.getvalue()).decode()

async def main():
    env = os.environ | {'MODULE025_TEST_REGISTER': 'true', 'MODULE025_TEST_SOW': document('word/document.xml'),
                        'MODULE025_TEST_GSD': document('xl/workbook.xml')}
    server = subprocess.Popen(['node', 'tests/module025-installed-browser-harness.mjs'], cwd=ROOT,
                              env=env, stdout=subprocess.PIPE, text=True)
    try:
        for line in server.stdout:
            if line.startswith('MODULE025_HARNESS_READY='):
                runner.ORIGIN = json.loads(line.split('=', 1)[1])['origin']
                break
            print(line.rstrip(), file=sys.stderr)
        else:
            raise RuntimeError('Register fixture failed to start')
        def state():
            with urlopen(runner.ORIGIN + '/__state') as response:
                return json.load(response)
        def reset(deny=False, fail_report=''):
            with urlopen(Request(runner.ORIGIN + '/__reset', method='POST',
                data=json.dumps({'status': 'confirmed', 'deniedModule': deny, 'failReport': fail_report}).encode())) as response:
                response.read()
        variables = {'BASE': runner.ORIGIN, 'TEST_LOGIN_PASSWORD': 'synthetic-password-only',
                     'MODULE025_ENGAGEMENT_NUMBER': 'SOW-TEST-025', 'MODULE025_UAT_RUN_ID': '12345-1'}
        launch = BrowserType.launch
        async def local_launch(browser_type, **kwargs):
            if os.environ.get('MODULE025_TEST_CHROMIUM'):
                kwargs['executable_path'] = os.environ['MODULE025_TEST_CHROMIUM']
            return await launch(browser_type, **kwargs)
        with patch.dict(os.environ, variables), patch.object(BrowserType, 'launch', local_launch):
            reset()
            await runner.run()
            requests = state()['requests']
            assert not any(item['method'] != 'GET' for item in requests if item['path'].startswith('/api/module025/'))
            assert not any(item['runHeaderPresent'] for item in requests if not item['path'].startswith('/api/module025/'))
            protected = [item for item in requests if item['path'].endswith(('sow.docx', 'gsd.xlsx'))]
            assert len([item for item in protected if item['authenticated'] and item['fixture']]) == 4
            assert len([item for item in protected if not item['authenticated'] and not item['runHeaderPresent']]) == 2
            # Failed report APIs must remain failures with a closed HTTP code,
            # not an unrelated missing-button timeout or a raw response body.
            for mode, code in [('api', 'browser_register_preflight_http_500'),
                               ('browser', 'browser_register_read_http_403')]:
                reset(fail_report=mode)
                try:
                    await runner.run()
                    raise AssertionError('Failed report was accepted')
                except RuntimeError as error:
                    assert str(error) == code, str(error)
            # Missing/revoked fixture authority must fail before opening the register.
            reset(deny=True)
            try:
                await runner.run()
                raise AssertionError('Denied fixture was accepted')
            except RuntimeError as error:
                assert str(error) == 'browser_fixture_bootstrap_denied'
            reset()
            with patch.dict(os.environ, {'MODULE025_UAT_RUN_ID': 'wrong-run'}):
                try:
                    await runner.run()
                    raise AssertionError('Malformed run was accepted')
                except RuntimeError as error:
                    assert str(error) == 'browser_fixture_run_missing'
        for invalid in (b'Engagement,Customer\nSOW-TEST-025,Synthetic customer\n',
                        b'<html>SOW error</html>', b'\xff'):
            try:
                runner.verify_csv(invalid)
                raise AssertionError('Non-report CSV was accepted')
            except RuntimeError as error:
                assert str(error).startswith('browser_csv_')
        print('MODULE025_REGISTER_BROWSER_REGRESSION=PASS full_app=true reads_only=true fixture_scoped=true report_http_failures=closed csv_contract=verified')
    finally:
        server.terminate()
        server.wait(timeout=15)

asyncio.run(main())
