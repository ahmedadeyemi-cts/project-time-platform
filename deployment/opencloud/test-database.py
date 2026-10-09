#!/usr/bin/env python3
"""Exercise minimum schema gate in a disposable database; no live migrations."""
import os
from pathlib import Path
import re
import secrets
import subprocess
import time

ROOT = Path(__file__).resolve().parent
name = 'opencloud-schema-ci-' + secrets.token_hex(6)
password = secrets.token_hex(32)
print('::add-mask::' + password, flush=True)
env = dict(os.environ, POSTGRES_PASSWORD=password)


def sql(user, text):
    return subprocess.run(['docker','exec','-i',name,'psql','-X','-U',user,'-d','postgres','-v','ON_ERROR_STOP=1'],
                          input=text, capture_output=True, text=True)

try:
    subprocess.run(['docker','run','--detach','--name',name,'--network','none','--env','POSTGRES_PASSWORD','postgres:16-bookworm'], env=env, check=True)
    for _ in range(60):
        ready = subprocess.run(['docker','exec',name,'pg_isready','-U','postgres'], capture_output=True)
        if ready.returncode == 0:
            break
        time.sleep(1)
    else:
        raise RuntimeError('Disposable PostgreSQL did not become ready')
    check = (ROOT/'pulse/schema-check.sql').read_text()
    if sql('postgres', 'CREATE ROLE ptp_app LOGIN;').returncode:
        raise RuntimeError('Synthetic role initialization failed')
    if sql('ptp_app', check).returncode == 0:
        raise RuntimeError('Empty schema incorrectly passed')
    relations = re.findall(r"'([a-z_]+)'", re.search(r'ARRAY\[(.*?)\]',check,re.S).group(1))
    fixture = '\n'.join('CREATE TABLE public.'+table+' (synthetic_id integer); GRANT SELECT ON public.'+table+' TO ptp_app;' for table in relations)
    if sql('postgres', fixture).returncode:
        raise RuntimeError('Synthetic fixture creation failed')
    result = sql('ptp_app', check)
    if result.returncode:
        raise RuntimeError('Qualified minimum synthetic schema failed: '+result.stderr)
    if sql('postgres', check).returncode == 0:
        raise RuntimeError('Superuser incorrectly passed as runtime identity')
    if sql('postgres', 'REVOKE SELECT ON public.projects FROM ptp_app;').returncode:
        raise RuntimeError('Synthetic privilege change failed')
    if sql('ptp_app', check).returncode == 0:
        raise RuntimeError('Missing runtime read grant incorrectly passed')
    print('MINIMUM_DATABASE_GUARD=PASS checks=4')
    print('FULL_BASELINE_AND_MIGRATION_PARITY=NOT_RUN')
finally:
    # Only the random disposable container created above is removed.
    subprocess.run(['docker','rm','--force','--volumes',name], capture_output=True)
