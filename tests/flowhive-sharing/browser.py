"""Synthetic request/interaction checks of the actual FlowHive Center. Never calls a tenant."""
import asyncio, json, os, re
from pathlib import Path
from urllib.parse import urlparse
from playwright.async_api import async_playwright
# Typography matches the application root in src/frontend/project-time-web/src/styles.css.
BUNDLE=Path(os.getenv('FLOWHIVE_SHARING_BUNDLE','/tmp/flowhive-sharing-test'))
OUT=Path(os.getenv('FLOWHIVE_SHARING_EVIDENCE','/tmp/flowhive-sharing-evidence'));OUT.mkdir(exist_ok=True,parents=True)
A='11111111-1111-4111-8111-111111111111';B='22222222-2222-4222-8222-222222222222';P='33333333-3333-4333-8333-333333333333';ACTOR='44444444-4444-4444-8444-444444444444'
def plan(pid):
    return dict(projectId=pid,projectCode='SYNTHETIC',projectName='Communications platform upgrade',customerName='Example customer',planId=P,planName='Reviewed project plan',
      projectStartDate='2026-09-08',projectEndDate='2026-10-15',tasks=[dict(wbsNumber=f'1.{i}',clientTaskId=f'task-{i}',name=name,phase='Plan',isSummary=False,isMilestone=False,status='blocked' if i==2 else 'not_started',durationWorkingDays=1,remainingEffortHours=2,percentComplete=0,citationIds=[1]) for i,name in enumerate(['Confirm delivery scope','Review site readiness','Approve transition schedule'],1)],dependencies=[],assignments=[],milestones=[])
async def main():
 async with async_playwright() as p:
  browser=await p.chromium.launch(headless=True,args=['--no-sandbox'])
  cases=0
  async def run_case(mode='success',theme='light',width=1440):
    nonlocal cases
    page=await browser.new_page(viewport={'width':width,'height':1100});page.set_default_timeout(7000)
    state={'enabled':False,'writes':[],'errors':[],'hold':asyncio.Event(),'started':asyncio.Event(),'denied':mode=='error'}
    page.on('pageerror',lambda e:state['errors'].append(str(e)))
    page.on('dialog',lambda dialog:dialog.accept())
    async def route_handler(route):
      req=route.request;path=urlparse(req.url).path
      if path=='/':
       return await route.fulfill(status=200,content_type='text/html',body=f'<html data-theme="{theme}"><head><title>Fixture</title><style>html{{font-family:Inter,ui-sans-serif,system-ui,-apple-system,BlinkMacSystemFont,"Segoe UI",sans-serif}}body{{margin:0;padding:16px;background:transparent;box-sizing:border-box}}</style></head><body><div id="root"></div></body></html>')
      if req.method not in ('GET','HEAD'):state['writes'].append((req.method,path,req.post_data_json))
      result={};status=200
      if path=='/api/identity/profile':result={'userId':ACTOR,'displayName':'Synthetic PM','email':'pm@example.invalid'}
      elif path.endswith('/capabilities'):result={'databaseMutationEnabled':True,'capabilities':[]}
      elif path.endswith('/portfolio'):result={'projects':[{'projectId':pid,'projectCode':name,'projectName':'Communications platform upgrade' if pid==A else 'Other project','customerName':'Example customer','status':'active','documentCount':1,'projectManagerName':'Synthetic PM'} for pid,name in [(A,'PROJECT-A'),(B,'PROJECT-B')]],'tasks':[],'assignments':[],'summary':{},'access':{'displayName':'Synthetic PM','effectiveUserId':ACTOR,'isViewAs':mode=='viewas'}}
      elif path=='/api/project-flowhive/plans':result={'plans':[{'planId':P,'projectId':A,'planName':'Reviewed plan','baselineVersion':None if mode=='nobaseline' else 2,'currentVersion':2}]}
      elif path.endswith('/enterprise'):
       pid=path.split('/')[4];seed=plan(pid)
       result={'project':{'projectId':pid,'customerName':'Example customer'},'access':{'canManage':True,'canEditPlanner':True,'canAdministerPlanner':mode not in ('viewas','engineer'),'canShare':mode!='engineer','isViewAs':mode=='viewas'},'workingCopy':{'plan':seed,'rowVersion':P,'workingRevision':1,'validation':{'valid':True,'issues':[]},'schedule':{'valid':True,'projectFinishDate':'2026-10-08','tasks':[{'wbsNumber':f'1.{i}','endDate':'2026-09-15' if i<3 else '2026-10-08','isCritical':True} for i in range(1,4)]}},'controls':{'approvedBudget':'invalid unrelated value','financialNotes':'Unsaved private fixture note','customerSharingEnabled':state['enabled'] if pid==A else False},'raidItems':[],'statusReports':[],'customerShares':[{'shareId':P,'versionNumber':2,'active':True,'expiresAt':'2026-11-01T00:00:00Z','revokedAt':None,'accessCount':1}] if mode=='existinglinks' else [],'sowEvidence':[]}
      elif path.endswith('/customer-sharing/enable'):
       state['started'].set()
       if mode=='delayed':await state['hold'].wait()
       if state['denied']:status=400;result={'message':'Synthetic validation failure.','correlationId':'PP-FIXTURE'}
       else:state['enabled']=True;result={'projectId':A,'customerSharingEnabled':True,'customerLinkCreated':False,'stateChanged':True,'message':'Customer sharing enabled. No link was created or sent.'}
      elif path.endswith('/documents/readiness'):result={'projectId':path.split('/')[4],'preparation':{'status':'ready','readyCount':1,'totalCount':1,'documents':[]}}
      elif path.endswith('/ai-planner/automation'):result={'projectId':path.split('/')[4],'enabled':False,'canManage':False,'status':'disabled','defaults':{'enabled':False}}
      elif path.endswith('/readiness'):result={'ready':True,'status':'ready'}
      elif path.startswith('/api/project-financials/'):result={'status':'financial_data_unavailable','project':None}
      elif path.endswith('/psa'):result={'meetings':[],'raidHistory':[],'decisions':[],'reminderPreferences':{'enabled':False}}
      await route.fulfill(status=status,content_type='application/json',body=json.dumps(result))
    await page.route('**/*',route_handler)
    await page.goto('http://fixture.invalid/')
    await page.evaluate("localStorage.setItem('projectPulseAuthSession',JSON.stringify({sessionToken:'SYNTHETIC-NOT-A-CREDENTIAL'}))")
    await page.add_style_tag(content=BUNDLE.joinpath('app.css').read_text());await page.add_script_tag(content=BUNDLE.joinpath('app.js').read_text());await page.evaluate('window.mountSharing()')
    try:
      await page.locator('.flowhive-scope-toolbar select').select_option(A)
    except Exception:
      print('BROWSER_DIAGNOSTICS',state['errors'],await page.locator('body').inner_text(),flush=True)
      raise
    await page.get_by_role('button',name='Project home',exact=True).click()
    card=page.locator('.flowhive-home-sharing');await card.get_by_text('New links off · 1 active link' if mode=='existinglinks' else 'Off · internal only',exact=True).wait_for()
    button=card.get_by_role('button',name='Enable customer sharing for this project',exact=True)
    if mode=='existinglinks':
      assert await card.get_by_text('Previously created links are still active.',exact=True).count()==1
      assert await card.get_by_role('button',name='Revoke',exact=True).is_enabled()
      assert state['writes']==[];cases+=1
    elif mode in ('viewas','engineer'):
      assert await button.is_disabled();assert state['writes']==[];cases+=1
    else:
      await button.click()
      if mode=='delayed':
        await state['started'].wait();assert await card.get_by_role('button',name='Enabling sharing…').is_disabled()
        await page.locator('.flowhive-scope-toolbar select').select_option(B)
        state['hold'].set();await page.wait_for_timeout(200)
        await card.get_by_text('Off · internal only',exact=True).wait_for()
        assert not await page.get_by_text('Customer sharing enabled. No link was created or sent.',exact=True).count();cases+=1
      elif mode=='error':
        await card.get_by_role('alert').wait_for();assert 'PP-FIXTURE' in await card.inner_text()
        assert await card.get_by_text('Off · internal only',exact=True).count()==1
        assert await button.is_enabled();cases+=1
      else:
        await card.get_by_text('Enabled · no active links',exact=True).wait_for()
        assert state['writes']==[('POST',f'/api/project-flowhive/projects/{A}/customer-sharing/enable',{})]
        await card.locator('summary').click()
        create=card.get_by_role('button',name='Create customer link',exact=True)
        assert await create.is_disabled()
        if mode=='nobaseline':assert await card.get_by_text('A reviewed baseline is needed before creating a link.',exact=True).count()==1
        else:
          await card.get_by_label(re.compile('Reviewed baseline',re.I)).select_option(P)
          assert await create.is_enabled()
        cases+=1
    contrast = await page.evaluate("""() => {
      const rgb = value => (value.match(/[\d.]+/g) || []).map(Number);
      const luminance = color => rgb(color).slice(0,3).map(x=>x/255).map(x=>x<=.04045?x/12.92:((x+.055)/1.055)**2.4).reduce((n,x,i)=>n+x*[.2126,.7152,.0722][i],0);
      return [...document.querySelectorAll('.flowhive-command-center h3,.flowhive-command-center h4,.flowhive-command-center .flowhive-home-eyebrow,.flowhive-command-center .flowhive-home-badge')].filter(el=>el.getClientRects().length).map(el=>{
        let current=el, background='rgb(255,255,255)';
        while(current){const value=getComputedStyle(current).backgroundColor;const c=rgb(value);if(c.length===3 || c[3]===1){background=value;break;}current=current.parentElement;}
        const a=luminance(getComputedStyle(el).color),b=luminance(background);
        return {text:el.textContent,ratio:(Math.max(a,b)+.05)/(Math.min(a,b)+.05)};
      });
    }""")
    assert all(item['ratio']>=4.5 for item in contrast),contrast
    assert state['errors']==[],state['errors']
    assert await page.evaluate('document.documentElement.scrollWidth <= window.innerWidth + 1'), 'page-level horizontal overflow'
    if mode=='success':
      # Exercise task filters and their reset; button semantics remain usable by keyboard.
      await page.get_by_role('button',name='Review priorities').click()
      assert await page.get_by_label(re.compile('Show work',re.I)).input_value()=='attention'
      await page.get_by_label(re.compile('Show work',re.I)).select_option('mine')
      assert await page.get_by_text('No open tasks match this filter.',exact=True).count()==1
      await page.get_by_role('button',name='Show all open work',exact=True).click()
      assert await page.locator('.flowhive-home-work tbody tr').count()==3
      if width==1440:await card.locator('summary').click()
      await page.locator('.flowhive-command-center').screenshot(path=str(OUT/f'command-center-{theme}-{width}.png'))
    await page.close()
  for mode in ['success','error','delayed','viewas','engineer','nobaseline','existinglinks']:await run_case(mode)
  await run_case('success','dark',1440)
  await run_case('success','light',390)
  await run_case('success','dark',390)
  await browser.close()
  print(f'FLOWHIVE_SHARING_BROWSER_CASES={cases}; LIVE_REQUESTS=0; RESULT=PASS')
asyncio.run(main())
