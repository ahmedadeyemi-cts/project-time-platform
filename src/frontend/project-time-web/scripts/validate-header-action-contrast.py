#!/usr/bin/env python3
"""Isolated header utility rendering with complete built CSS, never live APIs.

Exercises the real shell, adopted inverse header, both controls and their nested
labels. No credentials, product scripts or external resources are used.
"""
from __future__ import annotations
import argparse
import itertools
import json
import re
from pathlib import Path
from playwright.sync_api import sync_playwright

CONTROLS = {'approvals': '.approval-mailbox-button', 'search': '.projectpulse-global-search-button'}
STATES = ('default', 'hover', 'focus', 'pressed', 'expanded', 'disabled', 'aria-disabled')
MARKUP = '''<main class="app-shell enterprise-nav-enabled">
<header class="top-bar enterprise-top-bar">
<div class="brand-lockup"><strong>Pulse</strong><small>Unified Business Operations</small></div>
<nav class="enterprise-top-navigation" aria-label="Primary navigation"><a href="#my-role-in-pulse">My Role in Pulse</a><a href="#dashboard">Dashboard</a><a href="#modules">Modules</a><button class="enterprise-more-button">More</button></nav>
<div class="enterprise-header-utilities">
<div class="approval-mailbox-shell"><button type="button" class="approval-mailbox-button" aria-label="9 approval items require attention" aria-expanded="false"><span class="approval-mailbox-icon" aria-hidden="true" data-header-text="approvals.icon">✉</span><span class="approval-mailbox-label" data-header-text="approvals.label">Approvals</span><span class="approval-mailbox-badge" data-header-text="approvals.count">9</span></button></div>
<div class="projectpulse-global-search"><button type="button" class="projectpulse-global-search-button" aria-label="Search"><span aria-hidden="true" data-header-text="search.icon">⌕</span><strong data-header-text="search.label">Search</strong><kbd data-header-text="search.shortcut">Ctrl K</kbd></button></div>
<button type="button" class="profile-avatar-button" aria-label="Profile">AA</button></div></header></main>'''
MEASURE = r'''selector => {
 const button = document.querySelector(selector);
 const rgba = value => {
   if (!/^rgba?\(/.test(value)) throw new Error(`Unsupported color: ${value}`);
   const a = value.match(/[\d.]+/g).map(Number); return a.length === 3 ? [...a, 1] : a;
 };
 const visible = el => {
   const r = el.getBoundingClientRect(); if (!(r.width > 0 && r.height > 0)) return false;
   for (let n=el; n; n=n.parentElement) {
     const s=getComputedStyle(n); if (s.display==='none' || s.visibility==='hidden') return false;
   } return true;
 };
 const css = getComputedStyle(button);
 const targets = [...button.querySelectorAll('[data-header-text]')].map(el => {
   const backgrounds=[]; let opacity=1;
   for (let n=el; n; n=n.parentElement) opacity*=Number(getComputedStyle(n).opacity);
   for (let n=el; n; n=n.parentElement) {
     const s=getComputedStyle(n); backgrounds.push(rgba(s.backgroundColor));
     if (backgrounds.at(-1)[3]===1 || s.backgroundImage!=='none') break;
   }
   return {target:el.dataset.headerText,visible:visible(el),opacity,backgrounds,
     foreground:rgba(getComputedStyle(el).webkitTextFillColor || getComputedStyle(el).color)};
 });
 return {visible:visible(button),background:rgba(css.backgroundColor),disabled:button.disabled,
   ariaDisabled:button.getAttribute('aria-disabled'),focusVisible:button.matches(':focus-visible'),
   outlineStyle:css.outlineStyle,outlineWidth:parseFloat(css.outlineWidth),targets};
}'''
SETTLE = '''async () => {
 await new Promise(requestAnimationFrame);
 for (const a of document.getAnimations()) {
   if (Number.isFinite(a.effect.getComputedTiming().endTime)) { try { a.finish(); } catch {} }
 }
 await new Promise(requestAnimationFrame);
}'''


def contrast(target: dict) -> float | None:
    layers = target['backgrounds']
    if not layers or layers[-1][3] != 1:
        return None
    bg = layers[-1][:3]
    for layer in reversed(layers[:-1]):
        bg = [layer[i]*layer[3] + bg[i]*(1-layer[3]) for i in range(3)]
    fg = target['foreground']; alpha = fg[3]*target['opacity']
    painted = [fg[i]*alpha + bg[i]*(1-alpha) for i in range(3)]
    def luminance(rgb):
        values = [v/255 for v in rgb]
        linear = [v/12.92 if v<=0.04045 else ((v+0.055)/1.055)**2.4 for v in values]
        return sum(v*w for v,w in zip(linear, (0.2126,0.7152,0.0722)))
    low,high = sorted((luminance(painted),luminance(bg)))
    return (high+0.05)/(low+0.05)


def built_css(root: Path) -> str:
    dist = (root/'dist').resolve()
    links = re.findall(r'<link\b[^>]*rel=["\']stylesheet["\'][^>]*>', (dist/'index.html').read_text())
    if not links:
        raise RuntimeError('Run the complete frontend build before this test.')
    sources = []
    for link in links:
        match = re.search(r'href=["\']([^"\']+)["\']', link)
        if not match or not match[1].startswith('/assets/') or '..' in match[1]:
            raise RuntimeError('Expected only local built stylesheets.')
        path = (dist/match[1].lstrip('/')).resolve()
        if not path.is_relative_to(dist):
            raise RuntimeError('Stylesheet escapes build directory.')
        sources.append(path.read_text())
    return '\n'.join(sources)


def reset(page) -> None:
    page.mouse.up(); page.mouse.move(0,0)
    page.evaluate('''() => {
      document.activeElement?.blur();
      document.querySelectorAll('.approval-mailbox-button,.projectpulse-global-search-button').forEach(el=>{
        el.disabled=false;el.removeAttribute('aria-disabled');el.setAttribute('aria-expanded','false');el.classList.remove('active');
      });
    }''')


def set_state(page, selector: str, state: str) -> None:
    reset(page); button=page.locator(selector)
    if state in ('hover','pressed'):
        box=button.bounding_box()
        if box is None: raise RuntimeError('Header action is not rendered.')
        page.mouse.move(box['x']+box['width']/2,box['y']+box['height']/2)
        if state=='pressed': page.mouse.down()
    elif state=='focus': page.keyboard.press('Tab'); button.focus()
    elif state=='expanded': button.evaluate("el=>{el.classList.add('active');el.setAttribute('aria-expanded','true');}")
    elif state=='disabled': button.evaluate('el=>{el.disabled=true;}')
    elif state=='aria-disabled': button.evaluate("el=>el.setAttribute('aria-disabled','true')")
    page.evaluate(SETTLE)


def main() -> int:
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--web-root',type=Path,default=Path(__file__).resolve().parents[1])
    parser.add_argument('--output',type=Path,required=True)
    parser.add_argument('--browsers',default='chromium,firefox')
    parser.add_argument('--layouts',default='table,enterprise,classic')
    parser.add_argument('--chromium-executable')
    parser.add_argument('--css',type=Path,help='Local diagnostic build CSS, instead of dist/index.html')
    parser.add_argument('--extra-css',type=Path,help='Local candidate overlay; never used by CI')
    args=parser.parse_args()
    css=args.css.read_text() if args.css else built_css(args.web_root)
    if args.extra_css: css+='\n'+args.extra_css.read_text()
    args.output.mkdir(parents=True,exist_ok=True)
    report={'scope':'isolated built-CSS fixtures, not authenticated live UAT','threshold':4.5,
            'outboundRequestsAllowed':False,'measurements':[],'failures':[]}
    checks=0
    with sync_playwright() as p:
      for name in args.browsers.split(','):
        options={'headless':True}
        if name=='chromium' and args.chromium_executable: options['executable_path']=args.chromium_executable
        browser=getattr(p,name).launch(**options)
        context=browser.new_context(reduced_motion='reduce')
        context.route('**/*',lambda route:route.abort())
        page=context.new_page();page.set_default_timeout(5000)
        for layout,theme,owner,surface,width in itertools.product(
          tuple(args.layouts.split(',')),('light','dark'),('root','body'),('adopted','plain'),(1786,800)):
          case=f'{name}-{layout}-{theme}-{owner}-{surface}-{width}'
          print('HEADER_CASE='+case,flush=True)
          experience='classic' if layout=='classic' else 'enterprise'
          root_theme=f'data-theme="{theme}"' if owner=='root' else ''
          body_theme=f'data-theme="{theme}"' if owner=='body' else ''
          markup=MARKUP
          if surface=='adopted':
            markup=markup.replace('class="top-bar enterprise-top-bar"','class="top-bar enterprise-top-bar" data-enterprise-page-header="true"')
            markup=markup.replace('class="app-shell enterprise-nav-enabled"','class="app-shell enterprise-nav-enabled projectpulse-module-standard"')
          page.set_viewport_size({'width':width,'height':600})
          page.set_content(f'<!doctype html><html data-pulse-experience="{experience}" data-pulse-layout="{layout}" {root_theme}><head><meta charset="utf-8"><style>{css}</style></head><body {body_theme}>{markup}</body></html>')
          page.evaluate(SETTLE)
          for control,selector in CONTROLS.items():
            paints={}
            for state in STATES:
              set_state(page,selector,state);result=page.evaluate(MEASURE,selector)
              row={'case':case,'control':control,'state':state,**result};report['measurements'].append(row)
              errors=[]
              if not result['visible']:errors.append('control_not_visible')
              if result['background'][3]!=1:errors.append('control_background_not_opaque')
              if result['disabled']!=(state=='disabled'):errors.append('disabled_semantics_changed')
              if (result['ariaDisabled']=='true')!=(state=='aria-disabled'):errors.append('aria_disabled_semantics_changed')
              if state=='focus' and not(result['focusVisible'] and result['outlineWidth']>=2 and result['outlineStyle']!='none'):errors.append('keyboard_focus_indicator_missing')
              for target in row['targets']:
                target['ratio']=contrast(target) if target['visible'] else None
                if not target['visible']:
                  if not(width<=900 and target['target']=='approvals.label'):errors.append(target['target']+':unexpected_hidden_text')
                  continue
                checks+=1
                if target['ratio'] is None or target['ratio']<4.5:errors.append(target['target']+':insufficient_contrast')
                if target['opacity']!=1:errors.append(target['target']+':dimmed_text')
              paints[state]=result['background']
              if errors:report['failures'].append({'case':case,'control':control,'state':state,'errors':errors})
            if paints['default']==paints['hover'] or paints['default']==paints['expanded']:
              report['failures'].append({'case':case,'control':control,'errors':['interactive_state_not_distinct']})
          reset(page);page.evaluate(SETTLE)
          if layout=='table' and owner=='root' and surface=='adopted':
            page.screenshot(path=str(args.output/f'{case}.png'),full_page=True)
        context.close();browser.close()
    ratios=[t['ratio'] for r in report['measurements'] for t in r['targets'] if t.get('ratio') is not None]
    report.update({'status':'failed' if report['failures'] else 'passed','visibleTextChecks':checks,
                   'controlStateChecks':len(report['measurements']),'minimumContrast':min(ratios) if ratios else None})
    (args.output/'header-action-contrast.json').write_text(json.dumps(report,indent=2)+'\n')
    print(json.dumps({k:report[k] for k in ('status','visibleTextChecks','controlStateChecks','minimumContrast')},indent=2))
    if report['failures']:print(json.dumps(report['failures'][:12],indent=2))
    return 1 if report['failures'] else 0


if __name__=='__main__':
    raise SystemExit(main())
