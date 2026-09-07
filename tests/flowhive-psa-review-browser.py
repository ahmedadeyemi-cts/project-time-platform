"""Actual React review component; all API fixtures below are explicitly synthetic.

No application login, model request, database write or cloud deployment is made.
The in-memory callback transport allows the same tests in offline environments.
"""
import asyncio
import copy
import json
import os
from pathlib import Path
from playwright.async_api import async_playwright

PROJECT='11111111-1111-4111-8111-111111111111'
OTHER='22222222-2222-4222-8222-222222222222'
RUN='33333333-3333-4333-8333-333333333333'
VERSION='44444444-4444-4444-8444-444444444444'
SAVED='55555555-5555-4555-8555-555555555555'
PHASES=['Plan','Design','Implement','Validate','Release']
BUNDLE=Path(os.getenv('FLOWHIVE_REVIEW_BUNDLE','/tmp/flowhive-review-test'))
COUNT=0

def check(value, description):
    global COUNT
    assert value, description
    COUNT+=1
    print('PASSED:',description)

def fixture(project=PROJECT):
    current={'projectId':project,'tasks':[{'wbsNumber':'1.1','phase':'Plan','isSummary':False,'name':'Existing scoped activity',
        'status':'in_progress','percentComplete':50,'remainingEffortHours':4}],
        'milestones':[{'milestoneId':'synthetic-gate','name':'Retained acceptance gate','predecessorWbs':'1.1'}]}
    candidate={'projectId':project,'tasks':[{'wbsNumber':f'{i}.1','phase':phase,'isSummary':False,'name':f'Proposed {phase} activity',
        'description':'A synthetic description to exercise the actual reviewer.','detailedSteps':['Check the isolated fixture.','Record the synthetic outcome.'],
        'acceptanceCriteria':['Synthetic acceptance is documented.'],'citationIds':[1],'remainingEffortHours':2} for i,phase in enumerate(PHASES,1)]}
    schedule={'valid':True,'projectFinishDate':'2026-10-02','plannedHours':10,
        'tasks':[{'wbsNumber':t['wbsNumber'],'startDate':'2026-09-07','endDate':'2026-10-02'} for t in candidate['tasks']]}
    return {'projectId':project,'runId':RUN,'contract':'flowhive-reviewed-regeneration-v1','expectedWorkingRowVersion':VERSION,'currentPlan':current,
        'candidatePlan':candidate,'candidateSchedule':schedule,'candidateValidation':{'valid':True},'stateChanged':False}

async def run_case(browser, mode='normal', width=1400, dark=False):
    page=await browser.new_page(viewport={'width':width,'height':1000},color_scheme='dark' if dark else 'light')
    page.set_default_timeout(5000)
    calls=[];errors=[]
    page.on('pageerror',lambda e:errors.append(str(e)))
    # There is no network success stub: the actual component receives explicit
    # synthetic callback results. Unexpected network requests are denied.
    await page.route('**/*',lambda route:route.abort())
    async def api(_source,path,body=None):
        calls.append((path,copy.deepcopy(body)))
        project=OTHER if f'/{OTHER}/' in path else PROJECT
        data=fixture(project)
        if path.endswith('/review'):
            if mode=='late' and project==PROJECT:await asyncio.sleep(.25)
            if mode=='wrong-read':data['projectId']=OTHER
            if mode=='wrong-inner-read':data['candidatePlan']['projectId']=OTHER
            return {'body':data}
        if path.endswith('/review-preview'):
            assert body['expectedWorkingRowVersion']==VERSION
            assert body['decisions']==[{'existingWbs':'1.1','candidateWbs':None}]
            if mode=='preview-conflict':return {'status':409,'message':'Synthetic concurrent working-copy change'}
            return {'body':{'projectId':project,'runId':RUN,'previewFingerprint':'a'*64,'expectedWorkingRowVersion':VERSION,
                'plan':{**data['candidatePlan'],'projectId':OTHER if mode=='wrong-preview' else project},'schedule':data['candidateSchedule'],'validation':{'valid':True},
                'reviewSummary':{'mappedTaskCount':0,'retainedTaskCount':1,'preservedMilestoneCount':1,'previousPlannedHours':4,'plannedHours':14}}}
        if path.endswith('/apply-reviewed'):
            await asyncio.sleep(.08)
            assert body['previewFingerprint']=='a'*64 and body['expectedWorkingRowVersion']==VERSION
            if mode=='apply-timeout':return {'message':'Synthetic unknown save outcome'}
            if mode=='apply-conflict':return {'status':409,'message':'Synthetic stale working-copy revision'}
            return {'body':{'projectId':OTHER if mode=='wrong-save' else project,'runId':RUN,'terminal':True,
                'status':'completed','phase':'working_draft_ready','plan':data['candidatePlan'],'workingDraft':{'persisted':True,'rowVersion':SAVED,'workingRevision':2}}}
        raise AssertionError('Unexpected API operation: '+path)
    await page.expose_binding('__syntheticReviewApi',api)
    await page.set_content('<!doctype html><html><head></head><body><div id="root"></div></body></html>')
    await page.add_style_tag(content=BUNDLE.joinpath('app.css').read_text())
    await page.add_script_tag(content=BUNDLE.joinpath('app.js').read_text())
    await page.evaluate('''([project,run])=>{
      window.applied=[];window.regenerations=0;
      const request=async(path,body)=>{const r=await window.__syntheticReviewApi(path,body);if(!r.body){const e=new Error(r.message);e.status=r.status;throw e;}return r.body;};
      window.props={projectId:project,runId:run,getJson:path=>request(path),postJson:request,canEdit:true,hasLocalEdits:false,
        onApplied:r=>window.applied.push(r),onRegenerate:()=>window.regenerations++};
      window.remount=changes=>{Object.assign(window.props,changes);window.mountFlowHiveReview({...window.props});};
      window.remount({});
    }''',[PROJECT,RUN])
    if mode=='late':
        await page.wait_for_timeout(30)
        await page.evaluate('(id)=>window.remount({projectId:id})',OTHER)
        await page.get_by_text('Existing scoped activity',exact=False).wait_for()
        await page.wait_for_timeout(300)
        await page.get_by_role('button',name='Retain all existing activities separately').click()
        await page.get_by_label('Regeneration review note').fill('Synthetic reviewed retention decision.')
        await page.get_by_role('button',name='Preview merged work breakdown',exact=True).click()
        await page.get_by_label('Confirm reviewed scope and schedule').wait_for()
        check([p for p,b in calls if b][-1].startswith(f'/api/project-flowhive/projects/{OTHER}/'), 'project switch discards the late old-project review')
        check(await page.evaluate('window.applied.length')==0,'late review never writes or applies work')
    elif mode in ['wrong-read','wrong-inner-read']:
        await page.get_by_role('alert').wait_for()
        check(await page.get_by_role('button',name='Preview merged work breakdown',exact=True).count()==0,'wrong-project review is rejected before choices or saving')
        check(not any(body for _,body in calls),'wrong-project read makes no write requests')
    else:
        await page.get_by_text('Existing scoped activity',exact=False).wait_for()
        check(len(calls)==1 and calls[0][1] is None,'loading an existing proposal performs only a read')
        check(await page.get_by_text('Proposed Implement activity',exact=True).count()==1,'actual reviewer renders phase-specific proposed work')
        check(await page.get_by_text('1 existing milestone(s)',exact=True).count()==1,'retained milestone count is explicit')
        preview=page.get_by_role('button',name='Preview merged work breakdown',exact=True)
        check(await preview.is_disabled(),'preview requires explicit dispositions and review note')
        await page.get_by_role('button',name='Retain all existing activities separately').click()
        await page.get_by_label('Regeneration review note').fill('Synthetic reviewed retention decision.')
        if mode=='local-edits':
            await page.evaluate('window.remount({hasLocalEdits:true})')
            check(await preview.is_disabled(),'unsaved local work blocks preview and save')
        elif mode=='no-edit':
            await page.evaluate('window.remount({canEdit:false})')
            check(await preview.is_disabled(),'read-only permissions block preview and save')
        else:
            await preview.click()
            if mode in ['preview-conflict','wrong-preview']:
                await page.get_by_role('alert').wait_for()
                check(await page.get_by_role('button',name='Apply reviewed work breakdown',exact=True).count()==0,'concurrent preview conflict cannot be applied')
                await page.get_by_role('button',name='Reload review without regenerating').click()
                await page.wait_for_timeout(40)
                check(sum(p.endswith('/review') for p,_ in calls)==2,'review conflict has a read-only reload path')
                check(await page.evaluate('window.regenerations')==0,'review reload does not restart AI')
            else:
                apply=page.get_by_role('button',name='Apply reviewed work breakdown',exact=True)
                await apply.wait_for()
                check(await apply.is_disabled(),'preview alone does not apply without explicit acknowledgement')
                if mode=='normal':
                    await page.get_by_label('Regeneration review note').fill('Changed synthetic reviewed retention decision.')
                    check(await apply.count()==0,'editing the review note invalidates a previous preview')
                    await preview.click();await apply.wait_for()
                await page.get_by_label('Confirm reviewed scope and schedule').check()
                await apply.dblclick()
                if mode in ['apply-timeout','apply-conflict','wrong-save']:
                    await page.get_by_role('alert').wait_for()
                    check(await page.evaluate('window.applied.length')==0,'failed or mismatched save cannot report adoption')
                    if mode!='apply-conflict':
                        check(await apply.is_disabled(),'ambiguous apply outcome blocks an automatic or repeated write')
                    else:
                        check(await apply.count()==0,'stale revision invalidates its apply preview')
                else:
                    await page.wait_for_function('window.applied.length === 1')
                    check(await page.evaluate('window.applied[0].workingDraft.rowVersion')==SAVED,'successful apply passes the exact saved receipt to parent readback')
                check(sum(p.endswith('/apply-reviewed') for p,_ in calls)==1,'double-click attempts only one reviewed save')
                check(await page.evaluate('window.regenerations')==0,'review/preview/apply never invokes model generation')
        check(await page.evaluate('document.documentElement.scrollWidth<=innerWidth+1'),'review table scrolling is contained at '+str(width)+'px')
    check(not errors,'actual React reviewer has no uncaught browser errors')
    await page.close()

async def main():
    async with async_playwright() as p:
        options={'headless':True}
        if os.getenv('CHROME_PATH'):options['executable_path']=os.environ['CHROME_PATH']
        browser=await p.chromium.launch(**options)
        try:
            for mode in ['normal','apply-timeout','apply-conflict','wrong-save','wrong-read','wrong-inner-read','wrong-preview','preview-conflict','late','local-edits','no-edit']:
                await run_case(browser,mode)
            await run_case(browser,'normal',390,False)
            await run_case(browser,'normal',390,True)
        finally:await browser.close()
    print('FLOWHIVE_REVIEW_BROWSER_ASSERTIONS_PASSED='+str(COUNT))

if __name__=='__main__':asyncio.run(main())
