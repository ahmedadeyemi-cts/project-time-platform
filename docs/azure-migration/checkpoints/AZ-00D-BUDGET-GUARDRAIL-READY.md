# AZ-00D Budget Guardrail Ready

## Status

Ready to execute in Azure Cloud Shell.

## Purpose

Create or update a subscription-scoped Azure Cost Management budget for the Project Health Dashboard test subscription.

## Configuration

- Monthly budget: USD 200
- Default notification email: `Ahmed.Adeyemi@ussignal.com`
- Actual-cost notifications: 75%, 90%, 97.5%, and 100%
- Forecast notification: 90%
- Time grain: monthly
- API: Microsoft.Consumption budgets 2024-08-01

## Safety

The budget resource is a Cost Management control and creates no billable application infrastructure.

## Script

`deployment/azure/scripts/az00d-create-test-subscription-budget.sh`

## Migration hold

Creating the budget does not clear `HOLD_NO_COST_DATA`. The Rocky Linux restore VM and other blocked resources remain on hold until AZ-00C returns usable cost or forecast data and the monthly projection is within the USD 200 ceiling.
