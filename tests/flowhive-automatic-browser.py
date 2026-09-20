"""Actual automatic-planning React component with synthetic API responses; no live AI or application credentials."""
import asyncio
import copy
import json
import os
from pathlib import Path
from playwright.async_api import async_playwright, expect

async def main():
    bundle=Path(os.environ.get('FLOWHIVE_AUTOMATIC_BUNDLE','/tmp/flowhive-automatic-test'))
    async with async_playwright() as p:
        options={'headless':True}
        if os.getenv('CHROME_PATH'): options['executable_path']=os.environ['CHROME_PATH']
        browser=await p.chromium.launch(**options)
        try:
            page=await browser.new_page(viewport={'width':1280,'height':1000})
            page.set_default_timeout(8000)
            await page.clock.install(time=__import__('datetime').datetime.fromisoformat('2026-09-20T12:00:30+00:00'))
            state={'projectId':'project-a','enabled':False,'rowVersion':'revision-1','status':'disabled','message':'Automatic planning is off.',
                   'canManage':True,'defaults':{'enabled':False,'rowVersion':'default-1','canManage':False}}
            writes=[];errors=[];hold=False;held=asyncio.Event();release=asyncio.Event();unavailable=False
            page.on('pageerror',lambda e:errors.append(str(e)))
            async def route(r):
                nonlocal state
                if '/api/' in r.request.url:
                    if r.request.method=='PUT':
                        data=r.request.post_data_json;writes.append((r.request.url,data))
                        if r.request.url.endswith('/default'): state['defaults'].update(enabled=data['enabled'],rowVersion='default-2')
                        else: state.update(enabled=data['enabled'],rowVersion='revision-2',status='waiting_documents',message='Waiting for documents. You can leave this page.')
                    body=copy.deepcopy(state)
                    if 'project-b' in r.request.url: body.update(projectId='project-b',enabled=False,status='disabled',message='Project B is off.',runId=None,phases=None)
                    if hold and 'project-a' in r.request.url and r.request.method=='GET': held.set();await release.wait()
                    await r.fulfill(status=503 if unavailable else 200,content_type='application/json',body=json.dumps({'message':'Automatic planning is not installed yet.'} if unavailable else body));return
                if r.request.url.endswith('/app.js'): await r.fulfill(content_type='text/javascript',body=(bundle/'app.js').read_text());return
                if r.request.url.endswith('/app.css'): await r.fulfill(content_type='text/css',body=(bundle/'app.css').read_text());return
                await r.fulfill(content_type='text/html',body='<meta charset="UTF-8"><div id="root"></div><link rel="stylesheet" href="/app.css"><script src="/app.js"></script>')
            await page.route('**/*',route)
            await page.goto('http://flowhive.fixture/')
            toggle=page.get_by_role('checkbox',name='Automatically create the first AI plan')
            await toggle.wait_for();assert await toggle.is_enabled()
            assert await page.get_by_text('Administrator default for new projects').count()==0
            await toggle.click()
            await expect(toggle).to_be_checked()
            await page.get_by_text('Waiting for documents. You can leave this page.').wait_for()
            assert len(writes)==1 and writes[0][1]=={'enabled':True,'expectedVersion':'revision-1'}
            await page.clock.fast_forward(30000)
            assert len(writes)==1,'Polling must never start or update a plan'
            state.update(status='generating',runId='run-1',createdAt='2026-09-20T12:00:00Z',completedAt=None,
                         phases=[{'name':'Plan','number':1,'status':'completed','startedAt':'2026-09-20T12:00:00Z','completedAt':'2026-09-20T12:00:10Z','taskCount':3},
                                 {'name':'Design','number':2,'status':'processing','startedAt':'2026-09-20T12:00:10Z'}])
            await page.get_by_role('button',name='Check automatic plan status').click()
            await page.get_by_text('Stage 2 of 5: Design').wait_for()
            before=await page.get_by_role('timer',name='Design elapsed time',exact=True).inner_text()
            plan_before=await page.get_by_role('timer',name='Plan elapsed time',exact=True).inner_text()
            await page.clock.fast_forward(2100)
            assert before!=await page.get_by_role('timer',name='Design elapsed time',exact=True).inner_text()
            assert plan_before==await page.get_by_role('timer',name='Plan elapsed time',exact=True).inner_text()
            state.update(canManage=False);state['defaults']['canManage']=False
            await page.get_by_role('button',name='Check automatic plan status').click()
            await page.get_by_text('Your PM or an authorized administrator can change this setting.').wait_for()
            assert not await toggle.is_enabled() and len(writes)==1
            state.update(canManage=True,status='ready_for_review',message='Your first AI draft is ready for review.',completedAt='2026-09-20T12:01:00Z',
                         phases=[{'name':name,'number':i+1,'status':'completed','startedAt':'2026-09-20T12:00:00Z','completedAt':'2026-09-20T12:00:10Z','taskCount':3} for i,name in enumerate(['Plan','Design','Implement','Validate','Release'])]);state['defaults']['canManage']=True
            await page.get_by_role('button',name='Check automatic plan status').click()
            await page.get_by_role('button',name='Review working draft').click()
            assert await page.evaluate('window.draftLoads')==1
            await page.get_by_text('Administrator default for new projects',exact=True).click()
            await page.get_by_role('checkbox',name='Enable for new projects').click()
            await expect(page.get_by_role('checkbox',name='Enable for new projects')).to_be_checked()
            await page.get_by_role('checkbox',name='Enable for new projects').wait_for()
            assert len(writes)==2 and writes[-1][0].endswith('/default') and writes[-1][1]['expectedVersion']=='default-1'
            await page.set_viewport_size({'width':390,'height':844})
            assert await page.evaluate('document.documentElement.scrollWidth<=window.innerWidth'),'Mobile panel must not overflow'
            await page.screenshot(path='/tmp/flowhive-automatic-mobile.png',full_page=True)
            hold=True
            await page.get_by_role('button',name='Check automatic plan status').click();await held.wait()
            await page.evaluate("window.setAutomationProject('project-b')")
            await page.get_by_text('Project B is off.',exact=True).wait_for();release.set()
            assert await page.get_by_role('button',name='Review working draft').count()==0,'Late old-project status cannot replace the new project'
            unavailable=True
            await page.get_by_role('button',name='Check automatic plan status').click()
            await page.get_by_role('alert').filter(has_text='Automatic planning is not installed yet.').wait_for()
            assert not await toggle.is_enabled(),'Unavailable status must not enable a write'
            assert not errors,errors
            print('FLOWHIVE_AUTOMATIC_BROWSER=PASS pm_controls=verified admin_default=verified timers=verified readonly=verified stale_project=blocked polling_writes=0 mobile=verified')
        finally: await browser.close()

asyncio.run(main())
