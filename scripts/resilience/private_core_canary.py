"""Run the private revision canary through Azure's terminal channel; no ingress changes."""
import base64
import json
import os
import pty
import re
import select
import subprocess
import time


def check_revision(revision, container, source):
    assert re.fullmatch(r'ca-phd-test-api-westus3--c[cp]-[0-9]{1,20}-[0-9]{1,3}',revision)
    assert re.fullmatch(r'[a-zA-Z0-9_-]{1,80}',container)
    assert re.fullmatch(r'[a-f0-9]{40}',source)
    password=os.environ['TEST_LOGIN_PASSWORD']
    if len(password)<12:raise RuntimeError('test_credential_missing')
    master,slave=pty.openpty()
    command=['az','containerapp','exec','--subscription','cd32baeb-7b71-4bc0-8ea3-9f23a50903fe',
             '-g','rg-project-health-dashboard-test-app-westus3','-n','ca-phd-test-api-westus3',
             '--revision',revision,'--container',container,
             '--command',"/bin/sh -c stty${IFS}-echo;dotnet${IFS}/app/core-canary/Pulse.CoreCanary.dll"]
    process=subprocess.Popen(command,stdin=slave,stdout=slave,stderr=slave,close_fds=True)
    os.close(slave)
    buffer=b'';sent=False;deadline=time.monotonic()+360
    try:
        while time.monotonic()<deadline:
            ready,_,_=select.select([master],[],[],1)
            if ready:
                try:chunk=os.read(master,8192)
                except OSError:chunk=b''
                if not chunk:break
                buffer+=chunk
                if len(buffer)>128*1024:raise RuntimeError('private_canary_output_bound')
                if not sent and b'PULSE_CANARY_READY' in buffer:
                    # Remote terminal echo is disabled before readiness. Never use CLI/env args.
                    os.write(master,(json.dumps({'password':password,'sourceCommit':source})+'\n').encode())
                    sent=True
                match=re.search(rb'PULSE_CANARY_RESULT=([A-Za-z0-9+/=]+)\r?\n',buffer)
                if match:
                    result=json.loads(base64.b64decode(match[1],validate=True))
                    if result.get('result')!='PASS':raise RuntimeError('private_canary_'+result.get('failureReason','failed'))
                    if result.get('sourceCommit')!=source:raise RuntimeError('private_canary_source_identity')
                    return result
            if process.poll() is not None:break
        raise RuntimeError('private_canary_terminal_failed')
    finally:
        if process.poll() is None:process.terminate()
        try:process.wait(timeout=10)
        except subprocess.TimeoutExpired:process.kill();process.wait()
        os.close(master)
        # Raw terminal data can contain credential echo on an unexpected server; never publish.
