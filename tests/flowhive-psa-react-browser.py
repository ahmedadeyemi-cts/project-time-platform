"""Real React component integration with isolated, explicitly synthetic API fixtures.

This is not live-provider or authenticated UAT evidence. The test exercises the
actual FlowHive component's network requests, view state and saved-result readback.
Run the companion browser host first. No application credentials are accepted.
"""
import asyncio
import copy
import json
import os
from pathlib import Path
from types import SimpleNamespace
from urllib.parse import urlparse
from playwright.async_api import async_playwright

A = '11111111-1111-4111-8111-111111111111'
B = '22222222-2222-4222-8222-222222222222'
RUN = '33333333-3333-4333-8333-333333333333'
V1 = '44444444-4444-4444-8444-444444444444'
V2 = '55555555-5555-4555-8555-555555555555'
V3 = '88888888-8888-4888-8888-888888888888'
ACTOR = '66666666-6666-4666-8666-666666666666'
SAVED = '77777777-7777-4777-8777-777777777777'

def plan(project, name='Stored project task'):
    rows=[]
    for i,phase in enumerate(['Plan','Design','Implement','Validate','Release'],1):
        common={'clientTaskId':f'00000000-0000-4000-8000-{i:012d}', 'canonicalTaskId':None,
            'durationWorkingDays':1,'percentComplete':0,'remainingEffortHours':2,'status':'not_started',
            'isMilestone':False,'constraintType':'ASAP','constraintDate':None,'phase':phase,
            'description':'An explicitly synthetic test task.', 'detailedSteps':[], 'citationIds':[1],
            'priority':'normal','comments':'','notes':''}
        rows.append({**common,'wbsNumber':str(i),'parentWbsNumber':None,'isSummary':True,'name':phase,'durationWorkingDays':0,'remainingEffortHours':0})
        rows.append({**common,'wbsNumber':f'{i}.1','parentWbsNumber':str(i),'isSummary':False,'name':name+' '+phase})
    return {'projectId':project,'projectCode':'TEST-A' if project==A else 'TEST-B','projectName':'Project A' if project==A else 'Project B',
        'customerName':'Synthetic customer','planName':'Stored plan A' if project==A else 'Stored plan B','revisionLabel':'fixture',
        'projectStartDate':'2026-09-08','projectEndDate':'2026-10-30','tasks':rows,'assignments':[],'dependencies':[],
        'milestones':[],'gsdVersion':'fixture-gsd','sowVersion':'fixture-sow','notes':''}

def schedule(seed):
    return {'valid':True,'status':'calculated','projectStartDate':seed['projectStartDate'],'projectFinishDate':'2026-09-18',
        'projectTargetEndDate':seed['projectEndDate'],'scheduledWorkingDays':9,'plannedHours':10,'issues':[],
        'tasks':[{**t,'startDate':seed['projectStartDate'],'endDate':'2026-09-18','earliestStartIndex':0,'totalFloatWorkingDays':0,'isCritical':True}
                 for t in seed['tasks']]}

async def main(readback_mode=None):
    async with async_playwright() as p:
        launch={'headless':True}
        if os.getenv('CHROME_PATH'):launch['executable_path']=os.environ['CHROME_PATH']
        browser=await p.chromium.launch(**launch)
        try:
            state={'plans':{A:plan(A),B:plan(B)},'versions':{A:V1,B:V1},'run':None,'posts':[],
                   'readback_mode':readback_mode,'complete':False,'late_schedule':False,'delayed':None,'view_as':False,'errors':[],'polls':0}
            page=await browser.new_page(viewport={'width':1440,'height':1000})
            page.set_default_timeout(5000)
            page.on('pageerror',lambda error:state['errors'].append(str(error)))
            await page.add_init_script("try {localStorage.setItem('projectPulseAuthSession',JSON.stringify({sessionToken:'SYNTHETIC-TEST-ONLY'}));} catch {}")
            async def api(route):
                request=route.request;path=urlparse(request.url).path
                body={};status=200
                if path=='/api/identity/profile':body={'userId':ACTOR,'displayName':'Synthetic PM','email':'pm@example.invalid'}
                elif path.endswith('/capabilities'):body={'capabilities':[],'databaseMutationEnabled':True}
                elif path.endswith('/portfolio'):
                    body={'projects':[{'projectId':pid,'projectCode':'TEST-'+label,'projectName':'Project '+label,'customerName':'Synthetic customer',
                        'projectManagerName':'Synthetic PM','startDate':'2026-09-08','endDate':'2026-10-30','taskCount':5,'assignmentCount':0,'status':'active'} for pid,label in [(A,'A'),(B,'B')]],
                        'tasks':[],'assignments':[],'summary':{'projectCount':2,'taskCount':10},'access':{'displayName':'Synthetic PM'}}
                elif path.endswith('/readiness'):body={'ready':True,'status':'ready'}
                elif path=='/api/project-flowhive/plans':body={'plans':[{'planId':SAVED,'projectId':A,'planName':'Reviewed immutable fixture','currentVersion':3}]}
                elif path==f'/api/project-flowhive/plans/{SAVED}':
                    frozen=plan(A,'Immutable reviewed task');frozen['planId']=SAVED
                    body={'plan':frozen,'summary':{'projectId':A,'currentVersion':3},'schedule':schedule(frozen),'validation':{'valid':True,'issues':[]}}
                elif path.endswith('/enterprise'):
                    pid=path.split('/')[4];seed=state['plans'][pid]
                    completed = state['run'] and state['run'].get('terminal') and pid == A
                    if completed and state['readback_mode']=='newer_revision':
                        seed=plan(A,'Newer remote task');seed['projectStartDate']='2026-09-10'
                        state['plans'][A]=seed;state['versions'][A]=V3
                    body={'project':{'projectId':pid,'customerName':'Synthetic customer'},
                        'access':{'canEditPlanner':not state['view_as'],'canAdministerPlanner':not state['view_as'],'canAdoptBaseline':not state['view_as'],'isViewAs':state['view_as']},
                        'workingCopy':{'plan':seed,'schedule':schedule(seed),'validation':{'valid':True,'issues':[]},'workingRevision':1,'rowVersion':state['versions'][pid]},
                        'raidItems':[],'statusReports':[],'shares':[],'sowEvidence':{'items':[]},'controls':{}}
                    if completed and state['readback_mode']=='unavailable':
                        status=503;body={'message':'Synthetic saved-working-copy readback failure'}
                    if completed and state['readback_mode']=='wrong_project':
                        body['workingCopy']['plan']={**seed,'projectId':B}
                elif path.startswith('/api/project-financials/'):body={'status':'financial_data_unavailable','project':None}
                elif path.endswith('/ai-planner/runs/latest'):body=state['run'] or {'runId':None,'terminal':True}
                elif path.endswith('/ai-planner/runs') and request.method=='POST':
                    posted=request.post_data_json;state['posts'].append(posted)
                    state['run']={'runId':RUN,'projectId':A,'terminal':False,'status':'processing','phase':'extract_scope','progressPercent':30,
                        'createdAt':'2026-09-06T21:00:00Z','deadlineAt':'2026-09-06T21:05:00Z','attemptCount':1,
                        'workingDraft':{'persisted':False},'blockers':[],'warnings':[]}
                    body=state['run'];status=202
                elif path.endswith('/cancel'):
                    state['run']={**state['run'],'terminal':True,'status':'needs_attention','phase':'cancelled'};body=state['run']
                elif f'/ai-planner/runs/{RUN}' in path:
                    state['polls']+=1
                    if state['complete']:
                        new=copy.deepcopy(state['posts'][-1]['plan']);new['tasks']=plan(A,'Generated unique task')['tasks']
                        state['plans'][A]=new;state['versions'][A]=V2
                        state['run']={**state['run'],'terminal':True,'status':'completed','phase':'working_draft_ready','plan':new,'schedule':schedule(new),
                            'workingDraft':{'persisted':True,'rowVersion':V2,'workingRevision':2},'validation':{'valid':True,'issues':[]}}
                    body=state['run'];status=200 if body['terminal'] else 202
                elif path.endswith('/schedule/calculate'):
                    posted=request.post_data_json
                    if state['late_schedule']:
                        state['delayed']=asyncio.Event()
                        if not offline:await state['delayed'].wait()
                    body=schedule(posted)
                    if state['late_schedule'] and offline:body={'__deferredSchedule':body}
                elif path.endswith('/working-copy') and request.method=='PUT':
                    posted=request.post_data_json;pid=path.split('/')[4]
                    assert posted['expectedRowVersion']==state['versions'][pid]
                    state['plans'][pid]=posted['plan'];state['versions'][pid]=V2
                    body={'rowVersion':V2,'workingRevision':2}
                else:
                    status=404;body={'message':'Unexpected isolated test API: '+path}
                await route.fulfill(status=status,content_type='application/json',body=json.dumps(body))
            offline = os.getenv('FLOWHIVE_OFFLINE_BUNDLE')
            if offline:
                async def fixture(_source, path, options):
                    class MemoryRoute:
                        def __init__(self):
                            self.request=SimpleNamespace(url=path,method=options.get('method','GET'),
                                post_data_json=json.loads(options.get('body') or '{}'))
                        async def fulfill(self,**response):self.response=response
                    route=MemoryRoute();await api(route);return route.response
                await page.expose_binding('__flowhiveFixture',fixture)
            else:await page.route('**/api/**',api)
            async def reload_page():
                if not offline:
                    await page.goto(os.getenv('FLOWHIVE_BROWSER_URL','http://127.0.0.1:5188/__flowhive_test'));return
                # Offline in-memory adapter: no network policy is changed and no application server is contacted.
                await page.goto('about:blank')
                await page.set_content('<!doctype html><html><body><div id="root"></div></body></html>')
                await page.evaluate("""() => {
                    const values = new Map([['projectPulseAuthSession', JSON.stringify({sessionToken:'SYNTHETIC-TEST-ONLY'})]]);
                    Object.defineProperty(window,'localStorage',{configurable:true,value:{getItem:k=>values.get(k)||null,setItem:(k,v)=>values.set(k,v),removeItem:k=>values.delete(k)}});
                    window.fetch = (path, options={}) => new Promise((resolve,reject) => {
                        const abort=()=>reject(new DOMException('Request aborted','AbortError'));
                        if(options.signal?.aborted){abort();return;}
                        options.signal?.addEventListener('abort',abort,{once:true});
                        window.__flowhiveFixture(String(path),{method:options.method||'GET',body:options.body}).then(result=>{
                            options.signal?.removeEventListener('abort',abort);
                            const deferred=JSON.parse(result.body).__deferredSchedule;
                            const finish=()=>resolve(new Response(deferred ? JSON.stringify(deferred) : result.body,{status:result.status,headers:{'Content-Type':result.content_type}}));
                            if(deferred)window.__flowhiveDeferredSchedule=finish;else finish();
                        },reject);
                    });
                }""")
                await page.add_style_tag(content=(Path(offline)/'app.css').read_text())
                await page.add_script_tag(content=(Path(offline)/'app.js').read_text())
            await reload_page()
            await page.get_by_role('button',name='Planner',exact=True).click()
            await page.get_by_label('Plan name',exact=True).wait_for()
            await page.get_by_label('Start date',exact=True).fill('2026-09-10')
            await page.wait_for_timeout(80)
            if state['errors']: raise AssertionError(state['errors'])
            await page.get_by_role('button',name='AI Planner',exact=True).click()
            await page.wait_for_function("document.querySelector('.flowhive-planner-operation') || document.body.textContent.includes('extract scope') || document.body.textContent.includes('Extract Scope')")
            assert len(state['posts'])==1
            assert state['posts'][0]['plan']['projectStartDate']=='2026-09-10'
            assert state['posts'][0]['expectedWorkingRowVersion']==V1
            assert state['posts'][0]['hasWorkingCopyExpectation'] is True
            print('PASSED: actual React start posts edited dates and exact working-copy revision',flush=True)
            state['complete']=True
            if readback_mode:
                await page.get_by_text('Generation saved a draft; saved work-breakdown readback still requires attention.',exact=True).wait_for()
                assert await page.locator('input[value="Stored project task Plan"]').count()==1
                assert not await page.locator('input[value="Generated unique task Plan"]').count()
                assert 'work breakdown is saved and reloaded' not in await page.locator('body').inner_text()
                assert len(state['posts'])==1
                print(f'PASSED: {readback_mode} does not replace the displayed WBS or claim verified readback',flush=True)
                state['readback_mode']=None
                page.once('dialog',lambda dialog:dialog.accept())
                await page.get_by_role('button',name='Load working copy',exact=True).click()
                task='Newer remote task Plan' if readback_mode=='newer_revision' else 'Generated unique task Plan'
                await page.locator(f'input[value="{task}"]').wait_for()
                assert len(state['posts'])==1
                assert await page.locator('.flowhive-work-row input[type=date]').first.input_value()=='2026-09-10'
                assert not state['errors'],state['errors']
                print(f'PASSED: {readback_mode} recovers by reading the saved copy without another AI request',flush=True)
                return
            await page.locator('input[value="Generated unique task Plan"]').wait_for()
            assert await page.get_by_label('Start date',exact=True).input_value()=='2026-09-10'
            assert await page.locator('.flowhive-work-row input[type=date]').first.input_value()=='2026-09-10'
            assert not await page.get_by_role('heading',name='Project milestones',exact=True).count()
            await reload_page();await page.get_by_role('button',name='Planner',exact=True).click()
            await page.locator('input[value="Generated unique task Plan"]').wait_for()
            assert len(state['posts'])==1
            assert await page.locator('.flowhive-work-row input[type=date]').first.input_value()=='2026-09-10'
            print('PASSED: completed WBS and dates survive reload without another inference request',flush=True)
            # An old schedule request must not replace a different project's view or leave it busy.
            state['late_schedule']=True
            await page.get_by_role('button',name='Calculate schedule',exact=True).click()
            for _ in range(500):
                if state['delayed'] is not None:break
                await asyncio.sleep(.01)
            assert state['delayed'] is not None,(state['errors'],await page.locator('body').inner_text())
            await page.get_by_role('combobox',name='Canonical project',exact=True).select_option(B)
            await page.get_by_label('Plan name',exact=True).fill('Unsaved B edit')
            if offline:await page.evaluate('window.__flowhiveDeferredSchedule()')
            else:state['delayed'].set()
            await page.wait_for_timeout(200)
            assert await page.get_by_label('Plan name',exact=True).input_value()=='Unsaved B edit'
            assert not await page.get_by_role('button',name='Timeline & risk',exact=True).get_attribute('aria-pressed')=='true'
            print('PASSED: late schedule cannot overwrite a newly selected project',flush=True)
            page.once('dialog',lambda dialog:dialog.accept())
            await page.get_by_role('combobox',name='Canonical project',exact=True).select_option(A)
            await page.get_by_role('combobox',name='Saved FlowHive plan',exact=True).select_option(SAVED)
            await page.locator('input[value="Immutable reviewed task Plan"]').wait_for()
            await page.wait_for_timeout(200)
            assert await page.locator('input[value="Immutable reviewed task Plan"]').count()==1
            print('PASSED: immutable version is not replaced by the working-copy refresh',flush=True)
            await page.get_by_role('button',name='Load working copy',exact=True).click()
            await page.locator('input[value="Generated unique task Plan"]').wait_for()
            assert not await page.locator('input[value="Immutable reviewed task Plan"]').count()
            print('PASSED: explicit working-copy reload exits immutable-version view',flush=True)
            state['view_as']=True
            await page.evaluate("window.dispatchEvent(new CustomEvent('projectpulse:view-as-changed'))")
            await page.wait_for_timeout(350)
            assert await page.get_by_role('button',name='AI Planner',exact=True).is_disabled()
            assert not state['errors'],state['errors']
            print('PASSED: changed identity clears previous content and blocks View-As editing',flush=True)
        except Exception:
            print('FAILURE CONTEXT', state['errors'], await page.locator('body').inner_text(), flush=True)
            raise
        finally:await browser.close()

async def run_all():
    for mode in (None,'unavailable','newer_revision','wrong_project'):
        await main(mode)

if __name__=='__main__':asyncio.run(run_all())
