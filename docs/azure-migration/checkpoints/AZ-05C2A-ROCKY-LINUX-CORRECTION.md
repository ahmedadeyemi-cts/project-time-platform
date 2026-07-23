# AZ-05C2A Rocky Linux correction

Date: 2026-07-12

The initial private restore-runner draft referenced Ubuntu 22.04. That draft was corrected before deployment based on the project operating-system standard.

## Final decision

- Restore runner OS: Rocky Linux 9 x86-64
- No Ubuntu restore-runner VM was created
- No public IP will be assigned
- Azure Run Command will be used for administration
- `dnf` will be used for packages
- The deployment must validate `ID=rocky` and major version 9 from `/etc/os-release`
- The deployment must hard-fail rather than fall back to Ubuntu or another distribution

## Canonical script

`deployment/azure/scripts/az05c2a-private-rocky-restore-runner.sh`

## Architecture record

`docs/azure-migration/decisions/ADR-005-ROCKY-LINUX-STANDARD.md`
