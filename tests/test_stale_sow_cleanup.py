#!/usr/bin/env python3
import importlib.util
from pathlib import Path

SCRIPT = Path(__file__).resolve().parents[1] / "scripts/release-test/cleanup-stale-sows-protected-test.py"
spec = importlib.util.spec_from_file_location("stale_sow_cleanup", SCRIPT)
assert spec and spec.loader
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)

CURRENT = "11111111-1111-4111-8111-111111111111"
STALE_A = "22222222-2222-4222-8222-222222222222"
STALE_B = "33333333-3333-4333-8333-333333333333"
GSD = "44444444-4444-4444-8444-444444444444"
PLAN = "55555555-5555-4555-8555-555555555555"

documents = [
    {"documentId": CURRENT, "fileName": "Current SOW.pdf", "documentType": "SOW", "status": "active"},
    {"documentId": STALE_A, "fileName": "Old SOW.pdf", "documentType": "Statement of Work", "status": "active"},
    {"documentId": STALE_B, "fileName": "Archived Scope.pdf", "documentType": "Other", "status": "archived"},
    {"documentId": GSD, "fileName": "Current GSD.pdf", "documentType": "GSD", "status": "active"},
]
evidence = [
    {"documentId": PLAN, "workRegisterDocumentId": CURRENT, "category": "sow"},
    {"documentId": "66666666-6666-4666-8666-666666666666",
     "workRegisterDocumentId": STALE_B, "category": "statement_of_work"},
]
stale, reason = module.classify_stale(CURRENT.lower(), documents, evidence)
assert reason == ""
assert {row["documentId"] for row in stale} == {STALE_A.lower(), STALE_B.lower()}
assert all(row["documentId"] != GSD.lower() for row in stale)

missing, reason = module.classify_stale(
    "77777777-7777-4777-8777-777777777777", documents, evidence)
assert missing == []
assert reason == "current_sow_not_present_in_work_register"

readiness = {"preparation": {"documents": [
    {"documentId": PLAN, "category": "SOW"},
    {"documentId": GSD, "category": "GSD"},
]}}
enterprise = {"sowEvidence": evidence}
current, reason = module.current_sow_work_register_id(readiness, enterprise)
assert reason == ""
assert current == CURRENT.lower()

ambiguous = {"preparation": {"documents": [
    {"documentId": PLAN, "category": "SOW"},
    {"documentId": "88888888-8888-4888-8888-888888888888", "category": "SOW"},
]}}
current, reason = module.current_sow_work_register_id(ambiguous, enterprise)
assert current == ""
assert reason == "current_planning_sow_not_unique"
print("STALE_SOW_CLEANUP_UNIT=PASS")
