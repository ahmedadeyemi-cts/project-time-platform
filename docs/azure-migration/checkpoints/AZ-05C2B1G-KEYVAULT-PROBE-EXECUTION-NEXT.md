# AZ-05C2B1G — Key Vault DNS and Probe Execution Next

**Date:** 2026-07-12

The first restore attempt is terminal and its evidence is preserved. The next execution sequence is:

1. Repair and validate the East Key Vault private DNS zone group, VNet link, and A record.
2. Run a uniquely named read-only Key Vault probe.
3. Require `KEYVAULT_ACCESS_PROBE_RESULT=SECRET_RETRIEVAL_SUCCEEDED`.
4. Only then submit a clean restore retry under a new Run Command name, new result prefix, and isolated state directory.

The temporary restore runner remains running and billable during this work; deallocate it promptly after the restore reaches a terminal validated state or another controlled hold.
