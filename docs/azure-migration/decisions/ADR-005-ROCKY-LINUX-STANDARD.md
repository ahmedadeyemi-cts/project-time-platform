# ADR-005: Rocky Linux standard for migration hosts

Date: 2026-07-12
Status: Accepted

## Decision

Temporary Azure migration and restore hosts for Project Health Dashboard will use Rocky Linux 9 x86-64.

Ubuntu must not be used for the restore runner or other migration helper virtual machines unless this decision is explicitly superseded in a later architecture decision record.

## Rationale

- The source application currently runs on an Enterprise Linux-family operating system.
- The target application modernization work is already standardized around Rocky Linux.
- Using Rocky Linux reduces package-management, service-management, shell, and operational differences during migration.
- The migration process should use `dnf`, RPM packages, SELinux-aware procedures, and Rocky-compatible administration commands.

## Implementation controls

The canonical restore-runner script:

- Discovers a Rocky Linux 9 x86-64 image available in the target Azure region.
- Refuses to fall back to Ubuntu or another distribution.
- Creates the VM without a public IP.
- Uses Azure Run Command for administration.
- Validates `/etc/os-release` and requires `ID=rocky` with major version 9.
- Uses `dnf` for package installation.
- Records the selected image publisher, offer, SKU, version, and URN in the non-secret deployment configuration.

## Canonical script

`deployment/azure/scripts/az05c2a-private-rocky-restore-runner.sh`

The earlier Ubuntu-based draft was removed before deployment. No Ubuntu restore-runner VM was created.
