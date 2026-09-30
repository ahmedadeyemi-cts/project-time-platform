from gateway import create_app
from runtime import read_secret
from flask import jsonify, request
from clamd_client import ClamdClient
import subprocess,json
from pathlib import Path
app=create_app(token=read_secret())
def replica_readiness():
    if request.path!='/health/ready' or request.method!='GET':return None
    try:
        engine=ClamdClient().ready()
        proof=json.loads(Path('/run/clamav/pulse-scanner-ready.json').read_text())
        if proof.get('nativeReloadVerified') is not True or not proof.get('ready'):raise RuntimeError('native_reload_not_verified')
        p=subprocess.run(['/usr/bin/tesseract','--version'],stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL,timeout=3,check=False)
        return jsonify(status='ready' if p.returncode==0 else 'not_ready'),200 if p.returncode==0 else 503
    except Exception:return jsonify(status='not_ready'),503
app.before_request_funcs[None].insert(0,replica_readiness)

@app.before_request
def scanner_proof():
    if request.path=='/health/live':return None
    try:
        proof=json.loads(Path('/run/clamav/pulse-scanner-ready.json').read_text())
        if proof.get('nativeReloadVerified') is True and proof.get('ready'):return None
    except Exception:pass
    return jsonify(error={'code':'scanner_startup_proof_missing'},clean=False),503
