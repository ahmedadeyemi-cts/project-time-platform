# Module 081 authorization and imports

Organization-wide access is restricted to Super Administrator, Administrator, and Project Team Coordinator authority. Managers and engineering leads operate only inside their team or explicitly assigned team scope. Engineers operate on their team, custodied equipment, or assigned projects. Project managers and other viewers receive only records reachable through authoritative assignment.

There is no department-name fallback. The actual user remains the audit actor while the effective View-As user determines read scope. View-As never authorizes a mutation or export.

Imports accept only `.csv` and `.xlsx` files up to 10 MB. Credential-, password-, token-, key-, secret-, and connection-string-like headers are rejected. The preview stores a source checksum, parser version, sheet/row provenance, deterministic row fingerprint, sanitized payload, and validation messages. Operational tables change only after an authorized administrator commits accepted or explicitly reviewed warning rows; rejected and duplicate rows never commit.
