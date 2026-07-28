# AZ-00E: Budget Active and Build Resumed

## Status

Completed on 2026-07-12.

## Confirmed budget

- Name: `project-health-dashboard-test-monthly-200`
- Amount: USD 200
- Time grain: Monthly
- Start: 2026-07-01
- Notification recipient: `Ahmed.Adeyemi@ussignal.com`

## Active notifications

- 75% actual cost
- 90% actual cost
- 97.5% actual cost
- 100% actual cost
- 90% forecasted cost

## Policy change

The project owner approved continued buildout in the test subscription while cost is reviewed daily. Missing Cost Management rows no longer impose an automatic infrastructure hold.

The following work may proceed with daily monitoring:

- temporary Rocky Linux 10 PostgreSQL restore runner;
- PostgreSQL restore and validation;
- later application and regional infrastructure phases.

Temporary resources must still be deallocated or deleted promptly after use. Daily cost reports and Azure budget alerts remain mandatory.

## Next executable step

Run `deployment/azure/scripts/az05c2a-private-rocky10-restore-runner.sh`, verify Rocky Linux 10 and private PostgreSQL connectivity, and then proceed to the restore phase.
