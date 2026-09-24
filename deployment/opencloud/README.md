# OpenCloud deployment packages

Status: packaging work in progress. No OpenCloud installation, image publication, database migration, DNS change or Azure/Oracle rollout has been performed by this change.

## Confirmed requirements

- Two deployment bundles: Pulse with PostgreSQL; Celar AI with the existing Oracle-hosted processing capabilities.
- Internet-facing HTTPS service names: pulse-dev.ussignal.cloud and celarai-dev.ussignal.cloud. SSH administration uses the approved VPN/management network. Raw database, inference, scanning and container-management ports must not be public.
- Keep the existing Azure and Oracle endpoints running unchanged during preparation and parallel testing.
- Future production uses pulse.ussignal.cloud and celarai.ussignal.cloud, separate data/secrets, and an independently qualified redundant topology.
- GitHub remains the deployment system. Preserve personal-to-corporate replication to US-Signal-Technical-Operations-Center/ProjectPulse. Source/tag mirroring is not image, release-asset, secret, runner or environment replication.
- Corporate repository access is subject to organization SSO approval. Do not bypass that boundary or edit corporate main independently while it remains a one-way mirror.
- No live billing transmission, notifications, model-provider changes, customer data export, or production agent activation is authorized by packaging.

The implementation will inventory current runtime dependencies, validate packaging against the selected source revision, and provide a clear infrastructure handoff. Infrastructure planning readiness must remain distinct from installable-release and migration acceptance.
