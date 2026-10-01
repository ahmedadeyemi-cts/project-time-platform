"""Recognize only the exact additive controller for historical-regression replay.
This module grants no deployment authority and changes no runtime recovery guard.
"""
import hashlib
BASE_SHA256='6c587203f890a5525e041c750fc5076b688db81aa5c51345d0a659706a7449ff'
CURRENT_SHA256='11a5cbc14270c720518a37a5bcd539bac650d8c49b374f6fb155b901147c74d3'
BLOCK_SHA256='4c235d3c70ae9e93dc237b9fa62ebe86f818e4b5a87074c309ce6130d7689ce5'
START=b'      - name: Build private Pulse service images\n'
END=b'      - name: Restore exact prior Test images after application failure\n'
def normalize(data):
    if isinstance(data,str):data=data.encode()
    digest=hashlib.sha256(data).hexdigest()
    if digest==BASE_SHA256:return data
    assert digest==CURRENT_SHA256,'Unregistered controller content'
    assert data.count(START)==1 and data.count(END)==1,'Controller insertion boundary changed'
    lo=data.index(START);hi=data.index(END,lo)
    assert hashlib.sha256(data[lo:hi]).hexdigest()==BLOCK_SHA256,'Activation block changed'
    parent=data[:lo]+data[hi:]
    assert hashlib.sha256(parent).hexdigest()==BASE_SHA256,'Pre-existing execution or authorization changed'
    return parent
