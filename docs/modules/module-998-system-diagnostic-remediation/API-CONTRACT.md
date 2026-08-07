# Module 998 API Contract

Contract version: `2026-08-07.1`

## Read endpoints

| Method | Path | Purpose |
|---|---|---|
| GET | `/api/system-diagnostics/overview` | Operational posture and evidence counts |
| GET | `/api/system-diagnostics/checks` | Current sanitized native checks |
| GET | `/api/system-diagnostics/issues` | Failed, warning, and unknown retained findings |
| GET | `/api/system-diagnostics/sessions` | Diagnostic session history |
| GET | `/api/system-diagnostics/sessions/{id}` | Session, findings, and remediation evidence |
| GET | `/api/system-diagnostics/evidence-policy` | Data-minimization and retention boundary |
| GET | `/api/system-diagnostics/remediation-policy` | Lifecycle and adapter readiness |
| GET | `/api/system-diagnostics/runbooks` | Native and adapter-backed runbooks |
| GET | `/api/system-diagnostics/remediations` | Remediation approval/execution queue |

## Operational endpoints

| Method | Path | Outcome |
|---|---|---|
| POST | `/api/system-diagnostics/sessions` | Runs checks and persists sanitized findings |
| POST | `/api/system-diagnostics/remediation/prepare` | Persists a previewed plan |
| POST | `/api/system-diagnostics/remediation/approve` | Requires a separate eligible actor |
| POST | `/api/system-diagnostics/remediation/stage` | Confirms target and rollback readiness |
| POST | `/api/system-diagnostics/remediation/promote` | Executes native health refresh or returns `423 execution_adapter_required` |
| POST | `/api/system-diagnostics/remediation/verify` | Reruns checks, verifies accepted Azure revisions when applicable, and stores execution plus verification evidence |
| POST | `/api/system-diagnostics/remediation/close` | Closes verified or rolled-back work |

`POST /analysis` and `POST /remediation/rollback` remain adapter-gated.

For `restart_service`, promotion returns `409 remediation_execution_in_progress`
while its three-minute claim lease is active. An expired or legacy incomplete
claim is moved to `failed` and returns `409 azure_restart_reconciliation_required`;
automatic retry is forbidden. Verification requires retained accepted-revision
evidence and confirms every revision is active, `Healthy`, and `Running` through
the managed-identity Azure adapter before the request can become `verified`.

Partial execution returns `remediation_reconciliation_required` during
verification and is terminally `failed`; it cannot be closed as verified. A
fully accepted restart whose revisions are not yet healthy returns
`remediation_verification_pending`, remains `executed`, and may be verified
again. Each retry retains the original execution and accepted-revision evidence.
