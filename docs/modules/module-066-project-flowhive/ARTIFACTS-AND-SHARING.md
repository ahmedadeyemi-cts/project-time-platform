# Module 066E — Artifacts and Customer Sharing

## Internal preview source

The source package can produce transient PDF and XLSX bytes for a validated
browser-local plan. Both formats use the exact repository US Signal logo and
carry the logo SHA-256 in artifact-control evidence.

- PDF: logo, internal-draft banner, plan/project/customer metadata, paginated
  schedule rows, critical indicator, float, and checksum footer.
- XLSX: logo, Plan Summary, Schedule, Dependencies, and Artifact Control sheets.

Every artifact is marked `INTERNAL DRAFT — NOT A CUSTOMER BASELINE`. Notes are
excluded by the current UI request. Nothing is stored or delivered.

## Locked customer capability

Any audience other than `internal` returns HTTP 423. There is no customer token,
URL, PIN, email, webhook, upload, outbox, or external API call.

Before customer enablement, acceptance must prove:

- approved baseline and immutable checksum;
- customer/project isolation;
- restricted-field redaction;
- expiring single-purpose tokens and revocation;
- access and download audit;
- recipient verification and delivery authorization;
- PDF/XLSX visual QA using US Signal logos;
- mobile/accessibility validation;
- rollback and incident response.
