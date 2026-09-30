"""Managed-container startup helpers; no Azure credentials or business access."""
import ctypes, os, re, stat, sys, tempfile, time
from pathlib import Path

def harden(profile="gateway"):
    lib=ctypes.CDLL('/usr/local/lib/libpulse-sandbox.so',use_errno=True)
    lib.pulse_sandbox.argtypes=[ctypes.c_char_p];lib.pulse_sandbox.restype=ctypes.c_int
    if lib.pulse_sandbox(profile.encode()) != 0:
        raise RuntimeError('PULSE_ISOLATION_UNAVAILABLE')

def secret_file():
    raw=os.environ.pop('PULSE_SERVICE_TOKEN','')
    if re.fullmatch(r'[A-Za-z0-9_-]{32,4096}',raw) is None:raise RuntimeError('service_credential_invalid')
    directory=Path(tempfile.mkdtemp(prefix='pulse-service-secret-'))
    target=directory/'token'
    fd=os.open(target,os.O_WRONLY|os.O_CREAT|os.O_EXCL|os.O_NOFOLLOW,0o400)
    with os.fdopen(fd,'w') as f:f.write(raw)
    os.environ['PULSE_SERVICE_TOKEN_FILE']=str(target)
    return target

def read_secret():
    path=Path(os.environ['PULSE_SERVICE_TOKEN_FILE'])
    fd=os.open(path,os.O_RDONLY|os.O_NOFOLLOW)
    with os.fdopen(fd,'rb') as f:
        info=os.fstat(f.fileno())
        if info.st_uid!=os.getuid() or info.st_mode&0o077:raise RuntimeError('service_credential_permissions')
        value=f.read(4097).decode('ascii')
    if re.fullmatch(r'[A-Za-z0-9_-]{32,4096}',value) is None:raise RuntimeError('service_credential_invalid')
    return value

def start():
    mode=sys.argv[1]
    if mode in ('documents','laya-gateway'):
        secret_file()
        module='docs_wsgi:app' if mode=='documents' else 'laya_wsgi:app'
        os.execvp('gunicorn',['gunicorn','--config','/opt/pulse-services/gunicorn.conf.py',module])
    if mode=='scanner':
        end=time.monotonic()+600
        while time.monotonic()<end:
            if any((Path('/var/lib/clamav')/x).is_file() for x in ('daily.cvd','daily.cld')):break
            time.sleep(2)
        else:raise RuntimeError('signature_bootstrap_unavailable')
        os.execv('/usr/local/bin/pulse-sandbox',['pulse-sandbox','scanner','/usr/sbin/clamd','--foreground=true','--config-file=/etc/clamav/clamd.conf'])
    if mode=='updater':
        os.execv('/usr/local/bin/pulse-sandbox',['pulse-sandbox','updater','/usr/bin/freshclam','--daemon','--foreground=true','--config-file=/etc/clamav/freshclam.conf'])
    raise RuntimeError('startup_mode_invalid')
if __name__=='__main__':start()
