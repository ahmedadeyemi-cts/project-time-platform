# Module 997 Threat Intelligence Policy

## Approved-source contract

Future intelligence may come from governed internal telemetry, licensed vendor
sources, government or sector advisories, approved community exchanges, and
recorded analyst observations. No source is connected in this checkpoint.

Every future indicator requires source authority, observation and ingestion
time, confidence, freshness, expiry, license/handling terms, applicable asset
class, redaction result, and analyst disposition.

## Confidence

- `unconfirmed` (0–24): preserve and corroborate.
- `possible` (25–49): enrich only through approved sources.
- `probable` (50–79): escalate for analyst review.
- `confirmed` (80–100): invoke approved incident authority.

Confidence never grants automated containment. Indicators must not expose raw
payloads, credentials, private topology, customer content, or unrelated identity
data. Expired or stale intelligence remains visible as stale, never current.
