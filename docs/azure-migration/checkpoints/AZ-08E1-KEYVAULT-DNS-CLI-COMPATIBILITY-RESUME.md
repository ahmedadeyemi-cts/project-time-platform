# AZ-08E1 — Key Vault DNS CLI Compatibility Resume

Date: 2026-07-13

AZ-08E stopped before modifying the Key Vault DNS zone group because the installed Azure CLI rejected the unsupported `--yes` argument on `az network private-endpoint dns-zone-group delete`.

Confirmed before the stop:

- West Key Vault private endpoint provisioning: `Succeeded`
- Private endpoint connection: `Approved`
- Private endpoint IP: `10.30.5.7`
- Existing ACR images remain available and are not rebuilt
- Existing API Container App remains available from the successful identity bootstrap stage

`az08e1-keyvault-dns-cli-compatibility-resume.sh` downloads the canonical AZ-08E script, removes only the unsupported flag from the DNS zone-group delete command, validates the corrected script, and resumes the Key Vault DNS repair and West deployment finish.
