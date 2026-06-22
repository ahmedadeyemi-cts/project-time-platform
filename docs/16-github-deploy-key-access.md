# GitHub Deploy Key Access

## Purpose

This document records the successful GitHub deploy key setup from the OCI development VM to the private Project Time Platform repository.

## Status

Deploy key authentication from the OCI VM to GitHub was successful.

## Authentication Test

The following command was used from the VM:

```bash
ssh -i ~/.ssh/github_project_time_platform -T git@github.com
```

Successful result:

```text
Hi ahmedadeyemi-cts/project-time-platform! You've successfully authenticated, but GitHub does not provide shell access.
```

This confirms the VM can authenticate to the private GitHub repository using the deploy key.

## Security Notes

- The private deploy key must remain only on the VM.
- Do not commit the private key to GitHub.
- Do not paste the private key into chat, documentation, email, or tickets.
- The deploy key should remain read-only unless server-side write access is explicitly required later.

## Recommended Clone Location

The repository should be cloned into:

```text
/opt/project-time-platform/app/project-time-platform
```

## Recommended Clone Command

Use the SSH key explicitly:

```bash
cd /opt/project-time-platform/app
GIT_SSH_COMMAND='ssh -i ~/.ssh/github_project_time_platform -o IdentitiesOnly=yes' git clone git@github.com:ahmedadeyemi-cts/project-time-platform.git
```

## Validation Commands

After cloning, run:

```bash
cd /opt/project-time-platform/app/project-time-platform
git remote -v
git status
ls -la
```

## Next Step

After the repository is cloned and validated, continue with PostgreSQL setup and service planning.
