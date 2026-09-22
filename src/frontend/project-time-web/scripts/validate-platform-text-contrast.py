#!/usr/bin/env python3
"""Exercise production CSS against shared Pulse DOM contracts, not live data.

Requires playwright and Pillow in a test-only environment. Run npm run build
first. No credentials, application APIs, AI providers or deployment are used.
"""
from __future__ import annotations

import argparse
import functools
import json
import re
import threading
from http.server import SimpleHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path

from PIL import Image
from playwright.sync_api import sync_playwright


def contrast(foreground: list[float], background: tuple[int, ...], opacity: float) -> float:
    alpha = foreground[3] * opacity
    composited = [foreground[i] * alpha + background[i] * (1 - alpha) for i in range(3)]
    def luminance(color):
        channels = [c / 255 for c in color[:3]]
        linear = [c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4 for c in channels]
        return sum(c * weight for c, weight in zip(linear, (0.2126, 0.7152, 0.0722)))
    low, high = sorted((luminance(composited), luminance(background)))
    return (high + 0.05) / (low + 0.05)


def label(tag: str, name: str, text: str, attrs: str = "") -> str:
    return f'<{tag} data-contrast-check="{name}" {attrs}>{text}</{tag}>'


def fixtures() -> str:
    fragments = [f'''<article class="contrast-fixture">
      <div class="welcome-dashboard-hero">
        <div>{label('p', 'dashboard.eyebrow', 'PULSE', 'class="eyebrow"')}
          {label('h1', 'dashboard.title', 'Good morning, team')}
          {label('p', 'dashboard.copy', 'Here is what needs your attention today.')}</div>
        <div class="welcome-dashboard-date">
          {label('span', 'dashboard.date', 'Tuesday, September 22')}
          {label('small', 'dashboard.workspace', 'Administrator workspace')}</div>
        <div class="welcome-quick-actions">
          {label('a', 'dashboard.action', 'Work Register', 'href="#work-register"')}
        </div>
      </div></article>''']
    for name, wrapper, header in (
        ('standard', 'projectpulse-module-standard', ''),
        ('enterprise', 'uss-enterprise-module-page', 'class="uss-enterprise-page-header"'),
        ('adopted', 'project-workspace', 'data-enterprise-page-header="true"'),
    ):
        fragments.append(f'''<article class="contrast-fixture {wrapper}"><header {header}>
          {label('p', name + '.eyebrow', 'MODULE WORKSPACE', 'class="eyebrow"')}
          {label('h1', name + '.heading', 'Project workspace')}
          {label('p', name + '.description', 'Review assignments, hours and project status.')}
          {label('small', name + '.secondary', 'Assigned to your team')}
          {label('strong', name + '.metric', '12 active projects')}
          <div class="contrast-controls">
            <button class="primary-action">{label('span', name + '.primary', 'Create project')}</button>
            <button class="secondary-action">{label('small', name + '.secondary-button', 'View details')}</button>
            <button class="primary-action" disabled>{label('strong', name + '.disabled', 'Unavailable')}</button>
          </div>
          <div class="panel uss-status-card" data-contrast-surface="base">
            {label('h3', name + '.inset-heading', 'Review information')}
            {label('small', name + '.inset-copy', 'This is an independent card inside a header.')}
            <label>Filter <input data-contrast-check="{name}.input" value="Assigned projects"></label>
          </div>
        </header></article>''')
    legacy_surfaces = (
        'group2b-resilience-hero', 'group3-hero', 'group4-hero', 'group5-hero',
        'project-closeout-hero', 'closeout-email-hero', 'engineer-closeout-hero',
        'flowhive-hero', 'capacity-forecast-hero', 'defect-tracker-hero',
        'governed-operations-hero', 'eg-hero', 'ffla-hero', 'ffl-hero',
        'celar-ai-platform-hero', 'pulse-ai-doc-header', 'pulse-ai-rag-header',
        'pulse-ai-runtime-header', 'pulse-ai-hero', 'pulse-ai-deep-header',
        'celar-ops-header', 'modules-directory-hero',
        'projectpulse-view-as-drawer__header', 'pulse-display-preferences-drawer__header',
    )
    for surface in legacy_surfaces:
        fragments.append(f'''<article class="contrast-fixture"><div class="{surface}">
          {label('h2', surface + '.title', 'Workspace overview')}
          {label('p', surface + '.copy', 'Review the information for your role.')}
          {label('small', surface + '.detail', 'Updated today')}
          {label('span', surface + '.label', 'Project status')}
        </div></article>''')
    fragments.append(f'''<article class="contrast-fixture"><div class="welcome-card">
      <div class="welcome-card-heading">
        {label('span', 'dashboard-card.eyebrow', 'YOUR PROJECTS')}
        {label('h2', 'dashboard-card.title', 'Current assignments')}
      </div>
      {label('p', 'dashboard-card.copy', 'There are no overdue assignments.', 'class="welcome-card-copy"')}
      {label('a', 'dashboard-card.link', 'View assigned projects', 'class="welcome-card-link" href="#project-workspace"')}
    </div></article>''')
    fragments.append(f'''<article class="contrast-fixture celar-ai-contextual-chat">
      <header class="help-header">
        {label('strong', 'assistant.title', 'Ask Celar AI')}
        {label('span', 'assistant.copy', 'Help with your current workspace')}
      </header>
    </article>''')
    fragments.append('''<article class="contrast-fixture panel">
      <h2>Standard controls</h2>
      <button class="primary-action"><span data-contrast-check="control.primary">Create project</span></button>
      <button class="primary-action" disabled><span data-contrast-check="control.disabled">Unavailable</span></button>
      <button class="secondary-action"><small data-contrast-check="control.secondary">View details</small></button>
      <div class="pulse-experience-switcher"><button class="active"><span data-contrast-check="switcher.active">Enterprise</span></button></div>
      <div class="enterprise-top-bar"><nav class="enterprise-top-navigation"><a class="active" href="#dashboard"><span data-contrast-check="navigation.active">Dashboard</span></a></nav></div>
      <div class="pulse-header-theme-switcher"><button class="active"><span data-contrast-check="theme.active">Dark</span></button></div>
      <div class="tabs"><button class="active"><span data-contrast-check="tab.active">Overview</span></button></div>
    </article>''')
    for name, table_class in (('matrix', 'roles-matrix-table'), ('shared', 'uss-table'), ('generic', 'contrast-table')):
        fragments.append(f'''<article class="contrast-fixture"><div class="contrast-scroll">
          <table class="{table_class}"><thead><tr>
            <th>{label('span', name + '.page', 'Page')}</th>
            <th>{label('span', name + '.permission', 'Permission')}</th>
            <th>{label('span', name + '.description', 'Description')}</th>
            <th><div class="{'roles-matrix-role-heading' if name == 'matrix' else ''}">
              {label('strong', name + '.role', 'Project Management Lead')}
              {label('small', name + '.role-code', 'PROJECT_MANAGEMENT_LEAD')}
            </div></th></tr></thead><tbody><tr>
            <td>{label('strong', name + '.body', '001: Timesheet')}</td>
            <td>{label('small', name + '.body-copy', 'View module')}</td>
            <td>Current permissions</td>
            <td class="granted"><button class="roles-matrix-cell-button">
              {label('strong', name + '.allow', 'Allow')}
              {label('small', name + '.scope', 'MANAGED_PROJECTS')}
            </button></td></tr></tbody></table></div></article>''')
    return '\n'.join(fragments)


MEASURE = r'''() => [...document.querySelectorAll('[data-contrast-check]')].map(el => {
  const style = getComputedStyle(el);
  const range = document.createRange();
  range.selectNodeContents(el);
  const rect = el.matches('input,select,textarea') ? el.getBoundingClientRect() : range.getBoundingClientRect();
  const color = style.webkitTextFillColor || style.color;
  const rgba = color.match(/[\d.]+/g)?.map(Number);
  if (!rgba || !color.startsWith('rgb')) throw new Error(`Unsupported rendered color: ${color}`);
  if (rgba.length === 3) rgba.push(1);
  let opacity = 1;
  let hidden = false;
  for (let node = el; node; node = node.parentElement) {
    const css = getComputedStyle(node);
    opacity *= Number(css.opacity);
    hidden ||= css.display === 'none' || css.visibility === 'hidden';
  }
  return {name: el.dataset.contrastCheck, color, rgba, opacity, hidden,
    rect: {x: rect.x + scrollX, y: rect.y + scrollY, width: rect.width, height: rect.height}};
})'''


class QuietHandler(SimpleHTTPRequestHandler):
    def log_message(self, *_args):
        pass


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--web-root', type=Path, default=Path(__file__).resolve().parents[1])
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--browsers', default='chromium,firefox')
    parser.add_argument('--chromium-executable', default=None)
    args = parser.parse_args()
    dist = args.web_root / 'dist'
    index = (dist / 'index.html').read_text()
    links = re.findall(r'<link\b[^>]*rel=["\']stylesheet["\'][^>]*>', index)
    if not links:
        raise RuntimeError('No production stylesheets found. Run npm run build first.')
    args.output.mkdir(parents=True, exist_ok=True)
    document = '''<!doctype html><html><head><meta charset="utf-8">''' + ''.join(links) + '''
      <style>
        .contrast-fixtures { display: grid !important; gap: 28px; max-width: 1400px; margin: 0 auto; padding: 24px; }
        .contrast-fixture { display: block !important; min-width: 0; }
        .contrast-controls { display: flex; gap: 12px; margin: 16px 0; }
        .contrast-scroll { overflow: auto; }
        .contrast-fixture .panel { margin-top: 20px; padding: 20px; }
      </style></head><body><div id="root"><main class="app-shell enterprise-nav-enabled">
      <div class="contrast-fixtures">''' + fixtures() + '''</div></main></div></body></html>'''
    fixture_path = dist / '__contrast_test__.html'
    fixture_path.write_text(document)
    server = ThreadingHTTPServer(('127.0.0.1', 0), functools.partial(QuietHandler, directory=str(dist)))
    thread = threading.Thread(target=server.serve_forever, daemon=True)
    thread.start()
    failures, measurements = [], []
    base_url = f'http://127.0.0.1:{server.server_port}'
    try:
        with sync_playwright() as playwright:
            for engine in args.browsers.split(','):
                launch = {'headless': True}
                if engine == 'chromium' and args.chromium_executable:
                    launch['executable_path'] = args.chromium_executable
                    launch['args'] = ['--no-sandbox']
                browser = getattr(playwright, engine).launch(**launch)
                try:
                    page = browser.new_page(viewport={'width': 1600, 'height': 1100}, device_scale_factor=1)
                    page.route('**/*', lambda route: route.continue_() if route.request.url.startswith(base_url) else route.abort())
                    page.goto(base_url + '/__contrast_test__.html', wait_until='networkidle')
                    for experience in ('enterprise', 'classic'):
                        for theme in ('light', 'dark'):
                            for theme_owner in ('root', 'body'):
                                case = f'{engine}-{experience}-{theme}-{theme_owner}'
                                page.evaluate('''({experience,theme,owner}) => {
                                  document.documentElement.dataset.pulseExperience = experience;
                                  document.documentElement.removeAttribute('data-theme');
                                  document.body.removeAttribute('data-theme');
                                  (owner === 'root' ? document.documentElement : document.body).dataset.theme = theme;
                                }''', {'experience': experience, 'theme': theme, 'owner': theme_owner})
                                page.evaluate('document.fonts.ready')
                                page.wait_for_timeout(100)
                                measured = page.evaluate(MEASURE)
                                page.screenshot(path=str(args.output / f'{case}.png'), full_page=True)
                                # Hide glyph paint ONLY: currentColor-dependent backgrounds and
                                # the original layout/color/opacity remain unchanged.
                                paint = page.evaluate_handle("""() => [...document.querySelectorAll('.contrast-fixtures, .contrast-fixtures *')].map(el => {
                                  const saved = ['-webkit-text-fill-color', 'text-shadow'].map(key =>
                                    [key, el.style.getPropertyValue(key), el.style.getPropertyPriority(key)]);
                                  el.style.setProperty('-webkit-text-fill-color', 'transparent', 'important');
                                  el.style.setProperty('text-shadow', 'none', 'important');
                                  return {el, saved};
                                })""")
                                raw = page.screenshot(full_page=True)
                                paint.evaluate("""items => items.forEach(({el, saved}) => saved.forEach(([key, value, priority]) => {
                                  if (value) el.style.setProperty(key, value, priority);
                                  else el.style.removeProperty(key);
                                }))""")
                                paint.dispose()
                                from io import BytesIO
                                backdrop = Image.open(BytesIO(raw)).convert('RGB')
                                for item in measured:
                                    rect = item['rect']
                                    if item['hidden'] or rect['width'] <= 0 or rect['height'] <= 0:
                                        failures.append({'case': case, 'name': item['name'], 'error': 'Text is not visible'})
                                        continue
                                    ratios = []
                                    for fx in (0.1, 0.3, 0.5, 0.7, 0.9):
                                        for fy in (0.25, 0.5, 0.75):
                                            x = max(0, min(backdrop.width - 1, int(rect['x'] + rect['width'] * fx)))
                                            y = max(0, min(backdrop.height - 1, int(rect['y'] + rect['height'] * fy)))
                                            ratios.append(contrast(item['rgba'], backdrop.getpixel((x, y)), item['opacity']))
                                    result = {'case': case, 'name': item['name'], 'ratio': min(ratios), 'color': item['color']}
                                    measurements.append(result)
                                    if min(ratios) < 4.5:
                                        failures.append(result)
                finally:
                    browser.close()
    finally:
        server.shutdown()
        server.server_close()
        fixture_path.unlink(missing_ok=True)
    inventory = [str(path.relative_to(args.web_root)) for path in sorted((args.web_root / 'src').rglob('*.css'))]
    report = {
        'status': 'FAIL' if failures else 'PASS', 'checks': len(measurements),
        'stylesheetInventory': inventory, 'failures': failures, 'measurements': measurements,
        'scope': 'Production CSS against shared DOM fixtures in both themes and presentation modes.',
        'limitations': 'Not authenticated role-by-role UAT; no live application data or APIs were accessed.'
    }
    (args.output / 'contrast-report.json').write_text(json.dumps(report, indent=2) + '\n')
    print(f"PULSE_LIGHT_DARK_CONTRAST={report['status']} checks={len(measurements)} stylesheets={len(inventory)}")
    for failure in failures:
        print(json.dumps(failure))
    return 1 if failures else 0


if __name__ == '__main__':
    raise SystemExit(main())
