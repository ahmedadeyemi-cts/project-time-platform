#!/usr/bin/env python3
"""Use real React administration UI with local API fixtures; no live policy or model writes."""
import asyncio, json, os, subprocess
from pathlib import Path
from playwright.async_api import async_playwright, expect
ROOT=Path(__file__).resolve().parents[1]
async def main():
    server=subprocess.Popen(['node','tests/module-catalog-browser-harness.mjs'],cwd=ROOT,stdout=subprocess.PIPE,text=True)
    try:
        for line in server.stdout:
            if line.startswith('CATALOG_BROWSER_READY='):
                origin=line.strip().split('=',1)[1]; break
        else: raise RuntimeError('Browser fixture failed to start')
        async with async_playwright() as p:
            launch={'headless':True}
            if os.environ.get('MODULE025_TEST_CHROMIUM'): launch['executable_path']=os.environ['MODULE025_TEST_CHROMIUM']
            browser=await p.chromium.launch(**launch)
            for mismatch in [False,True]:
                page=await browser.new_page(); page.on('dialog',lambda d:d.accept())
                page.on('pageerror', lambda error: print('PAGE_ERROR',error,flush=True))
                page.on('console', lambda message: print('CONSOLE',message.text,flush=True) if message.type=='error' else None)
                saved=[]; published=[]; version={'versionNumber':7,'policyVersionId':'v7'}
                roles=[{'roleCode':r,'roleName':n} for r,n in [('SUPER_ADMINISTRATOR','Super Administrator'),('SOLUTION_ARCHITECT','Solution Architect')]]
                modules=[{'moduleCode':'025','moduleName':'SOW & GSD Workspace','routeScope':'sow-generator','currentState':'Installed'}]
                actions=[{'actionCode':a} for a in ['MODULE_ACCESS','MODULE_VIEW','RECORD_CREATE','RECORD_EDIT','EXPORT_DATA','WORKFLOW_MANAGE']]
                async def api(route):
                    nonlocal saved,version
                    url=route.request.url.split('/api/',1)[1]
                    data={}
                    if url=='rbac/v1/bootstrap':
                        data={'roles':roles,'modules':modules,'actions':actions,'scopes':[{'scopeCode':'ORGANIZATION'}],'policyVersion':version,'canWritePolicy':True}
                    elif url.startswith('runtime/role-policy/v1/versions') or url=='role-policy/versions': data={'versions':[]}
                    elif url.startswith('rbac/v1/roles/'):
                        code=url.split('/')[3].split('?')[0]
                        data={'role':{'roleCode':code},'moduleCode':'025','grants':saved if code=='SOLUTION_ARCHITECT' else [],'policyVersion':version}
                    elif url=='rbac/v1/policies/validate':data={'valid':True}
                    elif url=='rbac/v1/policies/publish':
                        body=route.request.post_data_json; published.append(body)
                        c=body['changes'][0]; saved=[{**g,'roleCode':c['roleCode'],'moduleCode':c['moduleCode']} for g in c['grants']]
                        version={'versionNumber':8,'policyVersionId':'v8'}
                        data={'status':'policy_published',**version}
                    elif url=='rbac/v1/matrix':
                        data={'policyVersion':version,'grants':saved+([{'roleCode':'SOLUTION_ARCHITECT','moduleCode':'025','actionCode':'MODULE_ACCESS','scopeCode':'ORGANIZATION','grantEffect':'DENY'}] if mismatch else [])}
                    await route.fulfill(status=200,content_type='application/json',body=json.dumps(data))
                await page.route('**/api/**',api)
                await page.goto(origin)
                await page.wait_for_load_state('networkidle')
                role=page.locator('label').filter(has_text='1. Select role').locator('select')
                await role.select_option('SOLUTION_ARCHITECT')
                await expect(page.get_by_role('button',name='Full Control',exact=False)).to_be_enabled()
                assert await page.locator('label').filter(has_text='3. Select module').locator('select').locator('option:checked').inner_text()=='025 · SOW & GSD Workspace'
                await page.get_by_role('button',name='Full Control',exact=False).click()
                await page.get_by_label('Required reason',exact=True).fill('Approve Solution Architect workspace access')
                await page.get_by_role('button',name='Validate changes',exact=True).click()
                await page.get_by_role('button',name='Publish new policy version',exact=True).click()
                await expect(page.get_by_text('Publication needs review' if mismatch else 'Publication verified',exact=True)).to_be_visible()
                await expect(role).to_be_enabled()
                assert len(published)==1
                assert published[0]['changes'][0]['moduleCode']=='025'
                assert published[0]['changes'][0]['roleCode']=='SOLUTION_ARCHITECT'
                if mismatch: await expect(page.get_by_text('Publication verified',exact=True)).to_have_count(0)
                await page.close()
            await browser.close()
        print('MODULE_CATALOG_BROWSER=PASS module_id full_control verified_receipt mismatch_blocks_success no_duplicate_write')
    finally: server.terminate();server.wait()
asyncio.run(main())
