"""Actual FlowHive Center; every request is intercepted with synthetic fixtures."""
import asyncio,json,os,re
from pathlib import Path
from urllib.parse import urlparse
from playwright.async_api import async_playwright
A='11111111-1111-4111-8111-111111111111';B='22222222-2222-4222-8222-222222222222'
P='33333333-3333-4333-8333-333333333333';PM='44444444-4444-4444-8444-444444444444'
ENGINEER='55555555-5555-4555-8555-555555555555';CONTACT='66666666-6666-4666-8666-666666666666';VERSION='77777777-7777-4777-8777-777777777777'
BUNDLE=Path('/tmp/flowhive-sharing-test');OUT=Path('/tmp/flowhive-sharing-evidence');OUT.mkdir(exist_ok=True)
def seed(pid):
 return dict(projectId=pid,planId=P,projectCode='FIXTURE',projectName='Communications upgrade',customerName='Synthetic customer',planName='Project working plan',revisionLabel='fixture',projectStartDate='2026-09-28',projectEndDate='',sourceKind='manual',
  tasks=[dict(clientTaskId='88888888-8888-4888-8888-888888888888',canonicalTaskId=None,wbsNumber='1',name='Review prerequisites',description='Synthetic test task',durationWorkingDays=2,isMilestone=False,isSummary=False,constraintType='ASAP',constraintDate='',percentComplete=0,remainingEffortHours=8,status='not_started',phase='Plan',citationIds=[1],detailedSteps=['Review','Confirm'])],
  dependencies=[],assignments=[dict(taskWbs='1',resourceUserId=PM,resourceDisplayName='Project Manager',plannedHours=8,allocationPercent=50)],milestones=[])
def timing(pid):return dict(projectId=pid,valid=True,projectFinishDate='2026-09-29',criticalTaskCount=1,scheduledWorkingDays=2,plannedHours=8,tasks=[dict(wbsNumber='1',name='Review prerequisites',startDate='2026-09-28',endDate='2026-09-29',durationWorkingDays=2,earliestStartIndex=0,latestStartIndex=0,totalFloatWorkingDays=0,freeFloatWorkingDays=0,isCritical=True)],issues=[],calendarMode='weekday_preview_module_057_not_applied')
async def main():
 async with async_playwright() as pw:
  browser=await pw.chromium.launch(headless=True,executable_path=os.getenv('FLOWHIVE_CHROMIUM_PATH') or None,args=['--no-sandbox','--disable-dev-shm-usage'])
  for mode,theme,width in [('success','light',1440),('error','light',1440),('readonly','dark',1440),('limit','light',1440),('race','light',1440),('conflict','light',1440),('success','dark',390)]:
   page=await browser.new_page(viewport={'width':width,'height':1100});page.set_default_timeout(7000)
   state={'writes':[],'errors':[],'plans':{A:seed(A),B:seed(B)},'version':P,'contacts':[dict(projectContactId=CONTACT,displayName='Customer IT owner',email='customer@example.invalid',phone='555-0101',title='IT Director',organization='Customer',contactKind='customer',isActive=True,rowVersion=P)],'meetings':[]}
   if mode=='limit':state['contacts']=[dict(state['contacts'][0],projectContactId=f'90000000-0000-4000-8000-{i:012d}',displayName=f'Contact {i}',email=f'contact{i}@example.invalid') for i in range(15)]
   page.on('pageerror',lambda e:state['errors'].append(str(e)));page.on('dialog',lambda dialog:dialog.accept())
   async def route_handler(route):
    req=route.request;path=urlparse(req.url).path
    if path=='/':return await route.fulfill(status=200,content_type='text/html',body=f'<html data-theme="{theme}"><style>html{{font-family:Inter,system-ui,sans-serif}}body{{margin:0;padding:12px;box-sizing:border-box}}</style><div id="root"></div></html>')
    status=200;body={}
    if req.method not in ('GET','HEAD'):state['writes'].append((req.method,path,req.post_data_json))
    if path=='/api/identity/profile':body={'userId':PM,'displayName':'Project Manager','email':'pm@example.invalid'}
    elif path.endswith('/capabilities'):body={'databaseMutationEnabled':True,'capabilities':[]}
    elif path.endswith('/portfolio'):body={'projects':[{'projectId':pid,'projectCode':code,'projectName':'Communications upgrade' if pid==A else 'Other project','customerName':'Synthetic customer','status':'active','documentCount':1,'projectManagerName':'Project Manager'} for pid,code in [(A,'A'),(B,'B')]],'tasks':[],'assignments':[],'summary':{},'access':{'effectiveUserId':PM,'displayName':'Project Manager'}}
    elif path=='/api/project-flowhive/plans':body={'plans':[{'planId':P,'projectId':A,'planName':'Reviewed fixture','baselineVersion':2,'currentVersion':2}]}
    elif path.endswith('/enterprise'):
     pid=path.split('/')[4];body={'project':{'projectId':pid,'customerName':'Synthetic customer'},'access':{'canManage':mode!='readonly','canEditPlanner':mode!='readonly','canAdministerPlanner':mode!='readonly','canShare':mode!='readonly','isViewAs':mode=='readonly'},'workingCopy':{'plan':state['plans'][pid],'rowVersion':state['version'],'workingRevision':2,'validation':{'valid':True,'issues':[]},'schedule':timing(pid)},'controls':{},'raidItems':[],'statusReports':[],'customerShares':[],'sowEvidence':[]}
    elif path.endswith('/collaboration'):
     pid=path.split('/')[4];body={'projectId':pid,'ready':True,'canManage':mode!='readonly','maxActiveContacts':15,'liveInvitationsAvailable':False,'team':[{'userId':PM,'displayName':'Project Manager','email':'pm@example.invalid','role':'Project Manager'},{'userId':ENGINEER,'displayName':'Engineer Two','email':'engineer@example.invalid','role':'Project team'}] if pid==A else [],'contacts':state['contacts'] if pid==A else [],'meetingDrafts':state['meetings'] if pid==A else []}
    elif path.endswith('/schedule/calculate'):
     body=timing(A);body['tasks'][0]['endDate']=req.post_data_json['tasks'][0].get('estimatedFinishDate') or '2026-09-29'
    elif path.endswith('/working-copy'):
     payload=req.post_data_json
     if mode=='race' and len([w for w in state['writes'] if w[1].endswith('/working-copy')])==1:await asyncio.sleep(1)
     if mode=='conflict':status=409;body={'message':'Another editor saved a newer working copy. Reload before saving.','issues':[],'stateChanged':False}
     elif mode=='error':status=400;body={'message':'A field needs correction.','issues':[{'path':'$.plan.tasks[0].durationWorkingDays','message':'Working-day duration must be a whole number.','severity':'error'}],'correlationId':'FIELD-FIXTURE'}
     else:state['plans'][A]=payload['plan'];state['version']=VERSION;body={'rowVersion':VERSION,'workingRevision':3,'schedule':timing(A),'validation':{'valid':True,'issues':[]},'stateChanged':True};body['schedule']['tasks'][0]['endDate']=payload['plan']['tasks'][0].get('estimatedFinishDate') or '2026-09-29'
    elif path.endswith('/contacts'):
     contact=dict(req.post_data_json,projectContactId='99999999-9999-4999-8999-999999999999',rowVersion=VERSION);state['contacts'].append(contact);body={'projectId':A,'projectContactId':contact['projectContactId'],'rowVersion':VERSION,'accountCreated':False,'invitationSent':False}
    elif path.endswith('/meeting-drafts'):
     draft=dict(req.post_data_json,meetingDraftId=VERSION,status='draft');state['meetings'].append(draft);body={'projectId':A,'meetingDraftId':VERSION,'invitationSent':False,'status':'draft'}
    elif path.endswith('/documents/readiness'):body={'projectId':path.split('/')[4],'isArchived':False,'preparation':{'status':'ready','readyCount':1,'totalCount':1,'documents':[]}}
    elif path.endswith('/ai-planner/automation'):body={'projectId':path.split('/')[4],'enabled':False,'canManage':False,'status':'disabled','defaults':{'enabled':False}}
    elif path.endswith('/readiness'):body={'status':'ready','ready':True}
    elif path.startswith('/api/project-financials/'):body={'status':'financial_data_unavailable','project':None}
    elif path.endswith('/psa'):body={'meetings':[],'raidHistory':[],'decisions':[],'reminderPreferences':{'enabled':False}}
    await route.fulfill(status=status,content_type='application/json',body=json.dumps(body))
   await page.route('**/*',route_handler);await page.goto('http://fixture.invalid/')
   await page.evaluate("localStorage.setItem('projectPulseAuthSession',JSON.stringify({sessionToken:'SYNTHETIC-NOT-A-CREDENTIAL'}))")
   await page.add_style_tag(content=(BUNDLE/'app.css').read_text());await page.add_script_tag(content=(BUNDLE/'app.js').read_text());await page.evaluate('window.mountSharing()')
   await page.locator('.flowhive-scope-toolbar select').select_option(A);await page.get_by_role('button',name='Project home',exact=True).click()
   team=page.get_by_role('region',name='Project team and customer contacts');await team.get_by_role('heading',name='Internal project team').wait_for()
   await team.get_by_text('Engineer Two',exact=True).wait_for()
   if mode=='readonly':
    assert await team.get_by_role('button',name='Create customer info',exact=True).is_disabled();assert await team.get_by_role('button',name='Plan meeting',exact=True).is_disabled();assert state['writes']==[]
   elif mode=='limit':
    assert await team.get_by_role('button',name='Create customer info',exact=True).is_disabled();assert await team.get_by_text('15/15 active',exact=True).count()==1
   else:
    if mode=='success':
     await team.get_by_role('button',name='Create customer info',exact=True).click();form=team.locator('form.flowhive-contact-form')
     await form.get_by_label('Name',exact=True).fill('Customer project lead');await form.get_by_label('Email',exact=True).fill('lead@example.invalid');await form.get_by_label('Phone number',exact=True).fill('555-0180');await form.get_by_label('Title',exact=True).fill('Project lead')
     await form.get_by_role('button',name='Save customer info',exact=True).click();await team.get_by_text('Customer project lead',exact=True).wait_for()
     await team.get_by_role('button',name='Plan meeting',exact=True).click();form=team.locator('form.flowhive-meeting-draft-form')
     await form.get_by_label('Meeting title',exact=True).fill('Project readiness review');await form.get_by_label('Starts',exact=True).fill('2026-10-01T10:00');await form.get_by_label('Ends',exact=True).fill('2026-10-01T11:00');await form.get_by_label('Customer-visible agenda',exact=True).fill('Review responsibilities and next steps.')
     await form.get_by_role('checkbox',name=re.compile('Project Manager')).check();await form.get_by_role('checkbox',name=re.compile('Customer IT owner')).check();await form.get_by_role('button',name='Save meeting draft',exact=True).click()
     await team.get_by_role('status').filter(has_text='Invitations have not been sent').wait_for();assert len(state['meetings'])==1
     assert set(state['meetings'][0]['attendeeReferences'])=={f'user:{PM}',f'contact:{CONTACT}'}
     await page.locator('.flowhive-command-center').screenshot(path=str(OUT/f'collaboration-{theme}-{width}.png'))
    await page.get_by_role('button',name='WBS plan',exact=True).click()
    picker=page.locator('.flowhive-people-picker');await picker.locator('summary').click()
    await picker.get_by_role('checkbox',name=re.compile('Engineer Two')).check();await picker.get_by_role('checkbox',name=re.compile('Customer IT owner')).check()
    assert await picker.locator('summary').inner_text() and '3 assigned' in await picker.locator('summary').inner_text()
    await picker.get_by_role('checkbox',name=re.compile('Engineer Two')).uncheck();assert '2 assigned' in await picker.locator('summary').inner_text()
    if mode in ('success','race','conflict'):
     # Autosave is the default; retaining the finish during debounce regresses the reported disappearing date.
     finish=page.get_by_label('End date for Review prerequisites',exact=True)
     await finish.fill('2026-10-02');assert await finish.input_value()=='2026-10-02'
    else:
     await page.get_by_role('button',name='Save now',exact=True).click()
    if mode=='race':
     await page.wait_for_function("document.querySelector('.flowhive-save-bar')?.textContent.includes('Saving changes')")
     await page.get_by_label('Task 1 name',exact=True).fill('Keep the newer edit')
    if mode=='conflict':
     await page.wait_for_function("document.querySelector('.flowhive-save-bar')?.textContent.includes('Not saved')")
     await page.get_by_label('Task 1 name',exact=True).fill('Preserve after conflict');await page.wait_for_timeout(2200)
     assert len([w for w in state['writes'] if w[1].endswith('/working-copy')])==1
     assert await page.get_by_label('Task 1 name',exact=True).input_value()=='Preserve after conflict'
    elif mode=='error':
     await page.get_by_role('region',name='Fields needing correction').wait_for();assert 'durationWorkingDays' in await page.get_by_role('region',name='Fields needing correction').inner_text();assert await page.get_by_role('button',name='Save now',exact=True).is_enabled();await page.wait_for_timeout(2200);assert len([w for w in state['writes'] if w[1].endswith('/working-copy')])==1
    else:
     await page.wait_for_function("document.querySelector('.flowhive-save-bar')?.textContent.includes('All changes saved')")
     writes=[body for method,path,body in state['writes'] if path.endswith('/working-copy')];assert len(writes)==(2 if mode=='race' else 1);assert writes[0]['plan']['tasks'][0]['durationWorkingDays']==5;assert writes[0]['plan']['tasks'][0]['estimatedFinishDate']=='2026-10-02'
     if mode=='race':assert writes[-1]['plan']['tasks'][0]['name']=='Keep the newer edit' and writes[-1]['expectedRowVersion']==VERSION
     saved=writes[-1]['plan'];assert saved['projectEndDate'] is None and saved['tasks'][0]['constraintDate'] is None
     assert len(saved['assignments'])==2;assert saved['assignments'][0]['plannedHours']==8;assert saved['assignments'][0]['allocationPercent']==50
     assert saved['assignments'][1]['resourceUserId'] is None and saved['assignments'][1]['projectContactId']==CONTACT and saved['assignments'][1]['plannedHours']==0
     await page.get_by_role('checkbox',name='Critical tasks only',exact=True).check();assert await page.locator('.flowhive-work-row').count()==1
     await page.get_by_role('button',name='Board',exact=True).click();await page.get_by_text('Project Manager, Customer IT owner',exact=True).wait_for()
   assert state['errors']==[],state['errors']
   assert all(not any(part in path for part in ['/events','/onlineMeetings','/customer-shares']) for method,path,body in state['writes']),state['writes']
   await page.get_by_role('button',name='Project home',exact=True).click()
   assert await page.evaluate('document.documentElement.scrollWidth <= window.innerWidth+1'),'page-level horizontal overflow'
   # Contact details cannot linger when changing project scope.
   await page.locator('.flowhive-scope-toolbar select').select_option(B);await page.wait_for_timeout(150)
   assert await page.get_by_role('region',name='Project team and customer contacts').get_by_text('customer@example.invalid',exact=True).count()==0
   await page.close()
  await browser.close()
 print('FLOWHIVE_COLLABORATION_BROWSER_CASES=7; LIVE_REQUESTS=0; INVITATIONS_SENT=0; RESULT=PASS')
asyncio.run(main())
