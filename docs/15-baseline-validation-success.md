# Baseline Validation Success

## Purpose

This document records the successful baseline validation of the initial OCI development VM.

## Validation Summary

The baseline development tools were successfully installed and validated on the OCI Oracle Linux development VM.

## Confirmed Tool Versions

| Tool | Version |
|---|---|
| git | 2.47.3 |
| podman | 5.6.0 |
| buildah | 1.41.8 |
| skopeo | 1.20.0 |
| jq | 1.6 |
| curl | 7.76.1 |

## Confirmed Memory and Swap

| Resource | Value |
|---|---|
| Memory | Approximately 945 MiB total observed after setup |
| Swap | Approximately 2.5 GiB total |

## Firewall Status

`firewalld` was enabled and SSH was confirmed as an allowed service.

Active firewall zone:

```text
public
```

Allowed services:

```text
dhcpv6-client ssh
```

## Application Directory Structure

The following directory structure was created:

```text
/opt/project-time-platform/
├── app
├── backups
├── config
├── data
├── logs
└── scripts
```

Ownership was set to:

```text
opc:opc
```

## Next Step

The next step is to configure secure GitHub repository access from the VM and clone the private repository into:

```text
/opt/project-time-platform/app
```

After the repository is cloned, continue with PostgreSQL setup.
