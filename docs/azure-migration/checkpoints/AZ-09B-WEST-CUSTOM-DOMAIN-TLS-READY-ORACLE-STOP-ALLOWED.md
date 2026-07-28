# AZ-09B — West Custom Domain TLS Ready; Oracle VM Stop Allowed

Date prepared: 2026-07-13

## Current Azure state

- West API Container App is healthy and running.
- West web Container App is healthy and running.
- PostgreSQL data has been restored and validated in Azure.
- API and web images are stored in Azure Container Registry.
- West Application Gateway WAF_v2 is healthy.
- Public HTTP root and health checks return HTTP 200.

## Oracle source VM decision

The Oracle source VM is no longer required for the running Azure test application and may be **stopped now** to reduce compute charges.

Do not terminate the instance or delete its boot volume yet. Retain it in a stopped state until:

1. HTTPS/custom-domain validation is complete.
2. Application functional testing and migrated-data reconciliation are complete.
3. The final write-freeze PostgreSQL export and delta reconciliation are complete.
4. The rollback retention window has been accepted.

Stopping the Oracle VM does not remove the need to review any continuing boot-volume, block-volume, backup, or reserved network resource charges.

## TLS/custom-domain plan

- Custom domain: `phd-west-test.onenecklab.com`
- DNS provider: Cloudflare
- DNS record: direct A record to West Application Gateway public IP, initially DNS-only
- Certificate authority: Let's Encrypt
- Validation: Cloudflare DNS-01 using a scoped API token
- Certificate storage: Azure Key Vault `kv-phd-t-w3-7825cc`
- Gateway identity: `id-phd-test-appgw-westus3`
- HTTPS listener: Application Gateway port 443
- HTTP behavior: permanent redirect to HTTPS
- Certificate renewal automation: follow-up task after initial TLS validation

## Required Cloudflare token permissions

Limit the token to the `onenecklab.com` zone with:

- Zone / DNS / Edit
- Zone / Zone / Read

The token must be entered through the script's hidden prompt and must never be committed, pasted into a PR, or shared in chat.

## Canonical script

`deployment/azure/scripts/az09b-configure-west-custom-domain-tls.sh`

## Expected result

`WEST_CUSTOM_DOMAIN_TLS_RESULT=READY`

The script does not rebuild images, redeploy Container Apps, change database data, or create the deferred East US PostgreSQL replica.
