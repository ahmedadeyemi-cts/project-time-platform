# OCI Free Tier VM Setup Guidance

## 1. Purpose

This document captures the initial Oracle Cloud Infrastructure Free Tier setup direction for the Project Time Platform.

The goal is to create a low-cost/free test environment that closely matches the intended end-state deployment on Rocky Linux.

## 2. Current Direction

The recommended first OCI resource is a Compute VM instance.

The platform should not begin with an Oracle managed database service for the main application database. The target application database is PostgreSQL, and the MVP/test environment should run PostgreSQL on the VM or as a container on the VM so the architecture remains portable to the final Rocky Linux deployment.

## 3. Current OCI Image Decision

During initial VM creation, Rocky Linux was not available as a selectable image in the OCI console.

Decision: Proceed with Oracle Linux as a temporary fallback image for the initial OCI Free Tier development VM.

Reason: Oracle Linux is RHEL-compatible and close enough to the Rocky Linux end-state for early application development, PostgreSQL testing, .NET backend testing, React frontend testing, Podman container testing, and deployment runbook development.

The end-state production target remains Rocky Linux unless a future technical or operational decision changes that direction.

## 4. Recommended Initial OCI Resource Plan

| Resource | Create Now? | Reason |
|---|---|---|
| Compute VM Instance | Yes | Needed to host the application, PostgreSQL, reverse proxy, and containers |
| Managed Oracle Autonomous Database | No | Not the target database; would move the design away from PostgreSQL |
| Object Storage Bucket | Later | Useful later for backups, but not needed before the VM exists |
| Load Balancer | No for MVP | A single VM and reverse proxy is enough for early build/testing |
| VCN/Subnet | Yes | Required network foundation for the VM |
| Public IP | Yes for test | Needed for SSH and web testing unless using private tunnel |

## 5. Target Test Architecture

```text
User Browser
   ↓
Cloudflare DNS / Optional Proxy
   ↓
OCI Public IP
   ↓
Oracle Linux VM for initial test
   ↓
Caddy or NGINX
   ↓
React Frontend + .NET API
   ↓
PostgreSQL
```

End-state architecture remains:

```text
User Browser
   ↓
Cloudflare DNS / Optional Proxy
   ↓
Rocky Linux Server
   ↓
Caddy or NGINX
   ↓
React Frontend + .NET API
   ↓
PostgreSQL
```

## 6. VM Creation Checklist

When creating the VM, capture the following values:

| Item | Value |
|---|---|
| OCI Region | US West (San Jose) initially selected |
| Compartment | TBD |
| VM Name | Suggested: ptp-dev-01 |
| Shape | Always Free eligible compute shape, if available |
| OS Image | Oracle Linux for initial OCI fallback; Rocky Linux remains end-state target |
| Public IP | TBD |
| SSH Username | TBD based on image |
| SSH Key | Do not store private key in GitHub |
| Boot Volume Size | TBD |
| VCN | TBD |
| Subnet | TBD |
| Ports Open | 22 initially; 80/443 when web testing begins |

## 7. Database Decision

For the MVP and early test environment:

- Use PostgreSQL.
- Run PostgreSQL on the VM or in a Podman container.
- Do not create Oracle Autonomous Database for the core application database.
- Do not expose PostgreSQL publicly.
- Back up PostgreSQL using documented backup scripts.

## 8. Security Notes

- Never commit SSH private keys to GitHub.
- Never commit database passwords to GitHub.
- Never commit Microsoft Entra client secrets to GitHub.
- Restrict SSH access where possible.
- Keep database ports private.
- Use HTTPS for the web application once a domain/subdomain is available.

## 9. Next Steps After VM Creation

After the VM is created, document:

1. Public IP address.
2. OS image selected.
3. SSH username.
4. VM shape.
5. VCN/subnet created.
6. Open ports.
7. Whether DNS is pointed to the VM.

Then update:

- `docs/05-rocky-linux-setup-runbook.md`
- `docs/00-running-implementation-document.md`
- `deployment/rocky-linux/`
