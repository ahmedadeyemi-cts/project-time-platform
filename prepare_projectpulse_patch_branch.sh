#!/usr/bin/env bash
set -euo pipefail

PATCH_KEY="${1:-}"

case "$PATCH_KEY" in
  c2|019M-C.2|019m-c2)
    BRANCH_NAME="feature/019m-c2-user-admin-local-create-delete"
    TODO_FILE="docs/todo/019m-c2-user-admin-local-create-delete.md"
    TITLE="019M-C.2 User Admin local user create/delete"
    ;;
  d|019M-D|019m-d)
    BRANCH_NAME="feature/019m-d-azure-admin-selective-import-sync-now"
    TODO_FILE="docs/todo/019m-d-azure-admin-selective-import-sync-now.md"
    TITLE="019M-D Azure Admin selective import UI + Sync Now"
    ;;
  ai|019M-E|019m-e)
    BRANCH_NAME="feature/019m-e-claude-ai-time-entry-suggestions"
    TODO_FILE="docs/todo/019m-e-claude-ai-time-entry-suggestions.md"
    TITLE="019M-E Claude AI time-entry task suggestions"
    ;;
  *)
    echo "Usage:"
    echo "  ./prepare_projectpulse_patch_branch.sh c2"
    echo "  ./prepare_projectpulse_patch_branch.sh d"
    echo "  ./prepare_projectpulse_patch_branch.sh ai"
    exit 1
    ;;
esac

cd "$(git rev-parse --show-toplevel)"

git fetch origin

if git show-ref --verify --quiet "refs/heads/$BRANCH_NAME"; then
  git checkout "$BRANCH_NAME"
else
  git checkout -b "$BRANCH_NAME"
fi

mkdir -p "$(dirname "$TODO_FILE")"

if [ ! -f "$TODO_FILE" ]; then
  cat > "$TODO_FILE" <<EOF
# $TITLE

## Scope

## Database changes

## Backend changes

## Frontend changes

## Validation commands

\`\`\`bash
dotnet build src/backend/ProjectTime.Api/ProjectTime.Api.csproj
cd src/frontend/project-time-web && npm run build
\`\`\`

## Browser validation

## Notes
EOF
fi

echo "Prepared branch: $BRANCH_NAME"
echo "Todo file: $TODO_FILE"
