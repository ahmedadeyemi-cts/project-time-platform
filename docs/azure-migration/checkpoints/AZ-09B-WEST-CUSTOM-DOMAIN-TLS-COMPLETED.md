# AZ-09B — West Custom Domain and TLS Completed

Completed: 2026-07-13

## Public endpoint

- URL: `https://phd-west-test.onenecklab.com`
- HTTPS root status: `200`
- HTTPS health status: `200`
- HTTP redirect status: `301`

## Certificate

- Certificate authority: Let's Encrypt
- Subject: `CN=phd-west-test.onenecklab.com`
- Expiration: `2026-10-11 00:12:57 GMT`
- Azure Key Vault certificate name: `tls-phd-west-test-onenecklab-com`
- Application Gateway certificate reference: created
- Renewal automation: pending

## Gateway and identity

- Application Gateway: `agw-phd-test-westus3`
- SKU: `WAF_v2`
- Managed identity: `id-phd-test-appgw-westus3`
- Key Vault role: `Key Vault Secrets User`

## Safety and migration status

- No application image rebuild occurred.
- No Container App redeployment occurred.
- No database change occurred.
- Oracle VM is not required for Azure runtime or TLS.
- Oracle VM termination remains deferred until the final source database export, reconciliation, and rollback window are complete.
- East PostgreSQL replica remains deferred.

## Result

`WEST_CUSTOM_DOMAIN_TLS_RESULT=READY`
