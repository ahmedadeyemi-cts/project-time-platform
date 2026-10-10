"""Exact additive accounting controller projection. Never grants release authority."""
from pathlib import Path
import hashlib,json
ROOT=Path(__file__).resolve().parents[2]
REG=json.loads((ROOT/'tests/accounting-controller-registration.json').read_text())
def normalize(data,file):
 if isinstance(data,str):data=data.encode()
 supervisor=file.endswith('module025-protected-uat-control.yml')
 expected=REG['supervisorSha256' if supervisor else 'controllerSha256']
 assert hashlib.sha256(data).hexdigest()==expected,'Unregistered accounting controller content'
 for before,after in reversed(REG['patches'][file]):
  assert data.count(after.encode())==1,'Accounting projection changed'
  data=data.replace(after.encode(),before.encode(),1)
 assert hashlib.sha256(data).hexdigest()==REG['parentSupervisorSha256' if supervisor else 'parentSha256'],'Accounting predecessor changed'
 return data
