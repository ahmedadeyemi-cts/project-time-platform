# OCI SSH Access Runbook

## 1. Purpose

This document explains how to access the initial OCI Linux VM over SSH for the Project Time Platform.

## 2. Current VM Direction

Current image selected during setup:

```text
Oracle-Linux-9.7-2026.06.15-0
```

For Oracle Linux platform images, the default SSH username is expected to be:

```text
opc
```

## 3. Required Information

Before connecting, capture the following from the OCI instance details page:

| Item | Value |
|---|---|
| Instance name | TBD |
| Lifecycle state | Running |
| Public IPv4 address | TBD |
| Image | Oracle-Linux-9.7-2026.06.15-0 |
| Shape | TBD |
| SSH username | opc |
| SSH private key location | Local machine only; do not commit to GitHub |

## 4. SSH from macOS or Linux

Use the following command format:

```bash
ssh -i /path/to/private_key opc@PUBLIC_IP_ADDRESS
```

Example:

```bash
ssh -i ~/.ssh/oci_ptp_dev_01.key opc@123.123.123.123
```

If the key file is too open, fix permissions:

```bash
chmod 600 ~/.ssh/oci_ptp_dev_01.key
```

Then retry the SSH command.

## 5. SSH from Windows

Use one of the following:

- Windows Terminal / PowerShell with OpenSSH
- PuTTY
- MobaXterm

PowerShell format:

```powershell
ssh -i C:\Path\To\private_key opc@PUBLIC_IP_ADDRESS
```

## 6. OCI Console Access Notes

The VM does not provide a normal graphical desktop interface by default. Access is expected through SSH command line.

If SSH fails, use OCI's console connection or Cloud Shell options only for troubleshooting.

## 7. Troubleshooting SSH

If SSH does not connect, validate:

1. Instance state is `Running`.
2. Public IPv4 address exists.
3. Correct username is used: `opc` for Oracle Linux.
4. Correct private key is used.
5. Port 22 is open in the OCI VCN security list or network security group.
6. Local network is not blocking outbound SSH.
7. The VM was created with the matching SSH public key.

## 8. Security Notes

- Do not commit private SSH keys to GitHub.
- Do not paste private SSH keys into chat.
- Do not enable password SSH login.
- Keep SSH access restricted where possible.
- Do not expose PostgreSQL publicly.
