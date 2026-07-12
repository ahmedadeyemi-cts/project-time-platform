# ADR-008: Continue Build with Daily Cost Monitoring

## Status

Accepted on 2026-07-12.

## Context

The Project Health Dashboard test subscription includes a monthly Azure credit of USD 200. A subscription budget named `project-health-dashboard-test-monthly-200` is active and sends email notifications to `Ahmed.Adeyemi@ussignal.com`.

The prior temporary hold was introduced because Azure Cost Management had not yet posted actual-cost or forecast rows. The project owner has clarified that infrastructure development should continue while cost is reviewed every day and managed reasonably.

## Decision

Development and migration work may continue in the test subscription even when cost data is still pending.

Cost monitoring is an operational guardrail rather than an automatic build blocker.

## Thresholds

- USD 150 actual cost: warning and begin optimization review.
- USD 180 actual cost: critical review of persistent and temporary resources.
- USD 195 actual cost: emergency action to deallocate or delete nonessential resources.
- USD 200 actual cost: monthly Azure credit treated as exhausted.
- USD 180 forecast: review projected month-end spend and identify avoidable recurring charges.

## Operational requirements

1. Run the cost-review script daily.
2. Keep Azure budget email notifications enabled.
3. Deallocate or delete temporary migration compute promptly.
4. Review PostgreSQL HA, Premium ACR geo-replication, NAT Gateways, public IPs, Application Gateways, Front Door, and the East US database replica as major cost drivers.
5. Do not weaken private networking, managed identity, encryption, or credential controls solely to reduce cost.
6. Production will use a separate paid subscription and may use the complete production architecture after formal sizing.

## Supersession

This decision supersedes the automatic build-hold interpretation in the AZ-00C cost-data hold checkpoint. The historical checkpoint remains valid as a record of what occurred before this decision.
