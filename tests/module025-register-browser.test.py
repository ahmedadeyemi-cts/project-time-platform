"""Run the real read-only register verifier against the complete built UI."""
import asyncio
import base64
from contextlib import redirect_stderr
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
    env = os.environ | {'MODULE025_TEST_REGISTER': 'true', 'MODULE025_TEST_DELAY_STARTUP': 'true',
                        'MODULE025_TEST_SOW': document('word/document.xml'),
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
        def reset(deny=False, fail_report='', hold_bootstrap=False, deny_navigation=False, retained_sa=False):
            with urlopen(Request(runner.ORIGIN + '/__reset', method='POST',
                data=json.dumps({'status': 'confirmed', 'deniedModule': deny, 'failReport': fail_report,
                                 'holdBootstrap': hold_bootstrap, 'deniedNavigation': deny_navigation,
                                 'retainedSa': retained_sa}).encode())) as response:
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
            # Hold the browser's real bootstrap until this test has inspected
            # the early tab. Loading must disable it; releasing the response
            # enables it and the complete real register lifecycle must pass.
            ready_open_register = runner.open_register
            early_visits = 0
            async def early_tab(page):
                nonlocal early_visits
                early_visits += 1
                if early_visits > 1:
                    await ready_open_register(page)
                    return
                tab = page.get_by_role('tab', name='SOW Register & SELL', exact=True)
                await tab.wait_for(state='visible')
                assert await tab.is_disabled(), 'Register tab was active before bootstrap completed'
                await page.context.request.get(runner.ORIGIN + '/__release-bootstrap')
                await tab.click()
            reset(hold_bootstrap=True)
            with patch.object(runner, 'open_register', early_tab):
                await runner.run()
            # Module API access cannot override a published navigation denial.
            # Preserve that failure and expose only closed diagnostic metadata.
            async def denied_navigation(page):
                await page.wait_for_function("window.__projectPulseEffectiveNavigation?.deniedModuleNumbers?.includes('025') === true")
                page.set_default_timeout(1500)
                await ready_open_register(page)
            reset(deny_navigation=True)
            diagnostic_output = io.StringIO()
            with patch.object(runner, 'open_register', denied_navigation), redirect_stderr(diagnostic_output):
                try:
                    await runner.run()
                    raise AssertionError('Navigation denial was accepted')
                except Exception as error:
                    assert type(error).__name__ == 'TimeoutError', type(error).__name__
            diagnostics = diagnostic_output.getvalue().splitlines()
            entry = json.loads(next(line.split('=', 1)[1] for line in diagnostics if line.startswith('MODULE025_REGISTER_ENTRY=')))
            assert entry['route'] == 'dashboard' and entry['module025Denied'] is True
            assert entry['module025ExplicitlyDenied'] is True
            assert entry['visibleWorkspaces'] == 0 and entry['visibleRegisterTabs'] == 0
            assert all(isinstance(value, (bool, int)) or value in ('dashboard', 'ready') for value in entry.values())
            assert 'synthetic-session-only' not in diagnostic_output.getvalue()
            assert 'demo.manager@ussignal.local' not in diagnostic_output.getvalue()
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
            # The independent path must use actual SA permissions and an exact
            # previously retained synthetic record, with no fixture override.
            reset(retained_sa=True)
            with patch.dict(os.environ, {'MODULE025_REGISTER_MODE': 'retained-sa',
                    'MODULE025_SOURCE_RUN_ID': '12345', 'PROJECTPULSE_M025_SA_EMAIL': 'synthetic.sa@ussignal.local',
                    'PROJECTPULSE_M025_SA_PASSWORD': 'synthetic-password-only'}):
                await runner.run()
                requests = state()['requests']
                assert not any(item['runHeaderPresent'] for item in requests)
                assert not any(item['method'] != 'GET' for item in requests if item['path'].startswith('/api/module025/'))
                assert len([item for item in requests if item['authenticated'] and item['path'].endswith(('sow.docx', 'gsd.xlsx'))]) == 4
                reset()  # Fixture-only authority cannot substitute for normal SA authority.
                try:
                    await runner.run()
                    raise AssertionError('Fixture authority accepted as normal SA')
                except RuntimeError as error:
                    assert str(error) == 'browser_login_failed'
        valid = {'runtimeEnvironment': 'test', 'records': [{'ownerUserId': 'sa',
            'projectName': 'Protected UAT Module 025 12345', 'customerName': 'Protected UAT normal SA 12345',
            'latestVersionNumber': 1, 'engagementNumber': 'SOW-TEST-025'}]}
        assert runner.select_retained_record(valid, 'sa', '12345') == 'SOW-TEST-025'
        for invalid in (valid | {'runtimeEnvironment': 'production'}, valid | {'records': []},
                        valid | {'records': valid['records'] * 2}, valid | {'hasMore': True}):
            try:
                runner.select_retained_record(invalid, 'sa', '12345')
                raise AssertionError('Unsafe retained record selection accepted')
            except RuntimeError:
                pass
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
