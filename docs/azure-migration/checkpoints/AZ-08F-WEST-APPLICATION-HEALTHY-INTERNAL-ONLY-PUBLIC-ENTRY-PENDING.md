# AZ-08F — West Application Healthy, Internal-Only; Public Entry Pending

Date: 2026-07-13

## Status

The West US 3 API and web Azure Container Apps revisions are deployed, healthy, and running.

- API: `ca-phd-test-api-westus3`
- Web: `ca-phd-test-web-westus3`
- Container Apps environment: `cae-phd-test-westus3`
- Environment accessibility: internal-only
- Environment default domain: `jollywave-6212cd8b.westus3.azurecontainerapps.io`
- Environment internal static IP: `10.30.0.167`

## Observed behavior

The web app FQDN does not resolve from a normal public browser or standard Azure Cloud Shell:

`ca-phd-test-web-westus3.jollywave-6212cd8b.westus3.azurecontainerapps.io`

This is expected for an internal Container Apps environment. App-level external ingress exposes the app through the environment inbound IP, but the environment inbound IP is private.

## Correction

The application deployment is complete at the Container Apps layer, but public browser access is not complete.

The generated Container Apps FQDN must not be presented as a public URL.

## Next action

Create the planned public reverse-proxy entry point in West US 3 using Azure Application Gateway WAF_v2, targeting the internal web Container App through the existing private DNS zone. After validation, add the custom public DNS name and TLS certificate. Azure Front Door Premium remains a later global-entry phase.

## Safety and cost

- No application image rebuild is required.
- No database change is required.
- No East PostgreSQL replica is created.
- Application Gateway WAF_v2 is a billable resource and requires explicit execution authorization before creation.
