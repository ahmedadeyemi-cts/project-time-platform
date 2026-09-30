from gateway import create_app
from runtime import read_secret
from flask import jsonify, request
from clamd_client import ClamdClient
import subprocess
app=create_app(token=read_secret())
def replica_readiness():
    if request.path!='/health/ready' or request.method!='GET':return None
    try:
        ClamdClient().ready()
        p=subprocess.run(['/usr/bin/tesseract','--version'],stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL,timeout=3,check=False)
        return jsonify(status='ready' if p.returncode==0 else 'not_ready'),200 if p.returncode==0 else 503
    except Exception:return jsonify(status='not_ready'),503
app.before_request_funcs[None].insert(0,replica_readiness)
