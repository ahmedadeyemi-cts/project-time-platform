"""Run as nonroot INSIDE the candidate Linux image, never on the operator host."""
import errno,os,socket,sys,tempfile,shutil,subprocess,stat
from pathlib import Path
sys.path.insert(0,'/opt/pulse-services')
from runtime import harden, IDENTITY_VARIABLES
if os.getuid()==0:raise SystemExit('native_test_must_be_nonroot')
profile=sys.argv[1] if len(sys.argv)>1 else 'gateway'
# Harmless injected metadata reproduces Azure's startup environment. No real
# identity endpoint is contacted and no identity token is requested or printed.
for key in IDENTITY_VARIABLES:os.environ[key]='synthetic-isolation-test'
harden(profile)
assert not any(key in os.environ for key in IDENTITY_VARIABLES)
for family in (socket.AF_INET,socket.AF_INET6):
    try:socket.socket(family,socket.SOCK_STREAM)
    except OSError as e:
        assert e.errno==errno.EPERM
    else:raise AssertionError('IP sockets are not blocked')
with socket.socket(socket.AF_UNIX,socket.SOCK_STREAM):pass
assert 'NoNewPrivs:\t1' in Path('/proc/self/status').read_text()
with tempfile.TemporaryDirectory() as tmp:
    p=Path(tmp)/'file';p.write_text('synthetic');p.unlink()
    # A downloaded/modified executable in writable storage must not run,
    # even when its ordinary Unix file permissions contain execute bits.
    program=Path(tmp)/'must-not-execute'
    shutil.copyfile('/usr/bin/true',program);program.chmod(0o755)
    try:subprocess.run([str(program)],check=True,timeout=3)
    except PermissionError:pass
    else:raise AssertionError('Execution from writable storage was allowed')
    device=Path(tmp)/'must-not-be-device'
    try:os.mknod(device,stat.S_IFCHR|0o600,os.makedev(1,3))
    except PermissionError:pass
    else:raise AssertionError('Device node creation was allowed')
print('NATIVE_PROCESS_ISOLATION=PASS unix_only=true no_new_privileges=true file_sandbox=true writable_storage_execution=false device_creation=false identity_metadata_removed=true')
