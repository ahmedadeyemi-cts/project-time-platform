# Azure Support Request — East US PostgreSQL Flexible Server Region Access

## Request type

- Issue type: **Service and subscription limits**
- Service: **Azure Database for PostgreSQL Flexible Server**
- Subscription: `cd32baeb-7b71-4bc0-8ea3-9f23a50903fe`
- Requested region: **East US**

## Suggested title

Enable Azure Database for PostgreSQL Flexible Server provisioning in East US

## Suggested description

Azure Database for PostgreSQL Flexible Server read-replica creation in East US is rejected with:

`The location is restricted from performing this operation.`

Source server:

- name: `pg-phd-test-w3-7825cc`
- resource group: `rg-project-health-dashboard-test-data-westus3`
- region: `West US 3`
- PostgreSQL version: `16`
- SKU: `Standard_D2ds_v4`
- tier: `GeneralPurpose`
- storage: `128 GiB`
- networking: private delegated subnet

Requested replica:

- name: `pg-phd-test-eus-7825cc`
- resource group: `rg-project-health-dashboard-test-data-eastus`
- region: `East US`
- PostgreSQL version: `16`
- SKU: `Standard_D2ds_v4`
- tier: `GeneralPurpose`
- storage: `128 GiB`
- networking: private delegated subnet `snet-postgresql` in `vnet-phd-test-eastus`

The East US PostgreSQL capability response advertises one compatible `supportedFastProvisioningEditions` match for this configuration, but the actual control-plane create request is denied.

The capability response reason is:

`Provisioning is restricted in this region. Please choose a different region. For exceptions to this rule please open a support request with Issue type of 'Service and subscription limits'.`

Please confirm and enable Azure Database for PostgreSQL Flexible Server provisioning in East US for this subscription so the planned cross-region read replica can be created.

## Evidence to attach

- `az05c3a1-eastus-postgresql-replica-sku-diagnostic-20260712T190556Z.log`
- `az05c3a2-postgresql-eastus-replica-preflight-corrected-20260712T191138Z.log`
- `az05c3b1-eastus-postgresql-location-restriction-20260712T192346Z.log`
- failed activity-log correlation ID and status message when available

## Safety and cost state

- replica resource created: no
- replica billing started: no
- migration VM: deallocated
- temporary result-upload role: removed
- creation retry blocked until Azure confirms regional access
