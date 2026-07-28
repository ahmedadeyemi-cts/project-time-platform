# ADR-006: Rocky Linux 10 Standard for Azure Migration Hosts

- Status: Accepted
- Date: 2026-07-11
- Supersedes: ADR-005 Rocky Linux 9 standard

## Decision

Use Rocky Linux 10.x for temporary Azure migration and restore virtual machines. At the time of this decision, Rocky Linux 10.2 is the current supported minor release.

The deployment automation must:

- Use the official Rocky Enterprise Software Foundation Azure publisher account, `resf`, or an official Rocky Linux Community Gallery image.
- Select the latest available Rocky Linux 10 x86-64 image in the target Azure region.
- Refuse to fall back to Rocky Linux 9, Ubuntu, or another distribution.
- Validate `/etc/os-release` after deployment and require `ID=rocky` with `VERSION_ID` beginning with `10`.
- Run `dnf -y upgrade` before installing migration tooling.
- Use only modern Azure VM sizes compatible with Rocky Linux 10's x86-64-v3 baseline.
- Record the selected publisher, offer, SKU, image version, image URN, Rocky minor version, and VM size in the non-secret deployment configuration and execution log.

## Rationale

Rocky Linux 10 provides the longest support runway for newly created migration hosts and aligns with the project's preference to use the newest supported Rocky Linux major release. The restore runner is a fresh deployment, so no major-version in-place upgrade is required.

The restore workload requires only standard supported tooling: DNF, curl, Azure managed identity access, AzCopy, PostgreSQL client utilities, private DNS, and TCP connectivity to Azure Database for PostgreSQL.

## Compatibility constraint

Rocky Linux 10 uses x86-64-v3 as the minimum x86-64 microarchitecture baseline. The VM-size selector must therefore avoid older or uncertain CPU families and choose a compatible current-generation Azure VM size.

## Marketplace constraint

The script must perform region-specific image discovery before provisioning. If an official Rocky Linux 10 image is not available to the subscription in the selected region, the deployment must stop without creating a different operating system.

## Scope

This decision applies to Azure virtual machines created for migration, restore, validation, administration, or troubleshooting. Azure managed services, including Azure Container Apps and Azure Database for PostgreSQL, do not expose a customer-selectable host operating system and are outside this decision.
