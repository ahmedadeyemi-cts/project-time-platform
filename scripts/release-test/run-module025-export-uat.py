#!/usr/bin/env python3
"""Installed Protected Test export acceptance. Never requests model generation."""
import asyncio
import importlib.util
import io
import json
import os
import posixpath
from decimal import Decimal
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

# Explicit additive wire defaults. Expand the expected legacy input only; never
# strip fields from actual readback, which would hide changed estimates or flags.
TASK_DEFAULTS = dict(regularHours=None, afterHours=None, afterHoursRequired=False,
                     afterHoursSuggested=False, afterHoursReason=None, reviewed=None,
                     estimateBasis=None)


def fixture_phases(current):
    phases = [sa.phase_payload(phase) for phase in current]
    sa.require(len(phases) == 5 and [p['phaseCode'] for p in phases] == list(sa.PHASE_CODES), 'five_phases_required')
    for phase in phases:
        code = phase['phaseCode']
        phase.update(objective='Synthetic reviewed '+code+' scope',
                     detailedActivities=['Synthetic '+code+' execution approach'],
                     technicalTasks=['INTERNAL_EXPORT_TECHNICAL_TASK '+code],
                     deliverables=['Synthetic '+code+' deliverable'],
                     acceptanceCriteria=['Synthetic '+code+' acceptance'],
                     finalHours=99)  # Server must reconcile the stale phase allowance.
        phase['tasks'] = [dict(taskId=str(uuid.uuid4()), description='Synthetic reviewed '+code+' task',
                              hours=.1, notes='INTERNAL_EXPORT_TEST_NOTE', regularHours=.05, afterHours=.05,
                              afterHoursRequired=True, afterHoursSuggested=False,
                              afterHoursReason='Synthetic after-hours designation for export acceptance.',
                              reviewed=True, estimateBasis='Synthetic reviewed labor allocation.'),
                          dict(taskId=str(uuid.uuid4()), description='Synthetic second '+code+' task', hours=.2, notes='')]
    return phases


def task_readback(actual, expected):
    sa.require(isinstance(actual, list) and len(actual) == 5
               and all(isinstance(p, dict) for p in actual)
               and [p.get('phaseCode') for p in actual] == list(sa.PHASE_CODES), 'task_readback_phase_coverage_failed')
    for phase, saved in zip(actual, expected):
        tasks = [dict(TASK_DEFAULTS, **task) for task in saved['tasks']]
        sa.require(phase.get('phaseCode') == saved['phaseCode'] and phase.get('tasks') == tasks
                   and phase.get('finalHours') == .3, 'task_reconciliation_readback_failed')
        for task in phase['tasks']:
            sa.require(type(task['afterHoursRequired']) is bool and type(task['afterHoursSuggested']) is bool
                       and (task['reviewed'] is None or type(task['reviewed']) is bool), 'task_review_flag_type_invalid')



def http(path, method='GET', payload=None, token=''):
    # Exact request allowlist applies to this verifier and all imported helpers.
    allowed = {('POST', '/api/auth/local/login'), ('POST', '/api/auth/session/logout'),
               ('GET', '/api/module025/sow-gsd/bootstrap'), ('POST', '/api/module025/sow-gsd'),
               ('GET', '/api/module025/sow-gsd/templates')}
    if record_id:
        root = '/api/module025/sow-gsd/' + record_id
        allowed |= {('GET', root), ('PUT', root), ('POST', root + '/archive'),
                    ('GET', root + '/draft-sow.docx'), ('GET', root + '/draft-gsd.xlsx'),
                    ('GET', root + '/work-tracking'), ('GET', root + '/transfer-options'),
                    ('GET', root + '/handoff-notifications')}
    sa.require((method, path) in allowed, 'export_request_not_allowed')
    return transport(path, method, payload, token)


sa.http = http


def document(raw, kind):
    sa.require(isinstance(raw, bytes), 'export_body_not_binary')
    with zipfile.ZipFile(io.BytesIO(raw)) as archive:
        if kind == 'sow':
            root = ET.fromstring(archive.read('word/document.xml'))
            ns = {'w': 'http://schemas.openxmlformats.org/wordprocessingml/2006/main'}
            # Word may split a sentence across formatting runs within a paragraph.
            text = '\n'.join(''.join(run.text or '' for run in node.findall('.//w:t', ns))
                             for node in root.findall('.//w:p', ns))
            sa.require('DRAFT' in text and 'Execution Approach' in text, 'draft_sow_content_missing')
            for code in sa.PHASE_CODES:
                sa.require(all(value in text for value in ('Synthetic reviewed '+code+' scope',
                           'Synthetic '+code+' execution approach', 'Synthetic '+code+' deliverable',
                           'Synthetic '+code+' acceptance')), 'draft_sow_phase_scope_missing')
                sa.require('Synthetic reviewed '+code+' task' not in text
                           and 'Synthetic second '+code+' task' not in text, 'detailed_task_in_customer_sow')
            sa.require('INTERNAL_EXPORT_TEST_NOTE' not in text
                       and 'INTERNAL_EXPORT_TECHNICAL_TASK' not in text, 'internal_note_in_customer_sow')
            sa.require(any(n.startswith('word/media/') for n in archive.namelist()), 'sow_logo_missing')
        else:
            ns = {'s': 'http://schemas.openxmlformats.org/spreadsheetml/2006/main'}
            workbook = ET.fromstring(archive.read('xl/workbook.xml'))
            sheets = workbook.findall('s:sheets/s:sheet', ns)
            names = [sheet.attrib['name'] for sheet in sheets]
            sa.require(names == ['Summary', 'Phase Breakdown', 'Totals Sheet', 'SELL SKUs', 'Plan', 'Design',
                                 'Implement', 'Validate', 'Release', 'Architect Notes', 'Gotcha Items',
                                 'Assumptions Responsibilities'], 'gsd_standard_sheets_missing')
            strings = ''.join(ET.fromstring(archive.read('xl/sharedStrings.xml')).itertext())
            sa.require('DRAFT' in strings, 'draft_gsd_content_missing')
            for code in sa.PHASE_CODES:
                sa.require('Synthetic reviewed '+code+' task' in strings
                           and 'Synthetic second '+code+' task' in strings
                           and 'INTERNAL_EXPORT_TECHNICAL_TASK '+code in strings, 'draft_gsd_task_detail_missing')
            sa.require('INTERNAL_EXPORT_TEST_NOTE' in strings, 'gsd_review_note_missing')
            rels = {item.attrib['Id']: item.attrib['Target'] for item in ET.fromstring(archive.read('xl/_rels/workbook.xml.rels'))}
            for sheet in sheets:
                name = sheet.attrib['name']
                expected = {'F4': '1.5'} if name == 'Summary' else (
                    {'B4': '.05', 'C4': '.05', 'B5': '.2', 'C5': '0', 'B100': '.3', 'C100': '.05'}
                    if name.lower() in sa.PHASE_CODES else {})
                if not expected:
                    continue
                target = rels[sheet.attrib['{http://schemas.openxmlformats.org/officeDocument/2006/relationships}id']]
                part = target.lstrip('/') if target.startswith('/') else posixpath.normpath('xl/'+target)
                xml = ET.fromstring(archive.read(part))
                for address, value in expected.items():
                    cell = xml.find(f'.//s:c[@r="{address}"]', ns)
                    cached = cell.find('s:v', ns) if cell is not None else None
                    sa.require(cached is not None and cached.text is not None, 'gsd_task_total_reconciliation_failed')
                    actual, expected = Decimal(cached.text), Decimal(value)
                    if address in ('B100', 'C100', 'F4'):
                        # Excel formula caches use binary floating point. Permit only
                        # sub-nanohour arithmetic residue; input allocations stay exact.
                        formula = cell.find('s:f', ns)
                        sa.require(formula is not None and bool(formula.text)
                                   and abs(actual-expected) <= Decimal('0.000000001'), 'gsd_task_total_reconciliation_failed')
                    else:
                        sa.require(actual == expected, 'gsd_task_total_reconciliation_failed')


def workspace_readiness(root, token):
    for path, field in [(root+'/work-tracking', 'schemaReady'), (root+'/transfer-options', 'coverageReady'),
                        (root+'/handoff-notifications', 'configured'), ('/api/module025/sow-gsd/templates', 'schemaReady')]:
        status, body, _ = http(path, token=token)
        sa.require(status == 200 and isinstance(body, dict) and body.get(field) is True,
                   'workspace_readiness_'+field+'_failed')
        if path.endswith('/templates'):
            sa.require(body.get('canActivate') is False, 'normal_sa_template_activation_not_blocked')


async def main():
    global record_id
    evidence = Path(os.environ.get('EVIDENCE_DIR', '/tmp/module025-export-evidence'))
    evidence.mkdir(parents=True, exist_ok=True)
    report = dict(status='failed', acceptanceScope='sow_exports', generationPosts=0,
                  draftExportsVerified=False, workspaceSchemaReadinessVerified=False, productionMutation=False, mockedResponses=False,
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
        workspace_readiness(root, token)
        report['workspaceSchemaReadinessVerified'] = True
        phases = fixture_phases(current['phases'])
        payload.update(expectedRevision=current['revision'],phases=phases)
        status, _, _ = http(root,'PUT',payload,token)
        sa.require(status == 200, 'task_save_failed')
        status, detail, _ = http(root,token=token)
        sa.require(status == 200, 'task_readback_failed')
        current = detail['engagement']
        sa.require(not current.get('lastGeneratedAt') and current.get('status') != 'confirmed', 'unexpected_generation_or_confirmation')
        task_readback(current['phases'], phases)
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
