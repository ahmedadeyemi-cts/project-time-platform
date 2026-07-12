# Cloud Shell GitHub Reauthentication Without Closing the Interactive Shell

## Purpose

Recover private GitHub repository access after Azure Cloud Shell loses GitHub CLI authentication, without using top-level `exit` statements that terminate the interactive session.

## Procedure

1. Run `gh auth status`.
2. When authentication is missing, run `gh auth login --hostname github.com --git-protocol https --web` and complete the device/browser authorization.
3. Run `gh auth status` again.
4. Verify private repository access with:

   `gh api repos/ahmedadeyemi-cts/project-time-platform --jq '.full_name'`

5. Download the canonical file with the GitHub contents API and validate it with `bash -n`.
6. Invoke the file with `bash "$LOCAL_SCRIPT"` so script exits remain inside the child process.

## Safety rule

Do not paste `exit`, `logout`, or `exec` into the interactive Cloud Shell control block.
