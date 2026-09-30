"""Mandatory native scanner startup/reload proof before accepting documents."""
import json,os,signal,socket,subprocess,sys,tempfile,time
from pathlib import Path
from runtime import harden
from clamd_client import ClamdClient
root=Path('/run/clamav');proof=root/'pulse-scanner-ready.json';proof.unlink(missing_ok=True)
harden('scanner')
child=subprocess.Popen(['/usr/sbin/clamd','--foreground=true','--config-file=/etc/clamav/clamd.conf'])
def stop(signum,frame):
    child.terminate()
for signum in (signal.SIGTERM,signal.SIGINT):signal.signal(signum,stop)
try:
    client=ClamdClient();end=time.monotonic()+240
    while True:
        if child.poll() is not None:raise RuntimeError('scanner_start_failed')
        try:engine=client.ready();break
        except Exception:
            if time.monotonic()>end:raise RuntimeError('scanner_start_deadline')
            time.sleep(2)
    with socket.socket(socket.AF_UNIX,socket.SOCK_STREAM) as c:
        c.settimeout(120);c.connect('/run/clamav/clamd.sock');c.sendall(b'zRELOAD'+bytes((0,)))
        reply=c.recv(1024)
        if b'RELOADING' not in reply:raise RuntimeError('scanner_reload_rejected')
    end=time.monotonic()+180
    while True:
        try:engine=client.ready();break
        except Exception:
            if time.monotonic()>end:raise RuntimeError('scanner_reload_deadline')
            time.sleep(2)
    with tempfile.TemporaryDirectory(prefix='pulse-scanner-proof-') as tmp:
        p=Path(tmp)/'clean';p.write_bytes(b'PULSE STARTUP CLEAN FIXTURE')
        result=client.scan(p)
        if not result.clean or result.infected:raise RuntimeError('scanner_startup_proof_failed')
    proof.write_text(json.dumps({'ready':True,'signatureVersion':engine.signatures,'nativeReloadVerified':True}))
    proof.chmod(0o444)
    print('PULSE_SCANNER_NATIVE_RELOAD=PASS',flush=True)
    raise SystemExit(child.wait())
finally:
    proof.unlink(missing_ok=True)
    if child.poll() is None:
        child.terminate()
        try:child.wait(timeout=10)
        except subprocess.TimeoutExpired:child.kill();child.wait()
