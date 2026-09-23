#!/usr/bin/env python3
"""Use actual built static bytes and Nginx headers; synthetic Teams host only."""
import json
from pathlib import Path
import sys
import time
from urllib.request import urlopen
from urllib.error import HTTPError, URLError
from playwright.sync_api import sync_playwright

output=Path(sys.argv[1]); output.mkdir(parents=True,exist_ok=True)
base='http://127.0.0.1:18080'
def fetch(path):
    try:
        with urlopen(base+path,timeout=5) as response: return response.status,dict(response.headers),response.read()
    except HTTPError as error: return error.code,dict(error.headers),error.read()
for attempt in range(30):
    try: status, headers, content=fetch('/teams-notifications/index.html'); break
    except URLError: time.sleep(1)
else: raise RuntimeError('Isolated Nginx did not start')
assert status==200
lower={k.lower():v for k,v in headers.items()}
assert 'x-frame-options' not in lower
assert 'https://*.cloud.microsoft' in lower['content-security-policy']
assert 'frame-ancestors https://teams.microsoft.com' in lower['content-security-policy']
for path in ['/index.html','/api/not-a-real-endpoint']:
    _,normal,_=fetch(path); normal={k.lower():v for k,v in normal.items()}
    assert normal['x-frame-options']=='SAMEORIGIN'
    assert "frame-ancestors 'self'" in normal['content-security-policy']
assert b'client_secret' not in content and b'localStorage' not in content
report={'headers':'passed','liveMicrosoftCalls':0,'cases':[]}
with sync_playwright() as p:
    browser=p.chromium.launch()
    for theme in ['light','dark']:
        for width in [360,1440]:
            page=browser.new_page(viewport={'width':width,'height':900},color_scheme=theme)
            unexpected=[]; errors=[]
            page.on('pageerror',lambda e:errors.append(str(e)))
            def route_handler(route):
                url=route.request.url
                if url=='https://teams.microsoft.com/test-host':
                    route.fulfill(content_type='text/html',body='<iframe title="Pulse" style="border:0;width:100%;height:850px" src="https://pulse.example.invalid/teams-notifications/index.html"></iframe>'); return
                if url.startswith('https://res.cdn.office.net/teams-js/'):
                    route.fulfill(content_type='text/javascript',body=f"window.microsoftTeams={{app:{{initialize:async()=>{{window.hostInitialized=true}},notifySuccess:()=>{{window.hostReady=true}},getContext:async()=>({{app:{{theme:'{theme}'}}}}),registerOnThemeChangeHandler:()=>{{}}}}}};"); return
                if url.startswith('https://pulse.example.invalid/teams-notifications/'):
                    suffix=url.removeprefix('https://pulse.example.invalid'); code,h,b=fetch(suffix)
                    h={k:v for k,v in h.items() if k.lower() not in ('transfer-encoding','connection','content-length','content-encoding')}
                    route.fulfill(status=code,headers=h,body=b); return
                unexpected.append(url);route.abort()
            page.route('**/*',route_handler)
            page.goto('https://teams.microsoft.com/test-host')
            frame=page.frame_locator('iframe')
            frame.locator('#host-status').filter(has_text='PulseApp is open in Teams').wait_for()
            assert frame.locator('html').get_attribute('data-theme')==theme
            assert frame.locator('#open-pulse').get_attribute('href')=='/#dashboard'
            assert frame.locator('#open-pulse').get_attribute('target')=='_blank'
            assert frame.locator('body').evaluate('(e)=>e.scrollWidth <= window.innerWidth')
            assert not unexpected and not errors,(unexpected,errors)
            page.screenshot(path=str(output/f'landing-{theme}-{width}.png'),full_page=True)
            report['cases'].append({'theme':theme,'width':width,'initialized':True,'unexpectedRequests':0,'pageErrors':0})
            page.close()
    browser.close()
(output/'landing-acceptance.json').write_text(json.dumps(report,indent=2)+'\n')
print('TEAMS_LANDING_AND_EXACT_HEADER_BOUNDARY=PASS; LIVE_TEAMS_CLIENT=NOT_TESTED')
