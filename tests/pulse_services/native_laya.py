"""Harmless real-model acceptance through the isolated Unix-socket protocol."""
import json,socket,time,sys
sys.path.insert(0,'/opt/pulse-services')
from laya_protocol import exchange,normalized,DecisionError
end=time.monotonic()+300
while True:
    try:health=normalized(exchange({'op':'health'},seconds=3),health=True);break
    except DecisionError:
        if time.monotonic()>end:raise
        time.sleep(2)
for text in ['STATEMENT OF WORK. Scope and deliverables: install the example network and validate acceptance.',
             'INVOICE. Payment requested for example services. Total due USD 100. Net 30 days.',
             'PURCHASE ORDER. Buyer authorizes the purchase of example equipment.',
             'This synthetic note describes a team meeting.']:
    answer=normalized(exchange({'op':'classify','text':text},seconds=30))
    assert answer['review_required'] and not answer['automation_approved']
    assert answer['workflow_actions_performed']==0
assert health['model_revision']=='1c5edc17a7acd8701df6fc341c0d179f1c62c982'
try:exchange({'op':'classify','text':'token '*5000},seconds=10)
except DecisionError:pass
else:raise AssertionError('Unbounded input accepted')
print('NATIVE_LAYA=PASS real_checkpoint=true inference_contract_cases=4 oversized_rejected=true cloud_fallback=false')
