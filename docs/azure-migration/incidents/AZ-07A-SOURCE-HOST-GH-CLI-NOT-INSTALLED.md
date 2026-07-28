# AZ-07A — Source Host GitHub CLI Not Installed

Date: 2026-07-12

## Observed result

The Oracle Linux source host was confirmed and the application repository was found at:

`/opt/project-time-platform/app/project-time-platform-022`

Repository HEAD:

`5a221da29cdfc1134e5d603175b311ff97658b67`

The source checkpoint wrapper could not download the canonical script because the `gh` command is not installed on the source host.

## Impact

- No source files were modified.
- No Git stage, commit, checkout, fetch, reset, stash, or clean action occurred.
- No application or Azure image build started.
- The source-code checkpoint remains pending.

## Recovery

Run the equivalent read-only checkpoint inline on the source host. The inline checkpoint records only Git status, file names, counts, and SHA-256 hashes. It does not print patch content or change the repository.
