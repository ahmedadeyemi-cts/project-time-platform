# Module 066E — Artifacts and Customer Sharing

## Internal preview source

The source package can produce transient PDF and XLSX bytes for a validated
browser-local plan. Both formats use the exact repository US Signal logo and
carry the logo SHA-256 in artifact-control evidence.

- PDF: landscape US Signal-branded schedule with plan/project/customer metadata,
  paginated task rows, and the exact Planner columns: WBS, Task Name, Start Date,
  End Date, Duration in Days, Progress, Predecessor, Type, Comments, Notes, and
  Assigned Identity.
- XLSX: logo, Plan Summary, Schedule, Dependencies, and Artifact Control sheets.
  The Schedule worksheet uses that same exact column order.

Comments, notes, and the Module 062-backed assigned identity are included in
both export formats. Every artifact is marked
`INTERNAL DRAFT — NOT A CUSTOMER BASELINE`. Nothing is stored or delivered.

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
