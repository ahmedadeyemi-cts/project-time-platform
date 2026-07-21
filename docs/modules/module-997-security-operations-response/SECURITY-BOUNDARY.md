# Module 997 Security Boundary

## Native evidence

Module 997 reads restricted ProjectPulse authentication events, session
metadata, platform audit events, alerts, incidents, and response requests. It
does not expose passwords, hashes, tokens, secret values, raw provider payloads,
packet captures, or unredacted exception messages.

## Containment boundary

Native session revocation is the only built-in containment action. It requires:

1. a durable incident;
2. a prepared response request;
3. approval by an eligible actor other than the requester;
4. management permission outside View-As;
5. `PROJECTPULSE_SECURITY_NATIVE_SESSION_REVOCATION_ENABLED=true`; and
6. an active ProjectPulse session UUID as the bounded target.

All other containment requires an approved adapter. Missing external telemetry
or adapters remains explicit and is never represented as healthy.
