#!/usr/bin/env bash
set -euo pipefail

PATCH_KEY="${1:-}"

case "$PATCH_KEY" in
  c2|019M-C.2|019m-c2)
    BRANCH_NAME="feature/019m-c2-user-admin-local-create-delete"
    COMMIT_MESSAGE="019M-C.2 User Admin local user create/delete"
    ;;
  d|019M-D|019m-d)
    BRANCH_NAME="feature/019m-d-azure-admin-selective-import-sync-now"
    COMMIT_MESSAGE="019M-D Azure Admin selective import UI + Sync Now"
    ;;
  ai|019M-E|019m-e)
    BRANCH_NAME="feature/019m-e-claude-ai-time-entry-suggestions"
    COMMIT_MESSAGE="019M-E Claude AI time-entry task suggestions"
    ;;
  *)
    echo "Usage:"
    echo "  ./push_projectpulse_patch.sh c2"
    echo "  ./push_projectpulse_patch.sh d"
    echo "  ./push_projectpulse_patch.sh ai"
    exit 1
    ;;
esac

echo "============================================================"
echo "Project Health Dashboard Git Push Helper"
echo "Patch:  $COMMIT_MESSAGE"
echo "Branch: $BRANCH_NAME"
echo "============================================================"

git rev-parse --is-inside-work-tree >/dev/null

echo
echo "Fetching latest remote state..."
git fetch origin

echo
echo "Current status:"
git status --short

echo
read -r -p "Continue and create/use branch '$BRANCH_NAME'? [y/N] " CONFIRM
if [[ ! "$CONFIRM" =~ ^[Yy]$ ]]; then
  echo "Cancelled."
  exit 0
fi

if git show-ref --verify --quiet "refs/heads/$BRANCH_NAME"; then
  git checkout "$BRANCH_NAME"
else
  git checkout -b "$BRANCH_NAME"
fi

echo
echo "Running backend build..."
dotnet build src/backend/ProjectTime.Api/ProjectTime.Api.csproj

echo
if [ -d "src/frontend/project-time-web" ]; then
  echo "Running frontend build..."
  cd src/frontend/project-time-web
  if [ -f "package-lock.json" ]; then
    npm ci
  else
    npm install
  fi
  npm run build
  cd ../../..
fi

echo
echo "Git status before commit:"
git status --short

if [ -z "$(git status --short)" ]; then
  echo "No changes to commit."
  exit 0
fi

echo
read -r -p "Stage all changes and commit? [y/N] " COMMIT_CONFIRM
if [[ ! "$COMMIT_CONFIRM" =~ ^[Yy]$ ]]; then
  echo "Cancelled before commit."
  exit 0
fi

git add -A
git commit -m "$COMMIT_MESSAGE"

echo
read -r -p "Push branch to origin? [y/N] " PUSH_CONFIRM
if [[ ! "$PUSH_CONFIRM" =~ ^[Yy]$ ]]; then
  echo "Committed locally but not pushed."
  exit 0
fi

git push -u origin "$BRANCH_NAME"

echo
echo "Done. Pushed: $BRANCH_NAME"
