# AZ-09A — West Application Gateway WAF Public Entry Ready

Date completed: 2026-07-13

## Result

The West public entry point for Project Health Dashboard is operational.

- Application Gateway: `agw-phd-test-westus3`
- SKU: `WAF_v2`
- Provisioning state: `Succeeded`
- WAF policy: `waf-phd-test-westus3`
- WAF mode: `Detection`
- Autoscale: minimum 0, maximum 2 capacity units
- Zones: 1, 2, and 3
- Public IP resource: `pip-phd-test-ingress-westus3`
- Public IP: `20.118.180.129`
- Public FQDN: `phd-test-westus3-7825cc.westus3.cloudapp.azure.com`
- Public URL: `http://phd-test-westus3-7825cc.westus3.cloudapp.azure.com`
- Backend: `ca-phd-test-web-westus3.jollywave-6212cd8b.westus3.azurecontainerapps.io`
- Backend protocol: HTTPS/443
- Health probe: HTTPS `/health`
- Backend health: `Healthy`
- Public root status: `200`
- Public health status: `200`
- Final result: `WEST_PUBLIC_ENTRY_RESULT=READY`

## Architecture state

The Container Apps environment remains internal-only. Application Gateway WAF_v2 provides the public browser entry point and forwards traffic privately to the healthy web Container App. The web Container App continues to proxy application API requests to the internal API Container App.

## Scope

This phase reused the existing healthy web and API Container Apps. It did not rebuild images, redeploy PostgreSQL, modify database schema or data, or create an East PostgreSQL replica.

## Security status

The first public listener is HTTP for validation only. TLS, the final custom domain, certificate binding, and HTTP-to-HTTPS redirection remain pending. Sensitive credentials should not be submitted until HTTPS is enabled.

## Cost status

Application Gateway WAF_v2 is an ongoing billable resource. The test gateway remains subject to the active subscription budget alerts and daily cost monitoring.

## Canonical script

`deployment/azure/scripts/az09a-create-west-application-gateway-waf.sh`
