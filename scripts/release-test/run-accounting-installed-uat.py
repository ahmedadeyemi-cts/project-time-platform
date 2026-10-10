#!/usr/bin/env python3
"""Read-only accounting acceptance against installed protected Test; no AI calls."""
import json,os,urllib.request,urllib.error,uuid
from pathlib import Path
ORIGIN='https://phd-west-test.onenecklab.com'
class NoRedirect(urllib.request.HTTPRedirectHandler):
 def redirect_request(self,*args):raise RuntimeError('accounting_redirect_denied')
opener=urllib.request.build_opener(NoRedirect());token=None;checks=[]
def request(path,payload=None,auth=True,binary=False):
 headers={'Accept':'application/json','Origin':ORIGIN,'Sec-Fetch-Site':'same-origin','X-ProjectPulse-Module-Number':'030','Cache-Control':'no-cache'}
 if auth and token:headers.update({'Authorization':'Bearer '+token,'X-ProjectPulse-Session':token})
 body=None
 if payload is not None:headers['Content-Type']='application/json';body=json.dumps(payload).encode()
 req=urllib.request.Request(ORIGIN+path,body,headers)
 with opener.open(req,timeout=90) as r:
  content=r.read(16*1024*1024+1)
  if len(content)>16*1024*1024:raise RuntimeError('accounting_response_limit')
  return content if binary else json.loads(content)
try:
 try:request('/api/enterprise-reporting/catalog',auth=False)
 except urllib.error.HTTPError as e:assert e.code in (401,403)
 else:raise RuntimeError('anonymous_accounting_access')
 checks.append('anonymous_denied')
 login=request('/api/auth/local/login',{'username':'jason.mosier@ussignal.local','password':os.environ['TEST_LOGIN_PASSWORD']},False)
 token=login.get('sessionToken');assert token
 catalog=request('/api/enterprise-reporting/catalog');codes={r['code'] for r in catalog['reports']}
 expected={'accounting_engagement_summary','accounting_invoice_ledger','accounting_milestone_detail','accounting_billable_time','accounting_monthly_revenue'}
 assert expected<=codes;checks.append('five_accounting_reports_installed')
 for code in sorted(expected):
  report=request('/api/enterprise-reporting/preview',{'reportCode':code,'limit':1})
  assert report['result']['resultStatus'] in ('complete','no_data'), 'accounting_source_not_complete'
  assert report['result']['columns'];checks.append(code)
 # Invalid write exercises route binding and validation without changing financial data.
 try:request('/api/billing/projects/'+str(uuid.uuid4())+'/accounting',{'operationId':str(uuid.uuid4()),'action':'entry','amount':0,'date':'2020-01-01','reference':'UAT','reason':'Invalid payload only','kind':'revenue'})
 except urllib.error.HTTPError as e:assert e.code in (400,403,404)
 else:raise RuntimeError('invalid_accounting_write_accepted')
 checks.append('invalid_write_denied')
 print('ACCOUNTING_INSTALLED_UAT=PASS checks='+str(len(checks)))
 receipt={'status':'passed','checks':checks,'productionMutation':False,'fixtureMutation':False,'celarAcceptance':'PENDING_NOT_EXECUTED'}
 Path(os.environ['EVIDENCE_DIR'],'accounting-installed-uat.json').write_text(json.dumps(receipt))
finally:
 if token:
  try:request('/api/auth/session/logout',{})
  except Exception:pass
