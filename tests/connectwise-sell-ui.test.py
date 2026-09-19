"""Local browser smoke test. Requires Playwright + Chromium; synthetic data only."""
import json, os, pathlib, socket, subprocess, tempfile, time
from playwright.sync_api import sync_playwright

root = pathlib.Path(__file__).resolve().parents[1]
web = root / 'src/frontend/project-time-web'
with tempfile.TemporaryDirectory(prefix='.connectwise-ui-', dir=web) as folder:
    folder = pathlib.Path(folder)
    (folder / 'index.html').write_text('<div id="root"></div><script type="module" src="./entry.jsx"></script>')
    (folder / 'entry.jsx').write_text("import React from 'react'; import {createRoot} from 'react-dom/client'; import Center from '../src/CrmErpIntegrationCenter.jsx'; createRoot(document.getElementById('root')).render(<Center/>);")
    log = tempfile.TemporaryFile()
    server = subprocess.Popen(['node', 'node_modules/vite/bin/vite.js', '--host', '127.0.0.1', '--port', '5188', '--strictPort'], cwd=web, stdout=log, stderr=log)
    try:
        for _ in range(100):
            try:
                with socket.create_connection(('127.0.0.1', 5188), timeout=.2): break
            except OSError: time.sleep(.1)
        else: raise RuntimeError('Local Vite did not start')
        with sync_playwright() as pw:
            browser = pw.chromium.launch(executable_path=os.environ.get('CHROMIUM_EXECUTABLE') or None, args=['--no-sandbox'])
            page = browser.new_page(viewport={'width': 1440, 'height': 1100})
            errors, writes, providers = [], [], []
            page.on('pageerror', lambda err: errors.append(str(err)))
            def api(route):
                request = route.request
                if request.method in ('POST', 'PUT'):
                    body = request.post_data_json
                    writes.append((request.url, body))
                    if request.url.endswith('/providers'):
                        providers.append({**body, 'isPersisted': True, 'isBuiltin': True, 'availabilityStatus': 'not_configured', 'credentialConfigured': False})
                    if request.url.endswith('/credential'):
                        providers[0]['credentialConfigured'] = True
                    result = {'message': 'Saved securely'}
                elif request.url.endswith('/providers'):
                    result = {'providers': providers, 'access': {'canManage': True}}
                else: result = {}
                route.fulfill(status=200, content_type='application/json', body=json.dumps(result))
            page.route('**/api/**', api)
            page.goto(f'http://127.0.0.1:5188/{folder.name}/index.html')
            page.get_by_role('button', name='Configure connection', exact=True).click()
            assert page.get_by_label('Base URL', exact=False).input_value() == 'https://sellapi.quosalsell.com'
            assert page.get_by_label('Base URL', exact=False).get_attribute('readonly') is not None
            assert page.locator('.crm-erp-configuration').get_by_role('button', name='OAuth 2.0', exact=False).count() == 0
            assert page.get_by_label('Private API key', exact=True).is_disabled()
            page.get_by_role('button', name='Create connection', exact=True).click()
            page.get_by_role('button', name='Edit connection', exact=True).click()
            page.get_by_label('Access key (from the ConnectWise SELL URL)', exact=True).fill('tenant_azure')
            page.get_by_label('Public API key', exact=True).fill('synthetic-public')
            page.get_by_label('Private API key', exact=True).fill('synthetic-private')
            assert page.get_by_label('Private API key', exact=True).get_attribute('type') == 'password'
            page.get_by_role('button', name='Save credential securely', exact=True).click()
            page.wait_for_function("document.querySelector('input[autocomplete=\"new-password\"]')?.value === ''")
            assert writes[0][1]['providerKey'] == 'connectwise_sell'
            assert 'privateKey' not in writes[0][1]
            assert writes[1][1] == {'accessKey': 'tenant_azure', 'publicKey': 'synthetic-public', 'privateKey': 'synthetic-private'}
            assert all(not value for value in page.locator('input[autocomplete="new-password"]').evaluate_all('(inputs) => inputs.map(input => input.value)'))
            assert 'synthetic-private' not in page.locator('body').inner_text()
            assert 'Zendesk' not in page.locator('body').inner_text()
            assert not errors, errors
            browser.close()
            print('ConnectWise SELL UI: save before keys, three masked fields, payload isolation, read-only CPQ endpoints and post-save clearing passed.')
    finally:
        server.terminate()
        server.wait(timeout=10)
        log.close()
