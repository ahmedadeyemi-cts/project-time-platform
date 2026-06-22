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

## 5. Current SSH Troubleshooting Example

Observed errors:

```text
Warning: Identity file /ssh-key-2026-06-21.key not accessible: No such file or directory.
Warning: Identity file /private_key not accessible: No such file or directory.
Warning: Identity file /Users/Ahmed.Adeyemi/.ssh/private_key.key not accessible: No such file or directory.
```

Meaning: the SSH command referenced key file paths that did not exist on the local Mac.

Observed error:

```text
WARNING: UNPROTECTED PRIVATE KEY FILE!
Permissions 0644 for '/Users/Ahmed.Adeyemi/.ssh/private_key.key' are too open.
This private key will be ignored.
Load key "/Users/Ahmed.Adeyemi/.ssh/private_key.key": bad permissions
```

Meaning: the key file exists, but macOS/OpenSSH rejected it because other users may be able to read it.

Fix:

```bash
chmod 600 ~/.ssh/private_key.key
ssh -i ~/.ssh/private_key.key opc@167.234.223.32
```

If the key is still in the Downloads folder, first move it into `.ssh`:

```bash
mkdir -p ~/.ssh
mv ~/Downloads/private_key.key ~/.ssh/private_key.key
chmod 600 ~/.ssh/private_key.key
ssh -i ~/.ssh/private_key.key opc@167.234.223.32
```

## 6. SSH from Windows

Use one of the following:

- Windows Terminal / PowerShell with OpenSSH
- PuTTY
- MobaXterm

PowerShell format:

```powershell
ssh -i C:\Path\To\private_key opc@PUBLIC_IP_ADDRESS
```

## 7. OCI Console Access Notes

The VM does not provide a normal graphical desktop interface by default. Access is expected through SSH command line.

If SSH fails, use OCI's console connection or Cloud Shell options only for troubleshooting.

## 8. Troubleshooting SSH

If SSH does not connect, validate:

1. Instance state is `Running`.
2. Public IPv4 address exists.
3. Correct username is used: `opc` for Oracle Linux.
4. Correct private key is used.
5. Port 22 is open in the OCI VCN security list or network security group.
6. Local network is not blocking outbound SSH.
7. The VM was created with the matching SSH public key.

## 9. Post-Login Validation Commands

After SSH succeeds, run:

```bash
cat /etc/os-release
whoami
uname -a
```

Document the output in the implementation notes before continuing with package installation.

## 10. Security Notes

- Do not commit private SSH keys to GitHub.
- Do not paste private SSH keys into chat.
- Do not enable password SSH login.
- Keep SSH access restricted where possible.
- Do not expose PostgreSQL publicly.
