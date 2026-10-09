"""Run the private revision canary through Azure's terminal channel; no ingress changes."""
import base64
import json
import os
import pty
import re
import select
import subprocess
import time


SUB='cd32baeb-7b71-4bc0-8ea3-9f23a50903fe'
RG='rg-project-health-dashboard-test-app-westus3'
APP='ca-phd-test-api-westus3'


def ready_replica(revision, container):
    raw=subprocess.check_output(['az','containerapp','replica','list','--subscription',SUB,
        '-g',RG,'-n',APP,'--revision',revision,'--only-show-errors','-o','json'],stderr=subprocess.PIPE)
    replicas=json.loads(raw)
    ready=[]
    for replica in replicas:
        name=replica.get('name','')
        if not re.fullmatch(re.escape(revision)+r'-[a-z0-9-]{1,100}',name):
            raise RuntimeError('private_canary_replica_identity')
        properties=replica.get('properties',{})
        containers=properties.get('containers',[])
        if properties.get('runningState')=='Running' and any(c.get('name')==container
                and c.get('runningState')=='Running' and c.get('ready') is True
                and c.get('started') is True for c in containers):
            ready.append(name)
    return sorted(ready)[0] if ready else None


def check_revision(revision, container, source):
    assert re.fullmatch(r'ca-phd-test-api-westus3--c[cp]-[0-9]{1,20}-[0-9]{1,3}',revision)
    assert re.fullmatch(r'[a-zA-Z0-9_-]{1,80}',container)
    assert re.fullmatch(r'[a-f0-9]{40}',source)
    deadline=time.monotonic()+360
    attempts=0
    while time.monotonic()<deadline:
        replica=ready_replica(revision,container)
        if replica is None:
            time.sleep(5)
            continue
        attempts+=1
        try:
            return terminal_check(revision,container,source,replica,deadline)
        except RuntimeError as failure:
            # A fresh revision can advertise Healthy before exec can attach. Retry only
            # before the handshake delivers a credential; application failures stay fatal.
            if str(failure)!='private_canary_attach_not_ready' or attempts>=3:
                raise
            time.sleep(5)
    raise RuntimeError('private_canary_running_replica_timeout')


def terminal_check(revision, container, source, replica, deadline):
    assert re.fullmatch(r'ca-phd-test-api-westus3--c[cp]-[0-9]{1,20}-[0-9]{1,3}',revision)
    assert re.fullmatch(r'[a-zA-Z0-9_-]{1,80}',container)
    assert re.fullmatch(r'[a-f0-9]{40}',source)
    password=os.environ['TEST_LOGIN_PASSWORD']
    if len(password)<12:raise RuntimeError('test_credential_missing')
    master,slave=pty.openpty()
    command=['az','containerapp','exec','--subscription','cd32baeb-7b71-4bc0-8ea3-9f23a50903fe',
             '-g','rg-project-health-dashboard-test-app-westus3','-n','ca-phd-test-api-westus3',
             '--revision',revision,'--replica',replica,'--container',container,
             '--command',"/bin/sh -c stty${IFS}-echo;dotnet${IFS}/app/core-canary/Pulse.CoreCanary.dll"]
    process=subprocess.Popen(command,stdin=slave,stdout=slave,stderr=slave,close_fds=True)
    os.close(slave)
    buffer=b'';sent=False
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
                match=re.search(rb'PULSE_CANARY_RESULT=([A-Za-z0-9+/=]+)\r*\n',buffer)
                if match:
                    result=json.loads(base64.b64decode(match[1],validate=True))
                    if result.get('result')!='PASS':raise RuntimeError('private_canary_'+result.get('failureReason','failed'))
                    if result.get('sourceCommit')!=source:raise RuntimeError('private_canary_source_identity')
                    expected=['/health','/health/live','/health/ready','/health/source','anonymous_session_denied','/api/security/context','/api/assignments/available-tasks?weekStart=2026-08-16','/api/timesheet/work-queue?weekStart=2026-08-16','/api/engineer-task-closeout/overview','/api/project-workspace/overview']
                    if result.get('checks')!=expected:raise RuntimeError('private_canary_checks_incomplete')
                    return {'result':'PASS','checks':expected,'sourceCommit':source,'celarSowAcceptance':'PENDING_NOT_EXECUTED'}
            if process.poll() is not None:break
        raise RuntimeError('private_canary_terminal_failed' if sent else 'private_canary_attach_not_ready')
    finally:
        if process.poll() is None:process.terminate()
        try:process.wait(timeout=10)
        except subprocess.TimeoutExpired:process.kill();process.wait()
        os.close(master)
        # Raw terminal data can contain credential echo on an unexpected server; never publish.
