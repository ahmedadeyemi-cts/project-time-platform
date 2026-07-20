# Module 998 Evidence and Redaction Contract

## Required evidence metadata

- evidence ID and correlation ID;
- approved source owner;
- observation and ingestion timestamps;
- freshness and confidence;
- severity and affected logical component;
- redaction result and classification;
- reviewer and review state;
- governing runbook and retention/disposal decision.

## Prohibited content

- credentials, tokens, keys, certificates, or secret values;
- connection strings or database passwords;
- raw provider payloads, logs, exceptions, or stack traces;
- private host names, IP addresses, tenant IDs, subscription IDs, or app IDs;
- unredacted customer, employee, time, project, billing, or approval records.

## Chain of custody

An approved future collector must minimize and redact before storage, hash the
governed artifact, record source and time, enforce least-privilege access, and
retain or dispose under an approved policy. The current module performs no
collection, storage, download, or export.
