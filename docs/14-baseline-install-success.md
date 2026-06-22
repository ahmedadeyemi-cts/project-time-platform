# Baseline Package Installation Success

## Purpose

This note records the successful baseline package installation on the OCI development VM.

## Issue Encountered

The VM initially terminated DNF package operations with:

```text
Killed
```

This was caused by the very small VM memory allocation.

## Working Resolution

The working resolution was:

1. Add additional swap.
2. Disable large nonessential repositories during baseline installation.
3. Refresh DNF metadata.
4. Install baseline packages again.

## Successful Packages

The install completed successfully for the baseline tools, including:

- git
- podman
- buildah
- skopeo
- wget
- unzip
- tar
- vim
- nano
- firewalld

The install also pulled required container dependencies such as:

- container-selinux
- containers-common
- netavark
- aardvark-dns
- crun
- fuse-overlayfs

## Next Validation Commands

Run:

```bash
git --version
podman --version
buildah --version
skopeo --version
jq --version
curl --version
free -h
```

## Next Setup Steps

After validation:

1. Enable and validate firewalld.
2. Create the application directory structure.
3. Decide the GitHub clone method for the private repository.
4. Proceed to PostgreSQL setup.
