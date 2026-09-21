"""No model downloads. Real Unix transport; Flask HTTP tests use synthetic results."""
import copy
import importlib.util
import json
from pathlib import Path
import socket
import sys
import tempfile
import threading
import time
import unittest
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'deployment/oracle-celar/gateway'))
import laya_decisions as d

ANSWER = {
    'ok': True, 'model_revision': d.REVISION, 'question_schema': d.SCHEMA,
    'document_type': 'invoice', 'probabilities': {'sow': .1, 'invoice': .7, 'purchase_order': .1, 'other': .1},
    'raw_model_confidence': .7, 'review_required': True, 'automation_approved': False,
    'workflow_actions_performed': 0, 'production_accuracy_validated': False,
    'input_truncated': False, 'input_state_tokens': 40, 'latency_ms': 2300,
    'confidence_is_probability_of_correctness': False,
    'service_pid': 98765, 'private_path': '/never/return'
}
HEALTH = {'ok': True, 'model_revision': d.REVISION, 'status': 'ready',
          'model_load_count': 1, 'state_token_budget': 450, 'inference_busy': False}

class ContractTests(unittest.TestCase):
    def test_valid_answer_is_sanitized(self):
        result = d.normalized(ANSWER)
        self.assertEqual(result['document_type'], 'invoice')
        self.assertNotIn('service_pid', result)
        self.assertNotIn('private_path', result)
        self.assertFalse(result['external_fallback_allowed'])

    def test_safety_fields_fail_closed(self):
        for key, value in [('review_required', False), ('automation_approved', True),
                           ('input_truncated', True), ('workflow_actions_performed', 1),
                           ('workflow_actions_performed', False), ('model_revision', 'changed'),
                           ('question_schema', 'other'), ('document_type', 'approved'),
                           ('confidence_is_probability_of_correctness', True), ('ok', False)]:
            with self.subTest(key=key, value=value):
                with self.assertRaises(d.DecisionError): d.normalized({**ANSWER, key: value})

    def test_probability_validation(self):
        for bad in [float('nan'), float('inf'), -1, 2, True, '0.7', None]:
            with self.subTest(bad=bad):
                body = copy.deepcopy(ANSWER); body['probabilities']['invoice'] = bad
                with self.assertRaises(d.DecisionError): d.normalized(body)

    def test_schema_and_sum(self):
        for scores in [{}, {'sow': 1}, {x: .1 for x in d.LABELS}]:
            with self.assertRaises(d.DecisionError): d.normalized({**ANSWER, 'probabilities': scores})

    def test_budget_and_latency(self):
        for key, value in [('input_state_tokens', 451), ('input_state_tokens', True),
                           ('latency_ms', float('nan')), ('latency_ms', -1)]:
            with self.assertRaises(d.DecisionError): d.normalized({**ANSWER, key: value})

    def test_health(self):
        self.assertTrue(d.normalized(HEALTH, health=True)['runtime_connected'])
        for key, value in [('model_load_count', 2), ('model_load_count', True),
                           ('state_token_budget', 449), ('status', 'starting')]:
            with self.assertRaises(d.DecisionError): d.normalized({**HEALTH, key: value}, health=True)

    def test_request(self):
        self.assertEqual(d.request_payload(b'{"text":"INVOICE"}'), {'op': 'classify', 'text': 'INVOICE'})
        for value in [b'{}', b'[]', b'{"text":""}', b'{"text":1}', b'{"text":"one","text":"two"}',
                      b'{"text":"one","provider":"external"}', b'{"text":NaN}', b'\xff', b'x'*16385]:
            with self.assertRaises(d.DecisionError): d.request_payload(value)

    def test_text_byte_limit(self):
        with self.assertRaises(d.DecisionError):
            d.request_payload(json.dumps({'text': 'x'*8193}).encode())

class SocketTests(unittest.TestCase):
    def serve(self, response, delay=0):
        directory = tempfile.TemporaryDirectory(); self.addCleanup(directory.cleanup)
        path = str(Path(directory.name) / 'worker.sock')
        listener = socket.socket(socket.AF_UNIX, socket.SOCK_STREAM)
        listener.bind(path); listener.listen(1)
        def worker():
            try:
                conn, _ = listener.accept()
                with conn:
                    conn.settimeout(1); conn.recv(16384); time.sleep(delay)
                    try: conn.sendall(response)
                    except OSError: pass
            finally: listener.close()
        thread = threading.Thread(target=worker, daemon=True); thread.start()
        self.addCleanup(lambda: thread.join(timeout=1))
        return path

    def test_actual_unix_request(self):
        path = self.serve(json.dumps(ANSWER).encode()+b'\n')
        self.assertEqual(d.exchange({'op':'classify','text':'INVOICE'}, path=path)['document_type'],'invoice')

    def test_absent_worker(self):
        with self.assertRaises(d.DecisionError) as caught:
            d.exchange({'op':'health'}, path='/nonexistent-laya-test.sock')
        self.assertEqual(caught.exception.status, 503)

    def test_deadline(self):
        path = self.serve(b'{}\n', delay=.2)
        with self.assertRaises(d.DecisionError) as caught: d.exchange({'op':'health'}, path=path, seconds=.04)
        self.assertEqual(caught.exception.status,504)

    def test_invalid_framing_and_json(self):
        for raw in [b'{}', b'{}\n{}\n', b'{"ok":true,"ok":false}\n', b'[]\n', b'x'*16385+b'\n']:
            with self.subTest(raw=raw[:40]):
                path=self.serve(raw)
                with self.assertRaises(d.DecisionError): d.exchange({'op':'health'},path=path)

    def test_worker_error_allowlist(self):
        for error, code, status in [('busy','decision_busy',503),
                                    ('input_exceeds_model_budget','decision_input_exceeds_model_budget',422),
                                    ('secret request body','decision_worker_failed',502),
                                    ({'secret':1},'decision_worker_failed',502)]:
            path=self.serve(json.dumps({'ok':False,'error':error}).encode()+b'\n')
            with self.assertRaises(d.DecisionError) as caught: d.exchange({'op':'health'},path=path)
            self.assertEqual((caught.exception.code,caught.exception.status),(code,status))

@unittest.skipUnless(importlib.util.find_spec('flask'), 'Flask HTTP tests require the CI dependency')
class HttpTests(unittest.TestCase):
    def setUp(self):
        from flask import Flask, request
        self.app=Flask(__name__)
        @self.app.before_request
        def _authenticate():
            if request.headers.get('Authorization') != 'Bearer test-only': return {},401
            if request.headers.get('X-Pulse-AI-Privacy-Boundary') != 'private_pulse_runtime_only': return {},403
        d.register(self.app); self.client=self.app.test_client()
        self.headers={'Authorization':'Bearer test-only','X-Pulse-AI-Privacy-Boundary':'private_pulse_runtime_only'}

    def test_authentication_before_socket(self):
        with patch.object(d,'exchange') as worker:
            self.assertEqual(self.client.get('/v1/decisions/health').status_code,401)
            self.assertEqual(self.client.get('/v1/decisions/health',headers={'Authorization':'Bearer test-only'}).status_code,403)
            worker.assert_not_called()

    def test_registered_routes(self):
        with patch.object(d,'exchange',return_value=ANSWER):
            result=self.client.post('/v1/decisions/document-type',headers=self.headers,json={'text':'INVOICE'})
            self.assertEqual(result.status_code,200)
            self.assertEqual(result.json['document_type'],'invoice')
            self.assertEqual(result.headers['Cache-Control'],'no-store')

    def test_no_unauthenticated_registration(self):
        from flask import Flask
        with self.assertRaises(RuntimeError): d.register(Flask('unsafe'))

if __name__=='__main__': unittest.main()
