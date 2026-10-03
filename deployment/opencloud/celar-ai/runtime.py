#!/usr/bin/env python3
"""Container entrypoint for the UNCHANGED Oracle gateway source. No model pull."""
from __future__ import annotations
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import urllib.request

MANIFEST = Path('/opt/opencloud/source-runtime.json')
FIELDS = {
    'gatewayVersion': 'CELAR_GATEWAY_VERSION',
    'generationModel': 'CELAR_GENERATION_MODEL',
    'reasoningModel': 'CELAR_REASONING_MODEL',
    'fastGeneralModel': 'CELAR_FAST_GENERAL_MODEL',
    'embeddingModel': 'CELAR_EMBEDDING_MODEL',
    'embeddingDimension': 'CELAR_EMBEDDING_DIMENSION',
    'ocrModel': 'CELAR_OCR_MODEL',
    'localGenerationModels': 'CELAR_LOCAL_GENERATION_MODELS',
    'structuredGenerationOrder': 'CELAR_STRUCTURED_GENERATION_ORDER',
    'structuredModelAttemptSeconds': 'CELAR_STRUCTURED_MODEL_ATTEMPT_SECONDS',
    'generalGenerationOrder': 'CELAR_GENERAL_GENERATION_ORDER',
    'generalModelAttemptSeconds': 'CELAR_GENERAL_MODEL_ATTEMPT_SECONDS',
    'sowModelAttemptSeconds': 'CELAR_SOW_MODEL_ATTEMPT_SECONDS',
    'sowTimeoutSeconds': 'CELAR_SOW_TIMEOUT_SECONDS',
    'sowContextTokens': 'CELAR_SOW_CONTEXT_TOKENS',
    'maxUploadBytes': 'CELAR_MAX_UPLOAD_BYTES',
    'maxJsonRequestBytes': 'CELAR_MAX_JSON_REQUEST_BYTES',
    'maxGatewayResponseBytes': 'CELAR_MAX_GATEWAY_RESPONSE_BYTES',
    'maxOcrPages': 'CELAR_MAX_OCR_PAGES',
    'maxOcrImagePixels': 'CELAR_MAX_OCR_IMAGE_PIXELS',
    'maxOcrImageEdge': 'CELAR_MAX_OCR_IMAGE_EDGE',
    'pdfRasterMaxEdge': 'CELAR_PDF_RASTER_MAX_EDGE',
    'ocrTotalTimeoutSeconds': 'CELAR_OCR_TOTAL_TIMEOUT_SECONDS',
    'chatTimeoutSeconds': 'CELAR_CHAT_TIMEOUT_SECONDS',
    'embeddingTimeoutSeconds': 'CELAR_EMBED_TIMEOUT_SECONDS',
}

def configure(path: Path = MANIFEST) -> dict:
    data = json.loads(path.read_text())
    if data.get('schema') != 1 or not set(FIELDS).issubset(data):
        raise ValueError('The source runtime manifest is incomplete')
    for source, name in FIELDS.items():
        value = data[source]
        os.environ[name] = ','.join(map(str, value)) if isinstance(value, list) else str(value)
    # Do not weaken source loopback restrictions for Docker DNS convenience.
    os.environ['CELAR_OLLAMA_BASE_URL'] = 'http://127.0.0.1:11434'
    os.environ['CELAR_CLAMAV_HOST'] = '127.0.0.1'
    os.environ['CELAR_CLAMAV_PORT'] = '3310'
    os.environ['CELAR_RUNTIME_TOKEN_FILE'] = '/run/secrets/runtime-token'
    return data

def health() -> None:
    token = Path('/run/secrets/runtime-token').read_text().strip()
    if len(token) < 32:
        raise ValueError('Runtime secret not provisioned')
    client = urllib.request.build_opener(urllib.request.ProxyHandler({}))
    for path in ['/health', '/v1/decisions/health']:
        request = urllib.request.Request('http://127.0.0.1:8787' + path, headers={
            'Authorization': 'Bearer ' + token,
            'X-Pulse-AI-Privacy-Boundary': 'private_pulse_runtime_only'})
        with client.open(request, timeout=5) as response:
            raw = response.read(65537)
            if len(raw) > 65536:
                raise ValueError('Oversized health response')
            data = json.loads(raw)
            if path == '/health' and data.get('status') != 'ready':
                raise ValueError('Inference/extraction/scanning readiness incomplete')
            if path.endswith('/decisions/health') and data.get('runtime_connected') is not True:
                raise ValueError('Laya readiness incomplete')
    print('CELAR_CONTAINER_HEALTH=PASS')

def self_test() -> None:
    # Runs in CI with --network none. No user documents or model weights.
    for executable in ['/usr/bin/tesseract', '/usr/bin/pdftotext', '/usr/sbin/clamd']:
        if not Path(executable).is_file():
            raise ValueError('Required native executable missing')
    version = subprocess.run(['/usr/bin/tesseract', '--version'], capture_output=True, text=True, check=True)
    if not version.stdout.startswith('tesseract 5.'):
        raise ValueError('Unexpected OCR major version')
    with tempfile.TemporaryDirectory() as directory:
        secret = Path(directory) / 'token'
        secret.write_text('synthetic-ci-token-' + '0' * 48)
        os.environ['CELAR_RUNTIME_TOKEN_FILE'] = str(secret)
        sys.path.insert(0, '/opt/celar-ai/gateway')
        from wsgi_decisions import app
        client = app.test_client()
        if client.get('/health').status_code != 401:
            raise ValueError('Unauthenticated health request was not rejected')
        headers = {'Authorization': 'Bearer ' + secret.read_text()}
        if client.get('/health', headers=headers).status_code != 403:
            raise ValueError('Privacy boundary was not enforced')
        headers['X-Pulse-AI-Privacy-Boundary'] = 'private_pulse_runtime_only'
        if client.get('/not-a-capability', headers=headers).status_code != 404:
            raise ValueError('Unknown route was not rejected')
        if client.get('/v1/decisions/health', headers=headers).status_code != 503:
            raise ValueError('Missing worker was not reported as unavailable')
    print('CELAR_CONTAINER_AUTH_AND_NATIVE_DEPENDENCIES=PASS')
    print('LIVE_MODELS_SCANNING_AND_LAYA_ACCURACY=NOT_RUN')

def main() -> None:
    mode = sys.argv[1] if len(sys.argv) == 2 else ''
    if mode not in {'gateway', 'health', 'self-test'}:
        raise ValueError('Choose gateway, health or self-test')
    configure()
    if mode == 'health':
        health()
    elif mode == 'self-test':
        self_test()
    else:
        os.chdir('/opt/celar-ai/gateway')
        os.execv('/usr/bin/gunicorn', ['gunicorn', '--workers', '2', '--threads', '4',
            '--bind', '127.0.0.1:8787', '--timeout', '3660', '--graceful-timeout', '30',
            '--keep-alive', '5', '--worker-tmp-dir', '/dev/shm', 'wsgi_decisions:app'])

if __name__ == '__main__':
    try:
        main()
    except Exception as error:
        # Do not include exception text: transport errors can contain details.
        print('CELAR_CONTAINER_CHECK=FAILED type=' + type(error).__name__, file=sys.stderr)
        raise SystemExit(1)
