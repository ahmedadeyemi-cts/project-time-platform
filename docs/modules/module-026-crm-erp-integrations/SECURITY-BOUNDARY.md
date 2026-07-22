# Module 026 security boundary

- Integration credentials require a separately managed 32-byte encryption key.
- AES-256-GCM uses a random nonce and provider/kind-specific authenticated data.
- Secrets are write-only and are zeroed from temporary byte arrays after use.
- OAuth state is random, hashed at rest, single-use, and expires after ten minutes.
- External requests require HTTPS, reject user-info URLs, reject local/private/link-local targets (including IPv6 unique-local and mapped-private addresses), and do not follow redirects.
- The Module 026 client disables proxy routing, resolves the target again at connection time, rejects the entire result when any address is non-public, and connects directly to an approved resolved IP so DNS rebinding cannot redirect credential-bearing traffic.
- Connection tests store no response body and expose only sanitized outcomes.
- SELL record imports are response-size bounded and retain only configured mapped fields; raw responses are not stored.
- SELL lookup templates require a literal `{recordId}` token and pass the same public-HTTPS/private-address protections.
- OAuth token responses are read with a fixed 64 KiB upper bound and are never returned to the browser.
- Mutation requests require an actual ProjectPulse session, same origin, and explicit management authority.
- View-As does not grant configuration or testing authority.
- Migration 034 and production deployment require separate authorization.
