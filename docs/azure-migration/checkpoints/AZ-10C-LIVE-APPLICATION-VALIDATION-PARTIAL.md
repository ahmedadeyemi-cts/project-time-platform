# AZ-10C — Live Azure Application Validation (Partial)

Date: 2026-07-13

## User-confirmed validation

The following actions were completed successfully through the public Azure endpoint:

- Opened `https://phd-west-test.onenecklab.com`
- Signed in successfully
- Restored an existing project successfully

## Confirmed infrastructure path

The successful browser workflow traversed:

1. Cloudflare public DNS
2. Azure Application Gateway WAF_v2
3. Internal Azure Container Apps web application
4. Internal Azure Container Apps API
5. Private Azure Database for PostgreSQL Flexible Server

## What this validates

- Public DNS and HTTPS are operational.
- The Application Gateway backend is healthy.
- Authentication and session propagation are operational.
- The web-to-API proxy path is operational.
- The API can read and write the Azure PostgreSQL database.
- The project lifecycle restoration workflow is operational for the tested project.

## What remains unverified

This checkpoint does not claim complete application acceptance. The following still require explicit validation:

- Billing identifier create/edit/apply workflows
- Full project lifecycle archive and restore scenarios
- Role and permission enforcement across protected routes
- Administrator user switching and effective-user audit behavior
- Project intake and resource assignment
- Approval, export, and audit workflows
- Certificate renewal automation
- WAF Prevention-mode readiness

## Next application phase

`AZ-11A — Role Enforcement and User Switcher Source Inventory`

The Oracle source VM is not required for this phase and may remain stopped.
