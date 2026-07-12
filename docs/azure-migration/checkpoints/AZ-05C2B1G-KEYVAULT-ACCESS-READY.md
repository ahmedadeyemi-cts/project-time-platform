# AZ-05C2B1G — East Key Vault Access Ready

**Date:** 2026-07-12

## Verified result

The uniquely named read-only Key Vault probe completed successfully from the East US Rocky Linux restore runner.

- Key Vault host resolved to private IP `10.40.5.7`.
- Managed-identity authentication succeeded.
- The Key Vault REST request returned HTTP 200.
- The required PostgreSQL credential record exists, is versioned, and is enabled.
- No credential value was displayed or committed.

## Restore status

The first PostgreSQL restore attempt is terminal `Failed`, and no restore-related process remains active.

That attempt downloaded and validated all 15 source artifacts, then stopped during Key Vault DNS resolution. It never connected to PostgreSQL and did not modify the target database.

## Decision

The private Blob, Key Vault, and PostgreSQL DNS paths are ready for a clean restore retry using a new managed Run Command name, isolated state directory, new guest log, and new evidence prefix.
