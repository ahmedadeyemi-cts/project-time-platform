#!/usr/bin/env python3
"""Installed Protected Test export acceptance. Never requests model generation."""
import asyncio
import importlib.util
import io
import json
import os
from pathlib import Path
import re
import uuid
import zipfile
from xml.etree import ElementTree as ET

spec = importlib.util.spec_from_file_location('sa', Path(__file__).with_name('run-module025-installed-sa-uat.py'))
sa = importlib.util.module_from_spec(spec)
spec.loader.exec_module(sa)
transport = sa.http
record_id = ''


def http(path, method='GET', payload=None, token=''):
    # Exact request allowlist applies to this verifier and all imported helpers.
    allowed = {('POST', '/api/auth/local/login'), ('POST', '/api/auth/session/logout'),
               ('GET', '/api/module025/sow-gsd/bootstrap'), ('POST', '/api/module025/sow-gsd')}
    if record_id:
        root = '/api/module025/sow-gsd/' + record_id
        allowed |= {('GET', root), ('PUT', root), ('POST', root + '/archive'),
                    ('GET', root + '/draft-sow.docx'), ('GET', root + '/draft-gsd.xlsx')}
    sa.require((method, path) in allowed, 'export_request_not_allowed')
    return transport(path, method, payload, token)


sa.http = http


def document(raw, kind):
    sa.require(isinstance(raw, bytes), 'export_body_not_binary')
    with zipfile.ZipFile(io.BytesIO(raw)) as archive:
        if kind == 'sow':
            xml = archive.read('word/document.xml').decode()
            sa.require('DRAFT' in xml and 'Synthetic reviewed plan task' in xml, 'draft_sow_content_missing')
            sa.require('INTERNAL_EXPORT_TEST_NOTE' not in xml, 'internal_note_in_customer_sow')
            sa.require(any(n.startswith('word/media/') for n in archive.namelist()), 'sow_logo_missing')
        else:
            ns = {'s': 'http://schemas.openxmlformats.org/spreadsheetml/2006/main'}
            workbook = ET.fromstring(archive.read('xl/workbook.xml'))
            names = [x.attrib['name'] for x in workbook.findall('s:sheets/s:sheet', ns)]
            sa.require(len(names) == 12 and all(x in names for x in ['Plan','Design','Implement','Validate','Release']), 'gsd_standard_sheets_missing')
            strings = archive.read('xl/sharedStrings.xml').decode()
            sa.require('DRAFT' in strings and 'Synthetic reviewed plan task' in strings, 'draft_gsd_content_missing')
            sa.require('INTERNAL_EXPORT_TEST_NOTE' in strings, 'gsd_review_note_missing')


async def main():
    global record_id
    evidence = Path(os.environ.get('EVIDENCE_DIR', '/tmp/module025-export-evidence'))
    evidence.mkdir(parents=True, exist_ok=True)
    report = dict(status='failed', acceptanceScope='sow_exports', generationPosts=0,
                  draftExportsVerified=False, productionMutation=False, mockedResponses=False,
                  sourceCommit=os.environ.get('TARGET_RELEASE_COMMIT',''), generationQualityVerified=False)
    token = ''
    cleaned = False
    try:
        sa.require(re.fullmatch('[0-9a-f]{40}', report['sourceCommit']) is not None, 'release_identity_missing')
        session = sa.login(os.environ.get('PROJECTPULSE_M025_SA_EMAIL',''), os.environ.get('PROJECTPULSE_M025_SA_PASSWORD',''))
        token = session['sessionToken']
        status, bootstrap, _ = http('/api/module025/sow-gsd/bootstrap', token=token)
        sa.require(status == 200 and isinstance(bootstrap,dict), 'bootstrap_failed')
        access = bootstrap.get('access',{})
        sa.require(access.get('isSolutionArchitect') is True and access.get('canCreate') is True
                   and access.get('canEditOwn') is True and access.get('isViewAs') is False
                   and access.get('protectedTestUatRoleFixture') is False, 'normal_sa_authority_required')
        ae = bootstrap.get('accountExecutives') or []
        inside = bootstrap.get('insideSalesRepresentatives') or []
        sa.require(ae and inside, 'people_directory_missing')
        suffix = os.environ.get('GITHUB_RUN_ID','manual') + '-' + os.environ.get('GITHUB_RUN_ATTEMPT','1')
        payload = dict(projectName='Synthetic export UAT '+suffix,customerId=None,
            customerName='Synthetic export customer '+suffix,customerEntryMode='manual',
            commercialModel='time_and_materials',customerProgram='standard',
            accountExecutiveUserId=ae[0]['userId'],resaleUserId=inside[0]['userId'],
            serviceOverview='Synthetic document export acceptance. No AI generation requested.')
        status, created, _ = http('/api/module025/sow-gsd','POST',payload,token)
        sa.require(status == 201 and isinstance(created,dict), 'create_failed')
        current = created['engagement']
        record_id = str(current['engagementId'])
        sa.require(re.fullmatch('[0-9a-fA-F-]{36}',record_id) is not None, 'record_identity_invalid')
        root = '/api/module025/sow-gsd/' + record_id
        status, detail, _ = http(root,token=token)
        sa.require(status == 200, 'initial_read_failed')
        current = detail['engagement']
        phases = [sa.phase_payload(p) for p in current['phases']]
        sa.require({p['phaseCode'] for p in phases} == set(sa.PHASE_CODES), 'five_phases_required')
        for phase in phases:
            phase['objective'] = 'Synthetic reviewed '+phase['phaseCode']+' scope'
            phase['finalHours'] = 99  # Server must replace the stale allowance with reviewed task sum.
            phase['tasks'] = [dict(taskId=str(uuid.uuid4()),description='Synthetic reviewed '+phase['phaseCode']+' task',hours=.1,notes='INTERNAL_EXPORT_TEST_NOTE'),
                              dict(taskId=str(uuid.uuid4()),description='Synthetic second task',hours=.2,notes='')]
        payload.update(expectedRevision=current['revision'],phases=phases)
        status, _, _ = http(root,'PUT',payload,token)
        sa.require(status == 200, 'task_save_failed')
        status, detail, _ = http(root,token=token)
        sa.require(status == 200, 'task_readback_failed')
        current = detail['engagement']
        sa.require(not current.get('lastGeneratedAt') and current.get('status') != 'confirmed', 'unexpected_generation_or_confirmation')
        for phase in current['phases']:
            expected = next(p for p in phases if p['phaseCode'] == phase['phaseCode'])
            sa.require(phase.get('tasks') == expected['tasks'] and phase['finalHours'] == .3, 'task_reconciliation_readback_failed')
        await sa.browser_lifecycle(session,current['engagementNumber'],'',report,evidence,preflight=True)
        for kind, path in [('sow',root+'/draft-sow.docx'),('gsd',root+'/draft-gsd.xlsx')]:
            status, _, _ = http(path)
            sa.require(status in (401,403), 'anonymous_export_not_blocked')
            status, raw, headers = http(path,token=token)
            sa.require(status == 200 and 'no-store' in headers.get('cache-control',''), 'authenticated_draft_download_failed')
            document(raw,kind)
        report['draftExportsVerified'] = True
        report['taskSaveReadbackVerified'] = True
        status, _, _ = http(root+'/archive','POST',token=token)
        sa.require(status == 200, 'synthetic_cleanup_failed')
        cleaned = True
        report['status'] = 'passed'
    except sa.AcceptanceError as error:
        report['diagnosticCode'] = str(error)
    except Exception as error:
        report['diagnosticCode'] = 'unexpected_'+type(error).__name__
    finally:
        if record_id and token and not cleaned:
            try:
                status, _, _ = http('/api/module025/sow-gsd/'+record_id+'/archive','POST',token=token)
                cleaned = status == 200
            except Exception:
                pass
        report['syntheticRecordArchived'] = cleaned
        if token:
            try:
                http('/api/auth/session/logout','POST',{},token)
            except Exception:
                pass
        (evidence/'module025-export-uat.json').write_text(json.dumps(report,indent=2)+'\n')
    print('MODULE025_EXPORT_UAT='+report['status']+' diagnostic='+report.get('diagnosticCode','none'))
    return 0 if report['status'] == 'passed' else 1


if __name__ == '__main__':
    raise SystemExit(asyncio.run(main()))
