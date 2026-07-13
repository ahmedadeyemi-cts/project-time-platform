# AZ-09A — West Application Gateway WAF Public Entry Ready

Date prepared: 2026-07-13

## Purpose

Create the public regional entry point for the healthy West US 3 web Container App while preserving the internal-only Azure Container Apps environment.

## Existing state

- West Container Apps environment is internal-only.
- API Container App active revision is healthy.
- Web Container App active revision is healthy.
- The generated Container Apps FQDN is private and is not expected to resolve from a normal public browser.
- The reserved West ingress public IP and dedicated Application Gateway subnet already exist.

## Planned resources and configuration

- Application Gateway: `agw-phd-test-westus3`
- SKU: `WAF_v2`
- Autoscale: minimum 0, maximum 2 capacity units
- Zones: 1, 2, and 3
- WAF policy: `waf-phd-test-westus3`
- WAF mode: Detection
- Public IP: `pip-phd-test-ingress-westus3`
- Application Gateway subnet: `snet-application-gateway` (`10.30.8.0/24`)
- Backend: `ca-phd-test-web-westus3` private FQDN
- Backend protocol: HTTPS/443
- Backend host header: selected from the Container App backend FQDN
- Health probe: HTTPS `/health`
- Initial listener: HTTP/80 for browser validation
- TLS and custom domain: pending a later step

## Safety and cost

- This is a billable Application Gateway WAF_v2 deployment with ongoing gateway and capacity charges.
- No application image will be rebuilt.
- No Container App will be redeployed.
- No database data or schema will be changed.
- No East US PostgreSQL replica will be created.
- The script requires explicit authorization through `PHD_CREATE_WEST_APP_GATEWAY_WAF=YES`.

## Canonical script

`deployment/azure/scripts/az09a-create-west-application-gateway-waf.sh`

## Expected terminal result

`WEST_PUBLIC_ENTRY_RESULT=READY`

The resulting public URL will use the DNS name assigned to the existing West ingress public IP. HTTPS and the final custom domain remain pending after the HTTP browser smoke test passes.
