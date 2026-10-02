"""Pure validation for Pulse service acceptance. No deployment or account operations."""
import re
from laya_protocol import REVISION, LABELS, VERSION

def require(value, code):
    if not value:
        raise ValueError(code)

def acceptance_job_name(run_id):
    """Azure Container Apps Job names must remain below 32 characters."""
    require(isinstance(run_id, str) and re.fullmatch(r"[1-9][0-9]{0,19}", run_id) is not None,
            "acceptance_run_identity_invalid")
    name = "psvc-check-" + run_id
    require(len(name) <= 31, "acceptance_job_name_too_long")
    return name

def validate_laya_health(body):
    """Validate the gateway's public health contract, not its private worker wire format."""
    expected = {
        "adapter_version": VERSION,
        "model_revision": REVISION,
        "ok": True,
        "status": "ready",
        "runtime_connected": True,
        "state_token_budget": 450,
        "supported_labels": list(LABELS),
        "review_required": True,
        "automation_approved": False,
        "workflow_actions_performed": 0,
        "external_fallback_allowed": False,
        "production_accuracy_validated": False,
    }
    require(isinstance(body, dict), "laya_health_shape_invalid")
    for name, value in expected.items():
        require(type(body.get(name)) is type(value) and body[name] == value,
                "laya_health_contract_invalid")
    require(type(body.get("inference_busy")) is bool, "laya_health_contract_invalid")
    return body

def validate_document_runtime(body):
    """Private DNS alone is not evidence that the selected scanner and OCR are ready."""
    require(isinstance(body, dict), "document_runtime_shape_invalid")
    ready = body.get("readiness")
    require(isinstance(ready, dict), "document_runtime_shape_invalid")
    allowed_status = {"private_document_runtime_ready", "private_document_runtime_partially_ready"}
    require(body.get("status") == ready.get("status")
            and body.get("status") in allowed_status,
            "document_runtime_not_ready")
    for field in ("clamAvConfigured", "malwareScannerEndpointPrivate", "ocrConfigured",
                  "ocrEndpointPrivate", "workerEnabled", "automaticDocumentQueueEnabled",
                  "documentServicePrincipalAuthorized", "processingTablesAvailable",
                  "uploadStorageProductionReady"):
        require(ready.get(field) is True, "document_runtime_required_capability_missing")
    return ready
