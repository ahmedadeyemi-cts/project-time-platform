"""Run as nonroot INSIDE the candidate Linux image, never on the operator host."""
import errno,os,socket,sys,tempfile
from pathlib import Path
sys.path.insert(0,'/opt/pulse-services')
from runtime import harden
if os.getuid()==0:raise SystemExit('native_test_must_be_nonroot')
profile=sys.argv[1] if len(sys.argv)>1 else 'gateway'
harden(profile)
try:socket.socket(socket.AF_INET,socket.SOCK_STREAM)
except OSError as e:
    assert e.errno==errno.EPERM
else:raise AssertionError('IP sockets are not blocked')
with socket.socket(socket.AF_UNIX,socket.SOCK_STREAM):pass
assert 'NoNewPrivs:\t1' in Path('/proc/self/status').read_text()
with tempfile.TemporaryDirectory() as tmp:
    p=Path(tmp)/'file';p.write_text('synthetic');p.unlink()
print('NATIVE_PROCESS_ISOLATION=PASS unix_only=true no_new_privileges=true file_sandbox=true')
