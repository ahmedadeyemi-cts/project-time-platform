"""Offline React interaction checks. Synthetic calendar data; no tenant login or messages."""
import asyncio
import os
from pathlib import Path
from playwright.async_api import async_playwright

BUNDLE = Path('/tmp/flowhive-team-calendar-test')
async def main():
    async with async_playwright() as p:
        browser = await p.chromium.launch(headless=True, executable_path=os.getenv('PLAYWRIGHT_CHROMIUM_EXECUTABLE'), args=['--no-sandbox'])
        page = await browser.new_page(viewport={'width': 1440, 'height': 960})
        page.set_default_timeout(5000)
        errors=[]
        page.on('pageerror', lambda error: errors.append(str(error)))
        await page.route('**/*', lambda route: route.abort())
        await page.set_content('<div id="root"></div>')
        await page.add_style_tag(content=BUNDLE.joinpath('app.css').read_text())
        await page.add_script_tag(content=BUNDLE.joinpath('app.js').read_text())
        await page.evaluate('''() => {
          window.calls=[];
          window.props={projectId:'project-a', request:async path=>{
            window.calls.push(path);
            const query=new URL(path,'https://fixture.invalid').searchParams;
            const day=query.get('start');
            return {status:'partial',members:[
              {userId:'pm',displayName:'Fixture PM',role:'Project Manager',status:'available',intervals:[{start:day+'T10:00:00Z',end:day+'T11:00:00Z',status:'busy'}]},
              {userId:'ae',displayName:'Fixture AE',role:'Account Executive',status:'unknown',intervals:[]}
            ]};
          }};
          window.mountFlowHiveTeamCalendar(window.props);
        }''')
        await page.locator('tbody').get_by_text('Fixture PM', exact=True).wait_for()
        assert await page.get_by_text('Availability unknown', exact=True).count()==7
        assert '10:00' in await page.locator('.flowhive-calendar-slot').inner_text()
        first=await page.evaluate('window.calls.at(-1)')
        await page.get_by_role('button',name='Next week',exact=True).click()
        await page.wait_for_function('window.calls.length === 2')
        assert await page.evaluate('window.calls.at(-1)')!=first
        await page.get_by_label('View',exact=True).select_option('month')
        await page.wait_for_function('window.calls.length === 3')
        assert await page.locator('thead th').count()>=29
        await page.get_by_label('Team member',exact=True).select_option('ae')
        assert await page.locator('tbody tr').count()==1
        assert 'Fixture AE' in await page.locator('tbody').inner_text()
        await page.get_by_role('button',name='Refresh',exact=True).click()
        await page.wait_for_function('window.calls.length === 4')
        await page.get_by_label('Team member',exact=True).select_option('all')
        await page.screenshot(path='/tmp/flowhive-team-calendar-preview.png',full_page=True)
        # A transport that ignores AbortSignal must still not reintroduce a stale project.
        await page.evaluate('''() => {
          window.props={projectId:'slow-project',request:path=>new Promise(resolve=>{window.resolveSlow=resolve;})};
          window.mountFlowHiveTeamCalendar(window.props);
        }''')
        await page.wait_for_function('typeof window.resolveSlow === "function"')
        assert await page.get_by_text('Fixture PM',exact=True).count()==0
        await page.evaluate('''() => {
          window.props={projectId:'project-b',request:async()=>({status:'loaded',members:[{userId:'b',displayName:'New project member',role:'Engineer',status:'available',intervals:[]}]})};
          window.mountFlowHiveTeamCalendar(window.props);
        }''')
        await page.locator('tbody').get_by_text('New project member',exact=True).wait_for()
        await page.evaluate('window.resolveSlow({status:"loaded",members:[{userId:"leak",displayName:"Stale project member",status:"available",intervals:[]}]})')
        await page.evaluate('() => new Promise(resolve => requestAnimationFrame(resolve))')
        assert await page.get_by_text('Stale project member',exact=True).count()==0
        await page.evaluate('''() => window.mountFlowHiveTeamCalendar({projectId:'error-project',request:async()=>{throw new Error('Synthetic calendar outage');}})''')
        await page.get_by_text('Synthetic calendar outage',exact=True).wait_for()
        assert await page.get_by_text('New project member',exact=True).count()==0
        await page.evaluate("""() => {
          window.openedTask=null;
          const today=new Date().toISOString().slice(0,10);
          window.mountFlowHiveOverview({userId:'engineer',dirty:true,onOpenTask:wbs=>window.openedTask=wbs,
            plan:{tasks:[{clientTaskId:'t1',wbsNumber:'1.1',name:'Configure test service',status:'blocked'},
                         {clientTaskId:'t2',wbsNumber:'1.2',name:'Verify test service',status:'not_started'}],
                  assignments:[{taskWbs:'1.1',resourceUserId:'engineer',resourceDisplayName:'Fixture engineer'}]},
            schedule:{valid:true,projectFinishDate:today,tasks:[{wbsNumber:'1.1',endDate:today,isCritical:true}]}});
        }""")
        await page.get_by_role('heading',name='What needs attention').wait_for()
        await page.get_by_role('button',name='My work 1',exact=True).click()
        assert await page.locator('tbody tr').count()==1
        await page.get_by_role('button',name='1.1 · Configure test service',exact=True).click()
        assert await page.evaluate('window.openedTask')=='1.1'
        await page.get_by_role('button',name='Unassigned 1',exact=True).click()
        assert 'Verify test service' in await page.locator('tbody').inner_text()
        await page.screenshot(path='/tmp/flowhive-delivery-overview-preview.png',full_page=True)
        assert not errors, errors
        await browser.close()
        print('FLOWHIVE_TEAM_CALENDAR_BROWSER=PASS (navigation, unknown state, member filter, refresh, race, failure)')

asyncio.run(main())
