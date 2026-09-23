#!/usr/bin/env python3
"""Exercise the real generated guide with full built CSS and offline identities."""
from __future__ import annotations
import argparse
import functools
import itertools
import json
import threading
from http.server import SimpleHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from playwright.sync_api import sync_playwright, expect

MEASURE = r'''() => {
 const rgba=value=>{const a=value.match(/[\d.]+/g)?.map(Number);if(!value.startsWith('rgb')||!a)throw new Error('Unsupported color '+value);return a.length===3?[...a,1]:a;};
 const root=document.querySelector('.guide-workbench');
 const walker=document.createTreeWalker(root,NodeFilter.SHOW_TEXT);const output=[];
 while(walker.nextNode()){
  const text=walker.currentNode;if(!text.textContent.trim())continue;
  const el=text.parentElement;if(el.closest('select,option,script,style'))continue;
  const range=document.createRange();range.selectNodeContents(text);const rect=range.getBoundingClientRect();
  if(!(rect.width>0&&rect.height>0))continue;
  let hidden=false,opacity=1,bg=null;
  for(let n=el;n;n=n.parentElement){const s=getComputedStyle(n);hidden ||= s.visibility==='hidden'||s.display==='none';opacity*=Number(s.opacity);if(!bg){const c=rgba(s.backgroundColor);if(c[3]===1)bg=c;else if(c[3]!==0)throw new Error('Unmodeled translucent surface');}}
  if(hidden)continue;
  const s=getComputedStyle(el);output.push({text:text.textContent.trim().slice(0,100),foreground:rgba(s.webkitTextFillColor||s.color),background:bg,opacity});
 }
 return output;
}'''

def ratio(item):
    if item['background'] is None:
        raise AssertionError('Text lacks an opaque owned background: '+item['text'])
    bg=item['background'][:3];fg=item['foreground'];alpha=fg[3]*item['opacity']
    rgb=[fg[i]*alpha+bg[i]*(1-alpha) for i in range(3)]
    def lum(v):
        values=[c/255 for c in v]
        linear=[c/12.92 if c<=.04045 else ((c+.055)/1.055)**2.4 for c in values]
        return sum(c*w for c,w in zip(linear,(.2126,.7152,.0722)))
    low,high=sorted((lum(rgb),lum(bg)))
    return (high+.05)/(low+.05)

class QuietHandler(SimpleHTTPRequestHandler):
    def log_message(self,*_args):
        pass

def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--fixture',type=Path,required=True)
    parser.add_argument('--output',type=Path,required=True)
    args=parser.parse_args();args.output.mkdir(parents=True,exist_ok=True)
    server=ThreadingHTTPServer(('127.0.0.1',0),functools.partial(QuietHandler,directory=str(args.fixture)))
    threading.Thread(target=server.serve_forever,daemon=True).start()
    origin=f'http://127.0.0.1:{server.server_port}'
    report={'scope':'Offline generated guide, full production CSS, synthetic identities; no live account or integration acceptance.', 'roleChecks':[], 'cases':[], 'pageErrors':[], 'unexpectedRequests':[], 'status':'failed'}
    try:
      with sync_playwright() as playwright:
        for browser_name in ('chromium','firefox'):
          browser=getattr(playwright,browser_name).launch(headless=True)
          context=browser.new_context(reduced_motion='reduce')
          def gate(route):
            if route.request.url.startswith(origin+'/') and '/api/' not in route.request.url:route.continue_()
            else:report['unexpectedRequests'].append(route.request.url.split('?')[0]);route.abort()
          context.route('**/*',gate)
          page=context.new_page();page.on('pageerror',lambda error:report['pageErrors'].append(str(error)))
          page.set_default_timeout(15000);page.goto(origin+'/#user-guide')
          expect(page.locator('.guide-workbench')).to_have_attribute('data-guide-ready','true')
          expect(page.locator('.guide-module')).to_have_count(76)
          expect(page.get_by_label('Your assigned role')).to_have_value('ENGINEERING')
          # Every handbook is reached through the actual verified-context hook.
          for role in page.evaluate('window.__guideTestRoleCodes'):
            page.evaluate('payload=>window.__guideTestAuthority(payload)',{'roles':[role]})
            expect(page.get_by_label('Your assigned role')).to_have_value(role)
            expect(page.locator('[data-guide-role]')).to_have_attribute('data-guide-role',role)
            expect(page.locator('.guide-role-steps details')).not_to_have_count(0)
            report['roleChecks'].append({'browser':browser_name,'role':role,'status':'passed'})
          page.evaluate('window.__guideTestAuthority({roles:["ENGINEERING"]})')
          expect(page.get_by_label('Your assigned role')).to_have_value('ENGINEERING')
          page.get_by_role('button',name='Browse all role reference',exact=True).click()
          page.get_by_label('Reference role').select_option('SUPER_ADMINISTRATOR')
          assert page.evaluate('window.__projectPulseEffectiveNavigation.roleCodes')==['ENGINEERING']
          assert all(href in ('#timesheet','#user-guide') for href in page.locator('.guide-open-workspace').evaluate_all('(els)=>els.map(el=>el.getAttribute("href"))'))
          page.get_by_role('button',name='Show my responsibilities',exact=True).click()
          page.evaluate('window.__guideTestAuthority({roles:["CUSTOM_GUIDE_ROLE"]})')
          expect(page.locator('[data-guide-role]')).to_have_attribute('data-guide-role','CUSTOM_GUIDE_ROLE')
          expect(page.locator('.guide-role-detail')).to_contain_text('not yet been published')
          page.evaluate('window.__guideTestAuthority({roles:[]})')
          expect(page.locator('[data-guide-role]')).to_have_count(0)
          page.evaluate('window.__guideTestAuthority({roles:["ENGINEERING"],state:"loading"})')
          expect(page.locator('.guide-workbench')).to_have_attribute('data-guide-ready','false')
          expect(page.locator('.guide-open-workspace')).to_have_count(0)
          page.evaluate('window.__guideTestAuthority({roles:["ENGINEERING"],refreshFailed:true})')
          expect(page.locator('.guide-open-workspace')).to_have_count(0)
          page.evaluate('window.__guideTestAuthority({roles:["ENGINEERING"]})')
          expect(page.locator('.guide-workbench')).to_have_attribute('data-guide-ready','true')
          page.evaluate('window.__guideTestAuthority({roles:["ENGINEERING"],identity:"changed-identity",preserveNavigation:true})')
          expect(page.locator('.guide-workbench')).to_have_attribute('data-guide-ready','false')
          page.evaluate('window.__guideTestAuthority({roles:["PROJECT_MANAGEMENT"],identity:"changed-identity",viewAs:true})')
          expect(page.locator('.guide-workbench')).to_have_attribute('data-guide-ready','true')
          expect(page.locator('.guide-edition')).to_contain_text('View-As is active')
          page.evaluate('window.__guideTestAuthority({roles:["ENGINEERING","PROJECT_MANAGEMENT"]})')
          expect(page.get_by_label('Your assigned role').locator('option')).to_have_count(2)
          page.evaluate('window.__guideTestAuthority({roles:["ENGINEERING"]})')
          search=page.get_by_label('Search the guide',exact=True)
          search.fill('PTO transfer')
          expect(page.locator('[data-guide-route="sow-generator"]')).to_have_count(1)
          search.fill('not-a-real-guide-query-123')
          expect(page.locator('.guide-module')).to_have_count(0)
          page.get_by_role('button',name='Reset filters',exact=True).click()
          page.get_by_label('Only my available workspaces',exact=True).check()
          expect(page.locator('.guide-module')).to_have_count(2)
          page.get_by_role('button',name='Reset filters',exact=True).click()
          page.get_by_role('button',name='Expand results',exact=True).click()
          expect(page.locator('.guide-module[open]')).to_have_count(76)
          page.get_by_role('button',name='Collapse results',exact=True).click()
          expect(page.locator('.guide-module[open]')).to_have_count(0)
          time=page.locator('[data-guide-route="timesheet"]')
          time.locator(':scope > summary').click()
          expect(time.locator('.guide-foundation')).to_contain_text('Up to five distinct activity timers')
          expect(time.locator('.guide-foundation')).to_contain_text('24 hours')
          assert 'Only one timer' not in time.locator('.guide-foundation').inner_text()
          # Keyboard disclosure and filtered printing restore the prior state.
          summary=time.locator(':scope > summary');summary.focus();page.keyboard.press('Enter')
          expect(time).not_to_have_attribute('open','')
          search.fill('sow-generator')
          page.get_by_role('button',name='Collapse results',exact=True).click()
          current=page.locator('.guide-module').count()
          page.evaluate('window.dispatchEvent(new Event("beforeprint"))')
          expect(page.locator('.guide-module[open]')).to_have_count(current)
          page.evaluate('window.dispatchEvent(new Event("afterprint"))')
          expect(page.locator('.guide-module[open]')).to_have_count(0)
          page.get_by_role('button',name='Reset filters',exact=True).click()
          page.get_by_role('button',name='Collapse results',exact=True).click()
          expect(page.locator('.guide-module[open]')).to_have_count(0)
          for theme,owner,width in itertools.product(('light','dark'),('root','body'),(1440,390)):
            page.set_viewport_size({'width':width,'height':1000})
            page.evaluate('''({theme,owner})=>{document.documentElement.removeAttribute('data-theme');document.body.removeAttribute('data-theme');(owner==='root'?document.documentElement:document.body).setAttribute('data-theme',theme);}''',{'theme':theme,'owner':owner})
            page.evaluate('''async()=>{await new Promise(requestAnimationFrame);await new Promise(requestAnimationFrame);}''')
            measured=page.evaluate(MEASURE)
            assert len(measured)>20,'Empty or unexpectedly small guide contrast audit'
            for item in measured:
              item['ratio']=ratio(item)
              assert item['ratio']>=4.5,f'{browser_name}/{theme}/{owner}: {item}'
            row={'browser':browser_name,'theme':theme,'owner':owner,'width':width,'textChecks':len(measured),'minimumContrast':min(item['ratio'] for item in measured),'status':'passed'}
            report['cases'].append(row)
            if owner=='root' and width==1440:page.screenshot(path=str(args.output/f'guide-{browser_name}-{theme}.png'),full_page=False)
          assert not report['pageErrors'],report['pageErrors']
          context.close();browser.close()
      assert not report['unexpectedRequests'],report['unexpectedRequests']
      report['status']='passed'
    finally:
      server.shutdown();server.server_close()
      (args.output/'user-guide-browser.json').write_text(json.dumps(report,indent=2)+'\n')
    print(json.dumps({key:report[key] for key in ('status','scope')},indent=2))
    print('USER_GUIDE_BROWSER_ROLE_CHECKS='+str(len(report['roleChecks'])))
    print('USER_GUIDE_BROWSER_PRESENTATIONS='+str(len(report['cases'])))
    return 0

if __name__=='__main__':
    raise SystemExit(main())
