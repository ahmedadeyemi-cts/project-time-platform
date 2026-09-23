#!/usr/bin/env python3
"""Actual React checklist with full built CSS, synthetic responses and no live sends."""
from __future__ import annotations
import argparse, copy, functools, hashlib, importlib.util, itertools, json, threading, traceback
from http.server import SimpleHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from playwright.sync_api import sync_playwright, expect

PROJECT = '11111111-1111-4111-8111-111111111111'
ACTIONS = ['delivery','acceptance','sent','billed','reopen_delivery','reopen_billing']
spec = importlib.util.spec_from_file_location('guide_measure', Path(__file__).with_name('validate-user-guide-browser.py'))
measure = importlib.util.module_from_spec(spec)
spec.loader.exec_module(measure)
MEASURE = measure.MEASURE.replace('.guide-workbench', '.completion-checklist')

class Quiet(SimpleHTTPRequestHandler):
    def log_message(self, *_args): pass

def initial():
    return dict(contract='project-completion-evidence-v1',projectId=PROJECT,state=dict(revision=0),basisFingerprint='synthetic-basis',closed=False,
        capabilities=dict.fromkeys(ACTIONS,True),pendingTimeCount=0,openTransmissionCount=0,
        automatedFinalDelivered=False,deliveryComplete=False,customerAcceptanceComplete=False,fullyBilled=False,billingEvidenceStale=False,invoices=[])

def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--fixture',required=True,type=Path)
    parser.add_argument('--output',required=True,type=Path)
    args=parser.parse_args();args.output.mkdir(parents=True,exist_ok=True)
    server=ThreadingHTTPServer(('127.0.0.1',0),functools.partial(Quiet,directory=str(args.fixture)))
    threading.Thread(target=server.serve_forever,daemon=True).start()
    origin=f'http://127.0.0.1:{server.server_port}'
    report=dict(testSourceSha256=hashlib.sha256(Path(__file__).read_bytes()).hexdigest(), status='failed',scope='Offline actual React component and freshly built CSS; synthetic API responses, not live authenticated account acceptance.',cases=[],checks=[],pageErrors=[],unexpectedRequests=[])
    try:
      with sync_playwright() as pw:
        for browser_name in ('chromium','firefox'):
          browser=getattr(pw,browser_name).launch(headless=True)
          context=browser.new_context(reduced_motion='reduce')
          state=initial();posts=[];fault={'abort':False,'conflict':False}
          def gate(route):
            request=route.request
            if not request.url.startswith(origin+'/'):
              report['unexpectedRequests'].append(request.url);route.abort();return
            if '/api/' not in request.url:
              route.continue_();return
            if f'/api/work-lifecycle/projects/{PROJECT}/completion-checklist' not in request.url:
              report['unexpectedRequests'].append(request.url);route.abort();return
            if request.method=='GET':
              route.fulfill(json=state);return
            assert request.method=='POST'
            payload=request.post_data_json;posts.append(payload)
            if fault['abort']:
              fault['abort']=False;route.abort();return
            if fault['conflict']:
              fault['conflict']=False;route.fulfill(status=409,json=dict(message='Synthetic revision changed'));return
            action=request.url.rsplit('/',1)[-1]
            assert action in ACTIONS and payload['confirmed'] is True
            state['state']['revision']+=1
            receipt=dict(receiptId='synthetic-receipt',occurredOn=payload['occurredOn'],reference=payload['reference'],evidence=payload['evidence'],party=payload['party'],notes=payload['notes'],recordedBy='synthetic-actor',recordedAt='2026-09-23T00:00:00Z',scope=payload['scope'])
            if action=='delivery': state['deliveryComplete']=True;state['state']['delivery']=receipt
            elif action=='acceptance': state['state']['acceptance']=receipt;state['customerAcceptanceComplete']=payload['scope']=='accepted'
            elif action=='sent': state['state']['sent']=receipt;state['fullyBilled']=False
            elif action=='billed': state['state']['billed']=receipt;state['fullyBilled']=True
            route.fulfill(json=dict(status='completion_evidence_recorded',message='Synthetic confirmation recorded.'))
          context.route('**/*',gate)
          context.tracing.start(screenshots=True,snapshots=True,sources=True)
          page=context.new_page();page.on('pageerror',lambda error:report['pageErrors'].append(str(error)))
          def ready(): expect(page.locator('.completion-progress')).to_contain_text(f"Saved evidence revision {state['state']['revision']}.")
          def step_button(name): return page.locator('.completion-steps').get_by_role('button',name=name,exact=True)
          def open_step(name):
            expect(page.locator('.completion-form')).to_have_count(0)
            button=step_button(name)
            expect(button).to_have_count(1)
            button.click()
            expect(page.locator('.completion-form')).to_have_count(1)
          def refresh(): page.get_by_role('button',name='Refresh checklist',exact=True).click();ready()
          def fill():
            form=page.locator('.completion-form')
            form.locator('input[type="text"], input:not([type])').first.fill('SYNTHETIC-REFERENCE')
            form.get_by_label('Supporting email, document or ticket reference').fill('Synthetic recorded document reference')
            form.get_by_label('Audit reason').fill('Synthetic reviewed evidence confirmation')
            form.locator('.completion-confirm input').check()
            return form
          page.goto(origin);ready()
          open_step('Record manual handoff')
          form=fill();expect(form.get_by_label('Package type')).to_have_value('partial')
          form.get_by_role('button',name='Save confirmation',exact=True).click();ready()
          expect(page.get_by_text('Partial manual handoff recorded',exact=True)).to_be_visible()
          expect(page.get_by_text('Not confirmed',exact=True)).to_be_visible()
          open_step('Update sent to certinia')
          form=fill();form.get_by_label('Package type').select_option('final');fault['abort']=True
          form.get_by_role('button',name='Save confirmation',exact=True).click()
          expect(page.locator('.completion-form').get_by_role('button',name='Retry the same confirmation',exact=True)).to_be_visible()
          previous=copy.deepcopy(posts[-1])
          page.locator('.completion-form').get_by_role('button',name='Retry the same confirmation',exact=True).click();ready()
          assert posts[-1]==previous and posts[-1]['operationId']==previous['operationId']
          expect(page.get_by_text('Final manual handoff recorded',exact=True)).to_be_visible()
          expect(page.get_by_text('Not confirmed',exact=True)).to_be_visible()
          open_step('Confirm fully billed');fill().get_by_role('button',name='Save confirmation',exact=True).click();ready()
          expect(page.get_by_text('Confirmed',exact=True)).to_be_visible()
          state['billingEvidenceStale']=True;state['fullyBilled']=False;refresh()
          expect(page.get_by_text('Reconciliation required',exact=True)).to_be_visible()
          open_step('Mark delivery complete');fill();fault['conflict']=True
          page.locator('.completion-form').get_by_role('button',name='Save confirmation',exact=True).click()
          expect(page.locator('.completion-form')).to_have_count(0);expect(page.get_by_role('alert')).to_contain_text('Synthetic revision changed')
          state=initial();state['capabilities']={a:a in ('delivery','acceptance','reopen_delivery') for a in ACTIONS}
          page.evaluate("window.__completionIdentity('pm')");ready()
          expect(step_button('Record manual handoff')).to_have_count(0)
          open_step('Mark delivery complete')
          state['capabilities']=dict.fromkeys(ACTIONS,False)
          page.evaluate("window.__completionIdentity('preview',true)");ready()
          expect(page.locator('.completion-form')).to_have_count(0)
          expect(step_button('Mark delivery complete')).to_have_count(0)
          report['checks'].append(dict(browser=browser_name,partialNotFinal=True,sentNotBilled=True,exactRetry=True,conflictInvalidation=True,roleAndViewAsInvalidation=True,unexpectedSends=0))
          state=initial();page.evaluate("window.__completionIdentity('ptc')");ready()
          for theme,owner,width in itertools.product(('light','dark'),('root','body'),(1366,390)):
            page.set_viewport_size(dict(width=width,height=1000))
            page.evaluate("([theme,owner])=>{document.documentElement.removeAttribute('data-theme');document.body.removeAttribute('data-theme');(owner==='root'?document.documentElement:document.body).setAttribute('data-theme',theme)}",[theme,owner])
            open_step('Record manual handoff');fill()
            rows=page.evaluate(MEASURE);assert len(rows)>15
            minimum=min(measure.ratio(row) for row in rows)
            assert minimum>=4.5, [(r['text'],measure.ratio(r)) for r in rows if measure.ratio(r)<4.5]
            assert page.evaluate('document.documentElement.scrollWidth<=innerWidth+2'), 'Unexpected horizontal overflow'
            button=page.locator('.completion-form').get_by_role('button',name='Save confirmation',exact=True)
            button.focus()
            # Establish actual keyboard modality; focus() after clicking is not :focus-visible.
            page.keyboard.press('Tab')
            expect(page.locator('.completion-form').get_by_role('button',name='Cancel',exact=True)).to_be_focused()
            page.keyboard.press('Shift+Tab')
            expect(button).to_be_focused()
            assert button.evaluate("el=>el.matches(':focus-visible') && getComputedStyle(el).outlineStyle!=='none'"), 'Keyboard focus indicator missing'
            name=f'{browser_name}-{theme}-{owner}-{width}'
            page.screenshot(path=str(args.output/f'{name}.png'),full_page=True)
            report['cases'].append(dict(name=name,visibleTextChecks=len(rows),minimumContrast=minimum))
            page.locator('.completion-form').get_by_role('button',name='Cancel',exact=True).click();expect(page.locator('.completion-form')).to_have_count(0)
          page.emulate_media(media='print');expect(page.locator('.completion-checklist')).not_to_be_visible()
          context.tracing.stop(path=str(args.output/f'{browser_name}-trace.zip'))
          context.close();browser.close()
      assert not report['pageErrors'] and not report['unexpectedRequests']
      report['status']='passed'
      print('COMPLETION_BROWSER_CHECKS=PASS '+json.dumps(dict(cases=len(report['cases']),checks=report['checks'])))
    except Exception as error:
      report['error']=str(error)
      report['traceback']=traceback.format_exc()
      raise
    finally:
      (args.output/'report.json').write_text(json.dumps(report,indent=2))
      server.shutdown();server.server_close()

if __name__=='__main__': main()
