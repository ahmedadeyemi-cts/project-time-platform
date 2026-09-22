#!/usr/bin/env python3
"""Measure source-derived module surfaces using complete built application CSS.

Isolated rendering only: business JavaScript and outbound requests never run.
Dynamic expressions in JSX are represented by explicit fixture substitutions.
"""
from __future__ import annotations
import argparse, hashlib, io, itertools, json, re, time
from pathlib import Path
from PIL import Image
from playwright.sync_api import sync_playwright

MEASURE = r'''() => {
 const out=[], hidden=[];
 const root=document.getElementById('audit-root');
 const tree=document.createTreeWalker(root,NodeFilter.SHOW_TEXT);
 let text;
 function describe(e) {return e.tagName.toLowerCase()+[...e.classList].map(c=>'.'+c).join('');}
 while(text=tree.nextNode()) {
  if(!text.textContent.trim())continue;
  const e=text.parentElement; if(['STYLE','SCRIPT','OPTION'].includes(e.tagName))continue;
  const s=getComputedStyle(e); let opacity=1,notVisible=false;const parents=[];
  for(let p=e;p && p!==root;p=p.parentElement) {
    const ps=getComputedStyle(p);opacity*=Number(ps.opacity);notVisible||=ps.display==='none'||ps.visibility==='hidden'||ps.visibility==='collapse';
    parents.push(describe(p));
  }
  const range=document.createRange();range.selectNodeContents(text);
  const rects=[...range.getClientRects()].filter(r=>r.width>0 && r.height>0);
  const row={text:text.textContent.trim().slice(0,160),selector:parents.slice(0,6).reverse().join(' > '),element:describe(e),parents,
   color:s.webkitTextFillColor||s.color,opacity,fontSize:s.fontSize,fontWeight:s.fontWeight};
  if(notVisible||!rects.length) {hidden.push({...row,reason:'not_rendered'});continue;}
  const rect=rects.find(r=>r.left>=0&&r.right<=innerWidth&&r.top>=0&&r.bottom<=innerHeight);
  if(!rect){hidden.push({...row,reason:'outside_fixture_viewport'});continue;}
  const box=e.getBoundingClientRect();
  const left=Math.max(rect.left,box.left),right=Math.min(rect.right,box.right),top=Math.max(rect.top,box.top),bottom=Math.min(rect.bottom,box.bottom);
  if(right<=left||bottom<=top){hidden.push({...row,reason:'text_outside_own_box'});continue;}
  row.sampleClippedToElement=left!==rect.left||right!==rect.right||top!==rect.top||bottom!==rect.bottom;
  row.rect={x:left,y:top,width:right-left,height:bottom-top};
  const canvas=document.createElement('canvas');canvas.width=canvas.height=1;
  const c=canvas.getContext('2d',{willReadFrequently:true});c.fillStyle=row.color;c.fillRect(0,0,1,1);
  row.rgba=[...c.getImageData(0,0,1,1).data];row.rgba[3]/=255;
  out.push(row);
 }
 return {text:out,hidden};
}'''

# Inline important paint overrides are deliberate: stylesheet-only masking loses
# against the production theme's high-specificity important text declarations.
# Preserve color/currentColor backgrounds and restore every original style byte.
MASK_TEXT = r"""() => {
 window.__auditSavedStyles=[...document.querySelectorAll('#audit-root *')]
   .map(el=>[el,el.getAttribute('style')]);
 for(const [el] of window.__auditSavedStyles){
   el.style.setProperty('-webkit-text-fill-color','transparent','important');
   el.style.setProperty('text-shadow','none','important');
 }
}"""
RESTORE_TEXT = r"""() => {
 for(const [el,value] of window.__auditSavedStyles || []){
   if(value===null)el.removeAttribute('style');else el.setAttribute('style',value);
 }
 delete window.__auditSavedStyles;
}"""

def built_css(root: Path) -> str:
    dist = (root / 'dist').resolve()
    index = (dist / 'index.html').read_text(encoding='utf-8')
    links = re.findall(r'<link\b[^>]*rel=["\']stylesheet["\'][^>]*>', index)
    if not links:
        raise RuntimeError('No built stylesheets; run the complete frontend build first.')
    sources = []
    for link in links:
        match = re.search(r'href=["\']([^"\']+)["\']', link)
        if not match or not match[1].startswith('/assets/') or '..' in match[1]:
            raise RuntimeError('Expected only local build stylesheets.')
        path = (dist / match[1].lstrip('/')).resolve()
        if not path.is_relative_to(dist) or not path.is_file():
            raise RuntimeError('Missing stylesheet or build-directory escape.')
        sources.append(path.read_text(encoding='utf-8'))
    return '\n'.join(sources)

def require(condition: bool, message: str) -> None:
    if not condition:
        raise RuntimeError(message)


def ratio(fg,bg,opacity):
 alpha=fg[3]*opacity
 painted=[fg[i]*alpha+bg[i]*(1-alpha) for i in range(3)]
 def lum(rgb):
  values=[v/255 for v in rgb[:3]]
  linear=[v/12.92 if v<=0.04045 else ((v+0.055)/1.055)**2.4 for v in values]
  return sum(v*w for v,w in zip(linear,(.2126,.7152,.0722)))
 lo,hi=sorted((lum(painted),lum(bg)))
 return (hi+.05)/(lo+.05)

def self_test(page) -> None:
    require(abs(ratio([0,0,0,1], (255,255,255), 1)-21) < .0001,
            'Black/white contrast reference failed.')
    require(ratio([255,255,255,1], (255,255,255), 1) == 1,
            'White-on-white negative fixture was accepted.')
    require(ratio([0,0,0,1], (255,255,255), .1) < 4.5,
            'Ancestor opacity must reduce effective contrast.')
    require(ratio([0,0,0,.1], (255,255,255), 1) < 4.5,
            'Foreground alpha must reduce effective contrast.')
    page.set_content("""<!doctype html><style>
      body{margin:0;background:white}
      #audit-root #paint{background:white;color:rgb(255,0,0);padding:20px}
      :root body #audit-root #paint span{-webkit-text-fill-color:white!important}
      </style><div id="audit-root"><div id="paint"><span
      style="font-size:24px;-webkit-text-fill-color:rgb(255,0,0)!important">Paint</span>
      <small style="display:none">Hidden</small></div></div>""")
    original = page.locator('#paint span').get_attribute('style')
    measured = page.evaluate(MEASURE)
    require(len(measured['text']) == 1 and len(measured['hidden']) == 1,
            'Hidden text must be reported separately, not counted as a pass.')
    require(measured['text'][0]['rgba'][:3] == [255,0,0],
            'Measurement must honor the effective text-fill color.')
    page.evaluate(MASK_TEXT)
    require(page.locator('#paint span').evaluate(
        "el=>getComputedStyle(el).webkitTextFillColor") == 'rgba(0, 0, 0, 0)',
        'Masking failed against inline-important source styles.')
    image = Image.open(io.BytesIO(page.screenshot())).convert('RGB')
    rect = measured['text'][0]['rect']
    require(image.getpixel((int(rect['x']+rect['width']/2),
                            int(rect['y']+rect['height']/2))) == (255,255,255),
            'Screenshot background still contains text paint.')
    page.evaluate(RESTORE_TEXT)
    require(page.locator('#paint span').get_attribute('style') == original,
            'Fixture style attributes were not restored exactly.')
    print('MODULE_SURFACE_MEASUREMENT_SELF_TESTS=PASS', flush=True)


def main():
 p=argparse.ArgumentParser(description=__doc__)
 p.add_argument('--fixtures',type=Path,required=True)
 p.add_argument('--web-root',type=Path,default=Path(__file__).resolve().parents[1])
 p.add_argument('--css',type=Path,help='Local diagnostic CSS only; CI uses dist/index.html')
 p.add_argument('--extra-css',type=Path);p.add_argument('--output',type=Path,required=True)
 p.add_argument('--browsers',default='chromium,firefox');p.add_argument('--themes',default='light,dark')
 p.add_argument('--owners',default='root,body');p.add_argument('--layouts',default='table,enterprise,classic')
 p.add_argument('--chromium-executable');p.add_argument('--filter',default='');p.add_argument('--limit',type=int,default=0);p.add_argument('--width',type=int,default=1440)
 args=p.parse_args();data=json.loads(args.fixtures.read_text());fixtures=data['fixtures']
 if args.filter:fixtures=[f for f in fixtures if re.search(args.filter,f['file']+' '+f['classes'])]
 if args.limit:fixtures=fixtures[:args.limit]
 require(bool(fixtures), 'No fixtures selected; an empty audit is not a pass.')
 require(bool(data.get('inventory')) and bool(data.get('domAdoption')), 'Missing source inventory or DOM adapter.')
 css=args.css.read_text() if args.css else built_css(args.web_root)
 if args.extra_css:css+='\n'+args.extra_css.read_text()
 args.output.mkdir(parents=True,exist_ok=True)
 report={'scope':data['scope'],'jsxFilesScanned':len(data['inventory']),'adapterSha256':data.get('adapterSha256'),'fixtureCount':len(fixtures),'filesWithFixtures':len({f['file'] for f in fixtures}),'fixturesSha256':hashlib.sha256(args.fixtures.read_bytes()).hexdigest(),'cssSha256':hashlib.sha256(css.encode()).hexdigest(),'threshold':4.5,'disclosures':'expanded for inspection','module025Context':'actual workspace palette wrapper','failures':[],'checks':[],'unmeasured':[]}
 started=time.monotonic()
 with sync_playwright() as pw:
  for browser_name in args.browsers.split(','):
   browser=getattr(pw,browser_name).launch(headless=True, **({'executable_path':args.chromium_executable} if browser_name=='chromium' and args.chromium_executable else {}))
   context=browser.new_context(reduced_motion='reduce',viewport={'width':args.width,'height':1600})
   context.route('**/*',lambda route:route.abort())
   page=context.new_page()
   self_test(page)
   for layout,theme,owner in itertools.product(args.layouts.split(','),args.themes.split(','),args.owners.split(',')):
    case=f'{browser_name}-{layout}-{theme}-{owner}'
    page.set_content('<!doctype html><html><head><meta charset="utf-8"><style>'+css+'</style><style>*{animation:none!important;transition:none!important;caret-color:transparent!important}</style></head><body><div id="audit-root"></div></body></html>')
    page.evaluate('''({layout,theme,owner})=>{
      const r=document.documentElement,b=document.body;
      r.dataset.pulseExperience=b.dataset.pulseExperience=layout==='classic'?'classic':'enterprise';
      r.dataset.pulseLayout=b.dataset.pulseLayout=layout;
      (owner==='root'?r:b).dataset.theme=theme;
    }''',{'layout':layout,'theme':theme,'owner':owner})
    for idx,f in enumerate(fixtures):
     html=f['html']
     if f['file'].startswith('src/module025/') and not re.search(r'class="[^"]*\bm025-workspace\b',html):
      html='<div class="m025-workspace">'+html+'</div>'
     if not re.search(r'<main[^>]*class="[^"]*\bapp-shell\b',html):html='<main class="app-shell enterprise-nav-enabled">'+html+'</main>'
     page.evaluate('''html=>{document.getElementById('audit-root').innerHTML=html;document.querySelectorAll('#audit-root details').forEach(el=>{el.open=true;});scrollTo(0,0);}''',html)
     page.evaluate(data['domAdoption'])
     result=page.evaluate(MEASURE)
     for hidden in result['hidden']:
      report['unmeasured'].append({'case':case,'file':f['file'],'line':f['line'],**hidden})
     if not result['text']:continue
     page.evaluate(MASK_TEXT)
     capture_height=min(1600,max(1,int(max(t['rect']['y']+t['rect']['height'] for t in result['text'])+3)))
     png=page.screenshot(animations='disabled',clip={'x':0,'y':0,'width':args.width,'height':capture_height})
     image=Image.open(io.BytesIO(png)).convert('RGB')
     fixture_fails=0
     for text in result['text']:
      rect=text['rect'];ratios=[];backgrounds=[]
      for fx,fy in ((.2,.3),(.5,.3),(.8,.3),(.2,.7),(.5,.7),(.8,.7)):
       xy=(min(image.width-1,max(0,int(rect['x']+rect['width']*fx))),min(image.height-1,max(0,int(rect['y']+rect['height']*fy))))
       bg=image.getpixel(xy);ratios.append(ratio(text['rgba'],bg,text['opacity']));backgrounds.append(bg)
      row={'case':case,'file':f['file'],'line':f['line'],'branch':f['branch'],'surface':f['classes'],**text,'ratio':min(ratios),'backgrounds':backgrounds}
      report['checks'].append(row)
      if row['ratio']<4.5:
       report['failures'].append(row);fixture_fails+=1
     if ('OnCallSchedulingCenter' in f['file'] or 'OemVendorDirectoryCenter' in f['file']) and (f['tag']=='header') and f['branch']==0:
      page.evaluate(RESTORE_TEXT)
      page.screenshot(path=str(args.output/f'{case}-{Path(f["file"]).stem}-{f["line"]}.png'),full_page=True)
     if idx%100==0:print(case,idx,'/',len(fixtures),'failures',len(report['failures']),flush=True)
   context.close();browser.close()
 require(bool(report['checks']), 'No visible text was measured; an empty audit is not a pass.')
 report.update({'status':'failed' if report['failures'] else 'passed','visibleTextChecks':len(report['checks']),'elapsedSeconds':round(time.monotonic()-started,1)})
 (args.output/'module-surface-contrast.json').write_text(json.dumps(report,indent=2)+'\n')
 print(json.dumps({k:report[k] for k in ('status','jsxFilesScanned','fixtureCount','visibleTextChecks','elapsedSeconds')},indent=2))
 print('failures',len(report['failures']),'unmeasured',len(report['unmeasured']))
 return bool(report['failures'])
if __name__=='__main__':raise SystemExit(main())
