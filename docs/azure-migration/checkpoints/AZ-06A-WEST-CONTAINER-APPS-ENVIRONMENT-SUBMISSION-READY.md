# AZ-06A — West Container Apps Environment Submission Ready

Date: 2026-07-12

## Decision

Resume the deployable West US 3 application path and defer the blocked East US PostgreSQL read replica. The regional replica is not required to create the primary Container Apps environment or continue application-platform deployment.

## Current database state

- West US 3 PostgreSQL primary is Ready.
- Initial database import and validation passed.
- Migration evidence was preserved and validated.
- Temporary migration permissions were removed.
- Migration VM is deallocated.
- East US replica was not created and no replica billing started.

## West deployment action

The next billable deployment action is the guarded asynchronous creation of:

- Container Apps environment: `cae-phd-test-westus3`
- Resource group: `rg-project-health-dashboard-test-app-westus3`
- Region: `westus3`
- Infrastructure subnet: `vnet-phd-test-westus3/snet-aca-infrastructure`
- Environment type: internal, workload profiles enabled
- Logging: existing `log-phd-test-westus3` Log Analytics workspace
- Infrastructure resource group: `rg-project-health-dashboard-test-aca-infra-westus3`

The submission script validates the existing West network, delegated `/23` subnet, NAT Gateway attachment, ACR, Key Vault, managed identity, Log Analytics workspace, and PostgreSQL primary before submitting the environment deployment.

## Application source-code boundary

Creating the Container Apps environment does not build or publish application images. The source server still has intentionally preserved uncommitted application changes. Image build and deployment remain blocked until those changes are reviewed, committed, and pushed without disturbing the current working tree.

## Scripts

- `deployment/azure/scripts/az06a-submit-west-container-apps-environment.sh`
- `deployment/azure/scripts/az06b-check-west-container-apps-environment.sh`

## Safety

- The submit script requires `PHD_CREATE_BILLABLE_WEST_ENVIRONMENT=YES`.
- The operation does not retry East US replica creation.
- The operation does not deploy application images.
- The status checker can query the known environment name even if the local state file is missing.
