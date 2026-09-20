"""Offline contract regressions for the installed export verifier, never live UAT.

Synthetic JSON/OOXML fixtures exercise strict acceptance assertions. No login,
network request, model generation, notification, or live evidence report occurs.
"""
import copy
import importlib.util
import io
import json
import os
from pathlib import Path
import unittest
import zipfile
from xml.etree import ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location('export_contract', ROOT/'scripts/release-test/run-module025-export-uat.py')
exports = importlib.util.module_from_spec(spec)
spec.loader.exec_module(exports)
W = 'http://schemas.openxmlformats.org/wordprocessingml/2006/main'
S = 'http://schemas.openxmlformats.org/spreadsheetml/2006/main'
R = 'http://schemas.openxmlformats.org/officeDocument/2006/relationships'
P = 'http://schemas.openxmlformats.org/package/2006/relationships'


def package(parts):
    stream = io.BytesIO()
    with zipfile.ZipFile(stream, 'w') as archive:
        for name, data in parts.items(): archive.writestr(name, data)
    return stream.getvalue()


def sow_fixture(missing=None, extra=None, logo=True):
    root = ET.Element(f'{{{W}}}document')
    body = ET.SubElement(root, f'{{{W}}}body')
    values = ['DRAFT', 'Execution Approach']
    for code in exports.sa.PHASE_CODES:
        values += ['Synthetic reviewed '+code+' scope', 'Synthetic '+code+' execution approach',
                   'Synthetic '+code+' deliverable', 'Synthetic '+code+' acceptance']
    if extra: values.append(extra)
    for value in values:
        if value == missing: continue
        para = ET.SubElement(body, f'{{{W}}}p')
        # Real Word content can split a phrase across formatting runs.
        for piece in (value[:10], value[10:]):
            ET.SubElement(ET.SubElement(para, f'{{{W}}}r'), f'{{{W}}}t').text = piece
    parts = {'word/document.xml': ET.tostring(root)}
    if logo: parts['word/media/logo.png'] = b'synthetic-logo-fixture'
    return package(parts)


def gsd_fixture(missing=None, wrong_cell=None, overrides=None, without_formulas=False):
    names = ['Summary', 'Phase Breakdown', 'Totals Sheet', 'SELL SKUs', 'Plan', 'Design', 'Implement', 'Validate', 'Release', 'Architect Notes', 'Gotcha Items', 'Assumptions Responsibilities']
    workbook = ET.Element(f'{{{S}}}workbook')
    sheets = ET.SubElement(workbook, f'{{{S}}}sheets')
    rels = ET.Element(f'{{{P}}}Relationships')
    parts = {}
    for index, name in enumerate(names, 1):
        ET.SubElement(sheets, f'{{{S}}}sheet', name=name, sheetId=str(index), attrib={f'{{{R}}}id':f'rId{index}'})
        ET.SubElement(rels, f'{{{P}}}Relationship', Id=f'rId{index}', Target=f'worksheets/sheet{index}.xml')
        sheet = ET.Element(f'{{{S}}}worksheet'); data = ET.SubElement(sheet, f'{{{S}}}sheetData')
        values = {'F4':'1.5'} if name == 'Summary' else ({'B4':'.05','C4':'.05','B5':'.2','C5':'0','B100':'.3','C100':'.05'} if name.lower() in exports.sa.PHASE_CODES else {})
        for address, value in values.items():
            cell = ET.SubElement(ET.SubElement(data, f'{{{S}}}row'), f'{{{S}}}c', r=address)
            if address in ('B100','C100','F4') and not without_formulas: ET.SubElement(cell, f'{{{S}}}f').text = 'SUM(B4:C5)'
            ET.SubElement(cell, f'{{{S}}}v').text = '99' if wrong_cell == (name, address) else (overrides or {}).get((name,address),value)
        parts[f'xl/worksheets/sheet{index}.xml'] = ET.tostring(sheet)
    strings = ['DRAFT', 'INTERNAL_EXPORT_TEST_NOTE']
    for code in exports.sa.PHASE_CODES:
        strings += ['Synthetic reviewed '+code+' task', 'Synthetic second '+code+' task', 'INTERNAL_EXPORT_TECHNICAL_TASK '+code]
    sst = ET.Element(f'{{{S}}}sst')
    for value in strings:
        if value != missing: ET.SubElement(ET.SubElement(sst, f'{{{S}}}si'), f'{{{S}}}t').text = value
    parts.update({'xl/workbook.xml':ET.tostring(workbook), 'xl/_rels/workbook.xml.rels':ET.tostring(rels), 'xl/sharedStrings.xml':ET.tostring(sst)})
    return package(parts)


class ExportContractTests(unittest.TestCase):
    def setUp(self):
        self.expected = exports.fixture_phases([{'phaseCode':code} for code in exports.sa.PHASE_CODES])
        self.actual = copy.deepcopy(self.expected)
        for phase in self.actual:
            phase['finalHours'] = .3
            phase['tasks'] = [dict(exports.TASK_DEFAULTS, **task) for task in phase['tasks']]

    def test_explicit_split_and_legacy_default_readback_is_exact(self):
        self.assertNotEqual(self.actual[0]['tasks'], self.expected[0]['tasks'], 'Reproduces pre-fix dictionary inequality')
        exports.task_readback(self.actual, self.expected)
        self.assertEqual(self.actual[0]['tasks'][0]['regularHours'], .05)
        self.assertEqual(self.actual[0]['tasks'][0]['afterHours'], .05)
        self.assertIsNone(self.actual[0]['tasks'][1]['reviewed'])

    def test_core_and_added_fields_cannot_change_or_disappear(self):
        mutations = {'taskId':'different', 'description':'Changed task', 'hours':.15, 'notes':'Changed note',
                     'regularHours':.1, 'afterHours':0, 'afterHoursRequired':False, 'afterHoursSuggested':True,
                     'afterHoursReason':'Changed reason', 'reviewed':False, 'estimateBasis':'Changed basis'}
        for field, value in mutations.items():
            with self.subTest(field=field):
                actual = copy.deepcopy(self.actual); actual[0]['tasks'][0][field] = value
                with self.assertRaises(exports.sa.AcceptanceError): exports.task_readback(actual, self.expected)
        for field in exports.TASK_DEFAULTS:
            with self.subTest(missing_legacy_default=field):
                actual = copy.deepcopy(self.actual); del actual[0]['tasks'][1][field]
                with self.assertRaises(exports.sa.AcceptanceError): exports.task_readback(actual, self.expected)
        actual = copy.deepcopy(self.actual); actual[0]['tasks'][0]['unexpected'] = 'must not be stripped'
        with self.assertRaises(exports.sa.AcceptanceError): exports.task_readback(actual, self.expected)

    def test_phase_coverage_task_order_and_total_are_strict(self):
        variants = [self.actual[:-1], list(reversed(self.actual)), [self.actual[0]]*5]
        reordered = copy.deepcopy(self.actual); reordered[0]['tasks'].reverse(); variants.append(reordered)
        stale = copy.deepcopy(self.actual); stale[0]['finalHours'] = 99; variants.append(stale)
        double = copy.deepcopy(self.actual); double[0]['finalHours'] = .35; variants.append(double)
        for actual in variants:
            with self.assertRaises(exports.sa.AcceptanceError): exports.task_readback(actual, self.expected)

    def test_customer_sow_requires_all_scope_and_excludes_task_detail(self):
        exports.document(sow_fixture(), 'sow')
        for code in exports.sa.PHASE_CODES:
            for marker in ['Synthetic reviewed '+code+' scope', 'Synthetic '+code+' execution approach', 'Synthetic '+code+' deliverable', 'Synthetic '+code+' acceptance']:
                with self.subTest(missing=marker), self.assertRaises(exports.sa.AcceptanceError): exports.document(sow_fixture(missing=marker), 'sow')
        for marker in ['INTERNAL_EXPORT_TEST_NOTE', 'INTERNAL_EXPORT_TECHNICAL_TASK plan', 'Synthetic reviewed plan task', 'Synthetic second release task']:
            with self.subTest(leaked=marker), self.assertRaises(exports.sa.AcceptanceError): exports.document(sow_fixture(extra=marker), 'sow')
        with self.assertRaises(exports.sa.AcceptanceError): exports.document(sow_fixture(logo=False), 'sow')

    def test_gsd_requires_detail_notes_and_exact_split_rollup(self):
        exports.document(gsd_fixture(), 'gsd')
        for marker in ['Synthetic reviewed plan task','Synthetic second release task','INTERNAL_EXPORT_TECHNICAL_TASK design','INTERNAL_EXPORT_TEST_NOTE']:
            with self.subTest(missing=marker), self.assertRaises(exports.sa.AcceptanceError): exports.document(gsd_fixture(missing=marker), 'gsd')
        for sheet, address in [('Summary','F4'),('Plan','B4'),('Plan','C4'),('Design','B5'),('Implement','C5'),('Validate','B100'),('Release','C100')]:
            with self.subTest(cell=(sheet,address)), self.assertRaises(exports.sa.AcceptanceError): exports.document(gsd_fixture(wrong_cell=(sheet,address)), 'gsd')

    def test_only_formula_cache_roundoff_is_tolerated(self):
        exports.document(gsd_fixture(overrides={('Plan','B100'):'0.30000000000000004',('Summary','F4'):'1.5000000000000002'}), 'gsd')
        for overrides in [{('Plan','B100'):'0.30001'}, {('Plan','B4'):'0.05000000000000001'}]:
            with self.assertRaises(exports.sa.AcceptanceError): exports.document(gsd_fixture(overrides=overrides), 'gsd')
        with self.assertRaises(exports.sa.AcceptanceError): exports.document(gsd_fixture(without_formulas=True), 'gsd')

    def test_real_exporter_and_typed_wire_output(self):
        directory = os.environ.get('MODULE025_REAL_EXPORT_DIR')
        if not directory:
            self.skipTest('Real .NET exporter output is mandatory in CI via MODULE025_REAL_EXPORT_DIR; unavailable in this local Python-only run.')
        output = Path(directory)
        exports.document((output/'Acceptance-Contract-SOW.docx').read_bytes(), 'sow')
        exports.document((output/'Acceptance-Contract-GSD.xlsx').read_bytes(), 'gsd')
        actual = json.loads((output/'Acceptance-Contract-Tasks.json').read_text())
        expected = copy.deepcopy(actual)
        for phase in expected:
            phase['finalHours'] = 99
            # The second task deliberately uses the legacy input shape. The actual
            # record serialized by .NET must retain every new optional default.
            phase['tasks'][1] = {key: phase['tasks'][1][key] for key in ('taskId','description','hours','notes')}
        exports.task_readback(actual, expected)

    def test_workspace_schema_checks_are_read_only_and_fail_closed(self):
        exports.record_id = '00000000-0000-4000-8000-000000000001'
        root = '/api/module025/sow-gsd/'+exports.record_id
        calls = []
        def response(path, method, payload, token):
            calls.append((method,path))
            return 200, {'schemaReady':True,'coverageReady':True,'configured':True,'canActivate':False}, {}
        exports.transport = response
        exports.workspace_readiness(root, 'synthetic-offline-token')
        self.assertEqual(len(calls),4); self.assertTrue(all(method=='GET' for method,_ in calls))
        for field in ['schemaReady','coverageReady','configured']:
            exports.transport = lambda *args, field=field: (200, {'schemaReady':True,'coverageReady':True,'configured':True,'canActivate':False,field:False}, {})
            with self.assertRaises(exports.sa.AcceptanceError): exports.workspace_readiness(root,'synthetic-offline-token')
        exports.transport = lambda *args: (200, {'schemaReady':True,'coverageReady':True,'configured':True,'canActivate':True}, {})
        with self.assertRaises(exports.sa.AcceptanceError): exports.workspace_readiness(root,'synthetic-offline-token')
        exports.transport = response
        for suffix in ['/generate','/confirm','/transfer','/handoff/return','/handoff/acknowledge','/sell','/work-tracking']:
            with self.subTest(blocked=suffix), self.assertRaises(exports.sa.AcceptanceError): exports.http(root+suffix,'POST')
        with self.assertRaises(exports.sa.AcceptanceError): exports.http('/api/module025/sow-gsd/unrelated/work-tracking')


if __name__ == '__main__': unittest.main()
