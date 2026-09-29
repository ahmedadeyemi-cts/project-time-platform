> Privileged downloads must use a reviewed 40-character commit and a SHA-256 from an independently reviewed checkpoint. Set `PHD_REVIEWED_SCRIPT_COMMIT` and `PHD_REVIEWED_SCRIPT_SHA256` before running a resume wrapper. The wrappers reject missing identities and mismatched bytes before enabling a privileged action. `bash -n` checks syntax; it does not establish integrity. Never execute a download selected by a branch name.

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
REVIEWED_COMMIT="${PHD_REVIEWED_SCRIPT_COMMIT:?set reviewed commit}"
REVIEWED_SHA256="${PHD_REVIEWED_SCRIPT_SHA256:?set reviewed SHA-256}"
[[ "$REVIEWED_COMMIT" =~ ^[0-9a-f]{40}$ && "$REVIEWED_SHA256" =~ ^[0-9a-f]{64}$ ]] || exit 1
SCRIPT_PATH="deployment/azure/scripts/az05c2a-private-rocky10-restore-runner.sh"
LOCAL_SCRIPT="/tmp/phd-azure-az05c2a-private-rocky10-restore-runner.sh"

gh api \
    -H "Accept: application/vnd.github.raw+json" \
    "repos/ahmedadeyemi-cts/project-time-platform/contents/${SCRIPT_PATH}?ref=${REVIEWED_COMMIT}" \
    > "$LOCAL_SCRIPT"

printf '%s  %s\n' "$REVIEWED_SHA256" "$LOCAL_SCRIPT" | sha256sum --check --status || exit 1
chmod 700 "$LOCAL_SCRIPT"
bash -n "$LOCAL_SCRIPT"
```

## Security notes

- Do not paste GitHub tokens into shell commands, chat, logs, or repository files.
- Browser authentication is preferable to manually placing a PAT in Cloud Shell.
- Because Cloud Shell is ephemeral, authentication may need to be repeated in a later session.
- No Azure resource should be created until its reviewed commit and digest are verified and its syntax passes `bash -n`.
