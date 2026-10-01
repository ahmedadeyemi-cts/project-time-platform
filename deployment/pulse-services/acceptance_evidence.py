"""Finite native-test evidence. Never exports a document, address or credential."""
import ipaddress
import json
import socket
import ssl

STAGES = frozenset(('initialization', 'documents_unauthorized', 'documents_health',
    'documents_signatures', 'laya_unauthorized', 'laya_health', 'clean_scan',
    'antivirus_detection', 'png_ocr', 'pdf_ocr', 'unsafe_ocr_input',
    'laya_classification', 'complete'))
CODES = frozenset(('private_dns_empty', 'private_dns_rejected', 'path_rejected',
    'response_too_large', 'laya_health_shape_invalid', 'laya_health_contract_invalid',
    'unauthorized_request_accepted', 'service_not_ready', 'signature_freshness',
    'antivirus_test_detection', 'ocr_failed', 'ocr_text_missing', 'ocr_scan_mismatch',
    'unsafe_ocr_input_accepted'))
_stage = 'initialization'
_dns_classes = []
_http_status = None

def set_stage(stage):
    global _stage, _dns_classes, _http_status
    if stage not in STAGES:
        raise ValueError('unrecognized_acceptance_stage')
    _stage, _dns_classes, _http_status = stage, [], None
    print('PULSE_PRIVATE_SERVICE_CHECK=' + json.dumps({'stage': stage}), flush=True)

def record_http_status(status):
    global _http_status
    _http_status = status if type(status) is int and 100 <= status <= 599 else None

def record_dns_addresses(addresses):
    """Classify for diagnostics only; this never authorizes any address."""
    global _dns_classes
    categories = set()
    private = tuple(ipaddress.ip_network(n) for n in
        ('10.0.0.0/8', '172.16.0.0/12', '192.168.0.0/16'))
    # Documented Azure platform ranges, not additions to the access policy.
    platform = tuple(ipaddress.ip_network(n) for n in
        ('100.100.0.0/17', '100.100.128.0/19', '100.100.160.0/19', '100.100.192.0/19'))
    for raw in addresses[:20]:
        try:
            address = ipaddress.ip_address(raw)
            if address.is_loopback:
                category = 'loopback'
            elif address.is_link_local:
                category = 'link_local'
            elif address.version == 4 and any(address in n for n in private):
                category = 'rfc1918'
            elif address.version == 4 and any(address in n for n in platform):
                category = 'azure_platform_reserved'
            else:
                category = 'other_address'
        except ValueError:
            category = 'invalid_address'
        categories.add(category)
    _dns_classes = sorted(categories)

def failure_record(error):
    message = str(error)
    if isinstance(error, (ValueError, AssertionError)) and message in CODES:
        diagnostic = message
    elif isinstance(error, json.JSONDecodeError):
        diagnostic = 'response_invalid_json'
    elif isinstance(error, ssl.SSLCertVerificationError):
        diagnostic = 'tls_verification_failed'
    elif isinstance(error, socket.gaierror):
        diagnostic = 'dns_resolution_failed'
    elif isinstance(error, TimeoutError):
        diagnostic = 'request_deadline_exceeded'
    elif isinstance(error, OSError):
        diagnostic = 'request_transport_failed'
    elif isinstance(error, AssertionError):
        diagnostic = 'assertion_failed'
    else:
        diagnostic = 'unexpected_failure'
    return {'passed': False, 'stage': _stage, 'diagnostic': diagnostic,
            'dnsAddressClasses': list(_dns_classes), 'httpStatus': _http_status,
            'rawDocumentContentPublished': False}
