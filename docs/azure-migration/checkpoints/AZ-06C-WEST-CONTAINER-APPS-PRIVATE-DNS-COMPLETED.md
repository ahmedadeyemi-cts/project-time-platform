# AZ-06C — West Container Apps Private DNS Completed

Date: 2026-07-12

The internal West US 3 Container Apps environment private DNS configuration completed successfully.

Validated state:

- Environment: `cae-phd-test-westus3`
- Default domain: `jollywave-6212cd8b.westus3.azurecontainerapps.io`
- Internal IP: `10.30.0.167`
- Private DNS zone: `jollywave-6212cd8b.westus3.azurecontainerapps.io`
- Wildcard A record: `*.jollywave-6212cd8b.westus3.azurecontainerapps.io -> 10.30.0.167`
- West VNet link: `Completed`
- East VNet link: `Completed`

No application image, Container App, public DNS record, Cloudflare record, or East PostgreSQL replica was created.

Next step: run the read-only AZ-07A source-code checkpoint on the Oracle Linux source host.