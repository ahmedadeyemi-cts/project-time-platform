#!/usr/bin/env python3
"""Read-only browser acceptance for an already-created Module 025 fixture.

The API lifecycle creates and retains the synthetic version. This browser pass
only reads the register, verifies the two retained artifacts are displayed, and
reloads without starting another generation or mutation.
"""
from __future__ import annotations

import asyncio
import hashlib
import json
import os
from pathlib import Path
from urllib.parse import urlparse

ORIGIN = 'https://phd-west-test.onenecklab.com'


def fail(code: str) -> None:
    raise RuntimeError(code)


async def run() -> None:
    from playwright.async_api import async_playwright

    base = os.environ.get('BASE', ORIGIN).rstrip('/')
    if base != ORIGIN:
        fail('unapproved_public_origin')
    password = os.environ.get('TEST_LOGIN_PASSWORD', '')
    engagement_number = os.environ.get('MODULE025_ENGAGEMENT_NUMBER', '')
    evidence_path = os.environ.get('MODULE025_CREATE_RESPONSE', '')
    if not engagement_number and evidence_path:
        engagement_number = json.loads(Path(evidence_path).read_text(encoding='utf-8')).get('engagement', {}).get('engagementNumber', '')
    if len(password) < 12 or not engagement_number:
        fail('browser_fixture_inputs_missing')

    async with async_playwright() as playwright:
        browser = await playwright.chromium.launch(headless=True)
        context = await browser.new_context(ignore_https_errors=False, viewport={'width': 1600, 'height': 1000})
        login = await context.request.post(
            f'{base}/api/auth/local/login',
            data={'username': 'demo.manager@ussignal.local', 'password': password},
            headers={'Origin': base, 'Sec-Fetch-Site': 'same-origin'},
        )
        if login.status != 200:
            fail('browser_login_failed')
        session = await login.json()
        if session.get('provider') != 'LOCAL' or not session.get('sessionToken'):
            fail('browser_session_missing')
        password = ''
        generation_posts = []
        unexpected_writes = []

        await context.add_init_script(
            "localStorage.setItem('projectPulseAuthSession', SESSION); localStorage.removeItem('projectPulseViewAsUser');"
            .replace('SESSION', json.dumps(json.dumps(session)))
        )

        async def restrict(route):
            request = route.request
            parsed = urlparse(request.url)
            if parsed.netloc == urlparse(base).netloc and parsed.path.endswith('/generate') and request.method == 'POST':
                generation_posts.append(parsed.path)
            if request.method not in ('GET', 'HEAD', 'OPTIONS'):
                unexpected_writes.append(request.method + ':' + parsed.path)
                await route.abort()
                return
            await route.continue_()

        await context.route('**/*', restrict)
        page = await context.new_page()
        page.set_default_timeout(45_000)
        try:
            await page.goto(f'{base}/#sow-generator', wait_until='domcontentloaded', timeout=45_000)
            await page.get_by_role('button', name='SOW Register & SELL', exact=True).click()
            register = page.locator('[data-module025-sow-register="true"]')
            await register.wait_for(state='visible')
            csv_path = await register.get_by_role('button', name='Export SA report (.csv)', exact=True).get_attribute('data-csv-download-path')
            async with page.expect_download() as report_download_info:
                await register.get_by_role('button', name='Export SA report (.csv)', exact=True).click()
            report_download = await report_download_info.value
            report_bytes = Path(await report_download.path()).read_bytes()
            if b'Engagement' not in report_bytes and b'SOW' not in report_bytes:
                fail('browser_csv_download_content_missing')
            search = register.get_by_placeholder('Search retained records')
            await search.fill(engagement_number)
            row = register.locator('table').nth(1).get_by_role('button', name=engagement_number, exact=True)
            await row.wait_for(state='visible')
            await row.click()
            versions = register.locator('.m025-register-version')
            await versions.first.wait_for(state='visible')
            if await versions.count() != 1:
                fail('browser_retained_version_count_mismatch')
            if await versions.first.get_by_role('button', name='Download SOW v1', exact=True).count() != 1:
                fail('browser_sow_download_missing')
            if await versions.first.get_by_role('button', name='Download GSD v1', exact=True).count() != 1:
                fail('browser_gsd_download_missing')
            await versions.first.get_by_text('File integrity', exact=True).click()
            if await versions.first.locator('.m025-register-hash').count() != 2:
                fail('browser_retained_hash_display_missing')
            hash_text = await versions.first.locator('.m025-register-hash').all_text_contents()
            expected_hashes = {
                'sow.docx': hash_text[0].split(':', 1)[-1].strip(),
                'gsd.xlsx': hash_text[1].split(':', 1)[-1].strip(),
            }
            for artifact, button_name in (('sow.docx', 'Download SOW v1'), ('gsd.xlsx', 'Download GSD v1')):
                with page.expect_download() as download_info:
                    await versions.first.get_by_role('button', name=button_name, exact=True).click()
                download = await download_info.value
                data = Path(await download.path()).read_bytes()
                if hashlib.sha256(data).hexdigest() != expected_hashes[artifact]:
                    fail(f'browser_{artifact}_hash_mismatch')
                with page.expect_download() as repeat_download_info:
                    await versions.first.get_by_role('button', name=button_name, exact=True).click()
                repeat = await repeat_download_info.value
                repeat_data = Path(await repeat.path()).read_bytes()
                if repeat_data != data:
                    fail(f'browser_{artifact}_repeat_bytes_changed')

            unauthenticated = await playwright.request.new_context()
            try:
                protected_paths = [csv_path, await versions.first.get_attribute('data-sow-download-path')]
                for protected_path in protected_paths:
                    if protected_path:
                        unauthorized = await unauthenticated.get(f'{base}{protected_path}')
                        if unauthorized.status not in (401, 403):
                            fail('browser_unauthorized_protected_download_not_rejected')
            finally:
                await unauthenticated.dispose()

            await page.reload(wait_until='domcontentloaded', timeout=45_000)
            await page.get_by_role('button', name='SOW Register & SELL', exact=True).click()
            await register.get_by_placeholder('Search retained records').fill(engagement_number)
            await register.locator('table').nth(1).get_by_role('button', name=engagement_number, exact=True).click()
            await register.locator('.m025-register-version').first.wait_for(state='visible')
            if await register.locator('.m025-register-version').count() != 1:
                fail('browser_reload_retained_version_missing')
            if generation_posts or unexpected_writes:
                fail('browser_readonly_register_started_mutation')
        finally:
            await context.close()
            await browser.close()

    print('MODULE025_REGISTER_BROWSER_DISPLAY=PASS csv=downloaded retainedBytes=sha256-verified repeatedBytes=stable unauthorized=checked versions=1 reload=verified generationPosts=0 writes=0')


if __name__ == '__main__':
    asyncio.run(run())
