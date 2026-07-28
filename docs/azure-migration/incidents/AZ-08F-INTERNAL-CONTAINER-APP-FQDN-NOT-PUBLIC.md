# AZ-08F — Internal Container Apps FQDN Not Publicly Accessible

Date recorded: 2026-07-13

## Observation

The West API and web Container Apps were healthy, but the generated web FQDN did not resolve from Firefox or Azure Cloud Shell.

## Root cause

The Container Apps environment was intentionally created as an internal environment. Its generated application FQDN is private and resolves only from the linked virtual network or a connected private network.

An app-level ingress value of `external` does not create a public endpoint when the environment itself uses an internal virtual IP.

## Correction

The generated Container Apps FQDN must not be represented as a public browser URL.

The planned public entry pattern is:

1. Public Application Gateway WAF_v2 frontend
2. Private HTTPS backend to the internal web Container App FQDN
3. Web Container App proxy to the internal API Container App
4. Private connection from the API to PostgreSQL

## Current status

- API Container App: healthy
- Web Container App: healthy
- Direct public access to generated Container Apps FQDN: not expected
- Public Application Gateway entry: prepared in AZ-09A
