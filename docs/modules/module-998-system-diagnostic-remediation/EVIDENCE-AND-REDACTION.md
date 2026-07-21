# Module 998 Evidence and Redaction Contract

Each diagnostic session stores sanitized findings containing a check code,
category, status, severity, summary, bounded evidence JSON, observation time,
target, and actor. Remediation evidence stores the runbook, action, target,
preview, adapter, rollback guidance, approvals, execution result, and
verification time.

Prohibited content includes credentials, tokens, keys, certificates, secret
values, connection strings, database passwords, raw provider payloads, raw log
bodies, stack traces, and unredacted customer or employee records.

Audit records provide chain-of-custody for session creation, remediation
preparation, approval, execution, verification, and closure. External export is
not enabled by this activation.
