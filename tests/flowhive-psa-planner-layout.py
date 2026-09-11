"""Chromium table-layout regression using application styles in import order.

Install: python -m pip install playwright==1.57.0
Browser: python -m playwright install chromium
Run: python tests/flowhive-psa-planner-layout.py
CHROME_PATH may select an existing browser. This is a DOM/CSS regression fixture,
not an authenticated application or persistence acceptance test.
"""
import json
import os
from pathlib import Path
from playwright.sync_api import sync_playwright

ROOT = Path(__file__).resolve().parents[1]
SRC = ROOT / 'src/frontend/project-time-web/src'

def stylesheet(name: str) -> str:
    return (SRC / name).read_text(encoding='utf-8')

confidence = stylesheet('project-flowhive-ai-confidence.css')
assert "@import './project-flowhive-planner-layout.css';" in confidence
styles = '\n'.join([
    stylesheet('project-flowhive-center.css'),
    stylesheet('project-flowhive-planner-layout.css'),
    confidence.replace("@import './project-flowhive-planner-layout.css';", ''),
    stylesheet('projectpulse-module-standard.css'),
])
headers = ['WBS', 'Task name', 'Start date', 'End date', 'Days', 'Progress',
           'Predecessor', 'Type', 'Comments', 'Notes', 'Assigned identity']
long_description = ('Record the project-specific migration readiness outcome '
                    'and retain its validation evidence. ') * 8
date_input = '<input type="date" value="2026-09-08" aria-label="Scheduled date">'
rows = ''.join(f'''<tr class="flowhive-work-row phase-plan">
<td><span class="flowhive-wbs-child">1.{index + 1}</span></td>
<td><div class="flowhive-task-name-control"><input value="Review migration readiness" aria-label="Task name"><button class="flowhive-inline-detail-button">Task details</button><button class="danger-quiet">Delete</button></div><small>{long_description}</small></td>
<td>{date_input}</td><td>{date_input}</td>
<td><div class="flowhive-duration-cell"><input type="number" value="2"><span>days</span></div></td>
<td><input type="number" value="0"></td><td><select><option>1.1</option></select></td><td><select><option>FS</option></select></td>
<td><textarea class="flowhive-sheet-textarea">Review comments</textarea></td><td><textarea class="flowhive-sheet-textarea">Internal notes</textarea></td><td><select><option>Unassigned</option></select></td></tr>''' for index in range(4))
html = f'''<!doctype html><html><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
<style>html{{font:16px Arial,sans-serif}}body{{margin:16px}}main{{display:grid;min-width:0}}{styles}</style></head><body><main>
<section class="project-flowhive-center projectpulse-module-standard"><div class="flowhive-view-panel"><div class="flowhive-table-wrap">
<table class="flowhive-task-table flowhive-planner-table flowhive-smartsheet-table"><thead><tr>{''.join(f'<th>{value}</th>' for value in headers)}</tr></thead><tbody>{rows}
<tr class="flowhive-task-detail-row"><td colspan="11"><div class="flowhive-task-detail-panel">Expanded task details remain in the scroll surface.</div></td></tr></tbody></table>
</div></div></section></main></body></html>'''

with sync_playwright() as playwright:
    options = {'headless': True}
    if os.environ.get('CHROME_PATH'):
        options['executable_path'] = os.environ['CHROME_PATH']
    browser = playwright.chromium.launch(**options)
    try:
        for theme in ('light', 'dark'):
            for width in (1440, 390):
                page = browser.new_page(viewport={'width': width, 'height': 1000})
                try:
                    page.set_content(html, wait_until='load')
                    page.evaluate('(theme) => document.documentElement.dataset.theme = theme', theme)
                    results = page.evaluate('''() => {
                        const checks = [];
                        const check = (name, passed) => checks.push({name, passed: Boolean(passed)});
                        const wrapper = document.querySelector('.flowhive-table-wrap');
                        const table = document.querySelector('table');
                        const nameCell = document.querySelector('.flowhive-work-row td:nth-child(2)');
                        const rect = nameCell.getBoundingClientRect();
                        check('no page-level horizontal overflow', document.documentElement.scrollWidth <= window.innerWidth + 1);
                        check('task grid scroll is contained', wrapper.scrollWidth > wrapper.clientWidth && wrapper.clientWidth <= window.innerWidth);
                        check('task name column is bounded', rect.width >= 300 && rect.width <= 400);
                        check('description wraps', getComputedStyle(nameCell.querySelector('small')).whiteSpace === 'normal');
                        check('task action buttons remain inside name cell', [...nameCell.querySelectorAll('button')].every(button => {
                            const r = button.getBoundingClientRect();
                            return r.left >= rect.left && r.right <= rect.right + 1 && r.height >= 38;
                        }));
                        check('date control is usable', document.querySelector('input[type=date]').getBoundingClientRect().width >= 100);
                        check('expanded details are not a frozen WBS cell', getComputedStyle(document.querySelector('.flowhive-task-detail-row td')).position === 'static');
                        wrapper.scrollLeft = 560;
                        if (window.innerWidth <= 700) check('mobile name cell does not hide dates', getComputedStyle(nameCell).position === 'static');
                        else check('desktop task name stays frozen', Math.abs(nameCell.getBoundingClientRect().left - wrapper.getBoundingClientRect().left - 80) < 3);
                        wrapper.scrollLeft = wrapper.scrollWidth;
                        check('last assignment field remains reachable', table.querySelector('tbody tr td:last-child').getBoundingClientRect().right <= wrapper.getBoundingClientRect().right + 2);
                        return checks;
                    }''')
                    failed = [item['name'] for item in results if not item['passed']]
                    assert not failed, f'{theme}/{width}: {failed}'
                    print(json.dumps({'status': 'passed', 'theme': theme, 'width': width, 'assertions': len(results)}))
                finally:
                    page.close()
    finally:
        browser.close()
