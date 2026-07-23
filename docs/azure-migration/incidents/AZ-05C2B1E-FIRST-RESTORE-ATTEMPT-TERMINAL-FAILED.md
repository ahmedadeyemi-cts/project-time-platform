# AZ-05C2B1E — First Restore Attempt Terminal Failed

**Date:** 2026-07-12

The first restore attempt is terminal and must not be updated or reused.

- Run Command: `phd-restore-postgresql13-seed`
- State: `Failed`
- Exit code: `6`
- Failure stage: `retrieving-key-vault-secret`
- PostgreSQL contacted: no
- Target modified: no
- Evidence uploaded: yes

A clean retry requires a new Run Command name, isolated state directory, and new result prefix after Key Vault DNS and secret access validation pass.
