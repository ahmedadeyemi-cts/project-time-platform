"""Exact reviewed service-order projection; never grants deployment authority.
All existing UAT remains mandatory and runs against the activated services.
"""
import hashlib
BASE_SHA256='6c587203f890a5525e041c750fc5076b688db81aa5c51345d0a659706a7449ff'
LEGACY_SHA256='11a5cbc14270c720518a37a5bcd539bac650d8c49b374f6fb155b901147c74d3'
CURRENT_SHA256='6c7d701fc02599626f5fcfb638f4f5d4809e27102dbeb2085ac5c7f81532ca62'
BLOCK_SHA256='4c235d3c70ae9e93dc237b9fa62ebe86f818e4b5a87074c309ce6130d7689ce5'
PRE_BLOCK_SHA256='2231356843b8111d165b2986b008cd67a100a61eb591119a12162ef1e63d60c0'
POST_BLOCK_SHA256='88800a50a5637fb41d51b60c3fa4edd52c6f57500696f7203dc7d4252929663d'
START=b'      - name: Build private Pulse service images\n'
MIDDLE=b'      - name: Run protected-Test authenticated functional UAT\n'
FINISH=b'      - name: Finalize private Pulse activation after full application acceptance\n'
END=b'      - name: Restore exact prior Test images after application failure\n'
OLD_ROLLBACK=b"&& steps.uat.outputs.deployment_health_verified != 'true' && steps.psa_live_uat.outputs.deployment_health_verified != 'true' && steps.sow_role_uat.outputs.deployment_health_verified != 'true') }}"
POST_ACCEPTANCE_ROLLBACK=b"&& ((steps.uat.outputs.deployment_health_verified != 'true' && steps.psa_live_uat.outputs.deployment_health_verified != 'true' && steps.sow_role_uat.outputs.deployment_health_verified != 'true') || steps.pulse_private_services_final.outcome == 'failure')) }}"
def normalize(data):
    if isinstance(data,str):data=data.encode()
    digest=hashlib.sha256(data).hexdigest()
    if digest==BASE_SHA256:return data
    assert digest in (LEGACY_SHA256,CURRENT_SHA256),'Unregistered controller content'
    assert data.count(START)==1 and data.count(END)==1,'Controller insertion boundary changed'
    lo=data.index(START)
    if digest==LEGACY_SHA256:
        hi=data.index(END,lo)
        assert hashlib.sha256(data[lo:hi]).hexdigest()==BLOCK_SHA256,'Legacy insertion changed'
        parent=data[:lo]+data[hi:]
    else:
        assert data.count(MIDDLE)==1 and data.count(FINISH)==1,'Post-activation acceptance boundary changed'
        mid=data.index(MIDDLE,lo);tail=data.index(FINISH,mid);end=data.index(END,tail)
        assert hashlib.sha256(data[lo:mid]).hexdigest()==PRE_BLOCK_SHA256,'Service preparation changed'
        assert hashlib.sha256(data[tail:end]).hexdigest()==POST_BLOCK_SHA256,'Acceptance finalization changed'
        parent=data[:lo]+data[mid:tail]+data[end:]
        assert parent.count(POST_ACCEPTANCE_ROLLBACK)==1,'Mandatory post-activation rollback changed'
        parent=parent.replace(POST_ACCEPTANCE_ROLLBACK,OLD_ROLLBACK,1)
    assert hashlib.sha256(parent).hexdigest()==BASE_SHA256,'Existing execution or authorization changed'
    return parent
