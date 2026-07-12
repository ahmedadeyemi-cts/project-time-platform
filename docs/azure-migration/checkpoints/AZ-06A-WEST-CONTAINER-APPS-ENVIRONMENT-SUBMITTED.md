# AZ-06A — West Container Apps Environment Submitted

Date: 2026-07-12

## Result

The guarded West US 3 Azure Container Apps environment submission was accepted.

## Environment

- Name: `cae-phd-test-westus3`
- Region: `westus3`
- Resource group: `rg-project-health-dashboard-test-app-westus3`
- Infrastructure subnet: `vnet-phd-test-westus3/snet-aca-infrastructure`
- Subnet prefix: `10.30.0.0/23`
- Subnet delegation: `Microsoft.App/environments`
- Network mode: internal
- Workload profiles: enabled
- Log Analytics workspace: `log-phd-test-westus3`

## Validated prerequisites

- Subscription selection matched the intended subscription.
- Microsoft.App, Microsoft.OperationalInsights, and Microsoft.ContainerService providers were registered.
- The delegated subnet and NAT Gateway attachment were present.
- West managed identity, ACR, Key Vault, and PostgreSQL primary were healthy.

## Submission state

- Azure submission result: accepted
- Immediate provisioning state: not yet visible
- Application images deployed: no
- East PostgreSQL replica retry: no

## Next action

Run `deployment/azure/scripts/az06b-check-west-container-apps-environment.sh` until the environment reports `Succeeded`, an internal configuration, the expected subnet, a default domain, and a static IP.
