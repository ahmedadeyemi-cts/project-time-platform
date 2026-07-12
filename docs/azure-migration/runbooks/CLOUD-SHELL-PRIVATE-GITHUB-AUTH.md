# Private GitHub Access from Azure Cloud Shell

Azure Cloud Shell sessions are ephemeral and do not automatically contain the user's GitHub SSH private key. Cloning a private repository with an SSH URL can therefore fail with `Permission denied (publickey)`.

## Approved approach

Use GitHub CLI browser authentication and fetch only the required file through the GitHub API.

```bash
if ! command -v gh >/dev/null 2>&1; then
    echo "ERROR: GitHub CLI (gh) is not installed in this Cloud Shell session."
    exit 1
fi

gh auth status --hostname github.com >/dev/null 2>&1 || \
    gh auth login \
        --hostname github.com \
        --git-protocol https \
        --web
```

Then fetch a private repository file without cloning:

```bash
BRANCH="azure-migration/project-health-dashboard-foundation"
SCRIPT_PATH="deployment/azure/scripts/az05c2a-private-rocky10-restore-runner.sh"
LOCAL_SCRIPT="/tmp/phd-azure-az05c2a-private-rocky10-restore-runner.sh"

gh api \
    -H "Accept: application/vnd.github.raw+json" \
    "repos/ahmedadeyemi-cts/project-time-platform/contents/${SCRIPT_PATH}?ref=${BRANCH}" \
    > "$LOCAL_SCRIPT"

chmod +x "$LOCAL_SCRIPT"
bash -n "$LOCAL_SCRIPT"
```

## Security notes

- Do not paste GitHub tokens into shell commands, chat, logs, or repository files.
- Browser authentication is preferable to manually placing a PAT in Cloud Shell.
- Because Cloud Shell is ephemeral, authentication may need to be repeated in a later session.
- No Azure resource should be created until the fetched script passes `bash -n`.
