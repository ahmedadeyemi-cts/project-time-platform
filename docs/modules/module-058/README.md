# Module 058 — CI/CD Pipeline Administration

Module 058 provides an administrator-only CI/CD control center and portable
pipeline foundation.

## Active providers

- Source control: GitHub
- Deployment: Azure Container Apps
- Registry: Azure Container Registry
- Identity: GitHub OIDC / Azure federated workload identity
- Artifact format: OCI container images

## Future portability

Provider settings are environment-driven. The future repository provider and
OpenCloud deployment provider can replace the active implementations without
changing the Module 058 route or release-manifest model.

## Administrator route

`#cicd-pipeline`

Permissions:

- `SYSTEM_ADMINISTRATION`
- `MANAGE_ALL`

## Required GitHub environment variables

- `AZURE_CLIENT_ID`
- `AZURE_TENANT_ID`
- `AZURE_SUBSCRIPTION_ID`
- `AZURE_RESOURCE_GROUP`
- `AZURE_ACR_NAME`
- `AZURE_API_APP`
- `AZURE_WEB_APP`
- `PUBLIC_URL`

Production uses the protected `production` GitHub environment.
