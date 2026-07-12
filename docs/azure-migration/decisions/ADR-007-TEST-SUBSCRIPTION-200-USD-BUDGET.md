# ADR-007: Test Subscription Monthly Budget Guardrail

## Status

Accepted on 2026-07-11.

## Decision

The Project Health Dashboard Azure **test** environment must be operated within a target ceiling of **USD 200 per month** because the current subscription includes a monthly Azure credit of that amount.

Production will be deployed later into a separate paid subscription and may use the full production architecture and sizing.

## Test-environment guardrails

1. Treat USD 200/month as a hard planning ceiling, not a target to consume.
2. Use a warning threshold of USD 150, a critical threshold of USD 180, and an emergency threshold of USD 195.
3. Temporary migration compute must be deallocated or deleted immediately after its task completes.
4. Do not create the East US PostgreSQL replica until the current monthly cost and forecast have been reviewed.
5. Do not create persistent WAF_v2 Application Gateways or Azure Front Door Premium in this subscription until a cost checkpoint confirms sufficient headroom.
6. Avoid always-on test compute when stop/deallocate/delete is practical.
7. Continue using private networking and managed identity; cost savings must not be achieved by weakening credential or data-access controls.
8. Record every persistent billable resource and its intended deletion or production-migration date.

## Current cost risks

The largest likely recurring charges are:

- PostgreSQL Flexible Server compute and HA standby
- PostgreSQL storage and backup retention
- Premium Azure Container Registry and geo-replication
- NAT Gateway hourly charges
- Standard public IP resources
- Future Application Gateway WAF_v2 instances
- Future Azure Front Door Premium
- Future East US PostgreSQL replica

## Temporary Rocky Linux restore runner

The Rocky Linux 10 restore runner is approved only as a short-lived migration resource. It must:

- have no public IP;
- use managed identity;
- be created only when the restore is ready to begin;
- be deallocated after restore validation;
- be deleted after its logs and results are preserved.

## Production

The production subscription is separate from this test subscription. The production deployment may restore full multi-region availability, WAF, Front Door, dedicated ingress, and cross-region database replication after formal sizing and cost approval.
