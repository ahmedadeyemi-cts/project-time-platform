#!/usr/bin/env bash
set -Eeuo pipefail

EXPECTED_BRANCH="source/work-register-billing-lifecycle-20260712"
EXPECTED_COMMIT="9cf36c2ab28c5eb00bd379bd63b2c8e07cd3af84"
BASE_DIR="${HOME}/project-health-dashboard-source-checkpoint"
LOG_DIR="${BASE_DIR}/logs"
CONFIG_DIR="${BASE_DIR}/config"
STAMP="$(date -u +%Y%m%dT%H%M%SZ)"
LOG="${LOG_DIR}/az08a-containerization-preflight-${STAMP}.log"
STATE_FILE="${CONFIG_DIR}/az08a-containerization-preflight.env"

mkdir -p "$LOG_DIR" "$CONFIG_DIR"

section() {
    echo
    echo "============================================================"
    echo "$1"
    echo "============================================================"
}

{
    section "AZ-08A - Read-Only Containerization Preflight"
    echo "TIME=$(date -u -Is)"
    echo "READ_ONLY_SOURCE_INSPECTION=true"
    echo "SOURCE_FILES_MODIFIED=false"
    echo "GIT_WRITE_ACTION_PERFORMED=false"
    echo "CONTAINER_IMAGE_BUILD_STARTED=false"
    echo "CONTAINER_IMAGE_PUSH_STARTED=false"
    echo "AZURE_RESOURCE_CREATION=false"

    ROOT="$(git rev-parse --show-toplevel 2>/dev/null || true)"
    if [ -z "$ROOT" ]; then
        echo "ERROR: Current directory is not inside a Git repository."
        return 1
    fi

    cd "$ROOT"

    BRANCH="$(git branch --show-current)"
    HEAD_SHA="$(git rev-parse HEAD)"
    UPSTREAM="$(git rev-parse --abbrev-ref --symbolic-full-name '@{upstream}' 2>/dev/null || true)"
    STATUS_COUNT="$(git status --short --untracked-files=all | sed '/^$/d' | wc -l | tr -d ' ')"

    echo "SOURCE_REPOSITORY_ROOT=$ROOT"
    echo "SOURCE_BRANCH=${BRANCH:-detached}"
    echo "EXPECTED_SOURCE_BRANCH=$EXPECTED_BRANCH"
    echo "SOURCE_BRANCH_MATCH=$([ "$BRANCH" = "$EXPECTED_BRANCH" ] && echo yes || echo no)"
    echo "SOURCE_HEAD=$HEAD_SHA"
    echo "EXPECTED_SOURCE_HEAD=$EXPECTED_COMMIT"
    echo "SOURCE_HEAD_MATCH=$([ "$HEAD_SHA" = "$EXPECTED_COMMIT" ] && echo yes || echo no)"
    echo "SOURCE_UPSTREAM=${UPSTREAM:-not-configured}"
    echo "SOURCE_STATUS_ENTRY_COUNT=$STATUS_COUNT"
    echo "SOURCE_WORKTREE_CLEAN=$([ "$STATUS_COUNT" = "0" ] && echo true || echo false)"

    if [ -f /etc/os-release ]; then
        . /etc/os-release
        echo "OPERATING_SYSTEM=${PRETTY_NAME:-unknown}"
    fi

    section "Container tooling"

    for tool in docker podman buildah skopeo crane az gh dotnet node npm python3; do
        if command -v "$tool" >/dev/null 2>&1; then
            echo "TOOL[$tool]=present"
            case "$tool" in
                docker) docker --version 2>/dev/null | sed 's/^/TOOL_VERSION[docker]=/' || true ;;
                podman) podman --version 2>/dev/null | sed 's/^/TOOL_VERSION[podman]=/' || true ;;
                buildah) buildah --version 2>/dev/null | sed 's/^/TOOL_VERSION[buildah]=/' || true ;;
                skopeo) skopeo --version 2>/dev/null | sed 's/^/TOOL_VERSION[skopeo]=/' || true ;;
                az) az version --query '"azure-cli"' -o tsv 2>/dev/null | sed 's/^/TOOL_VERSION[az]=/' || true ;;
                gh) gh --version 2>/dev/null | head -n 1 | sed 's/^/TOOL_VERSION[gh]=/' || true ;;
                dotnet) dotnet --version 2>/dev/null | sed 's/^/TOOL_VERSION[dotnet]=/' || true ;;
                node) node --version 2>/dev/null | sed 's/^/TOOL_VERSION[node]=/' || true ;;
                npm) npm --version 2>/dev/null | sed 's/^/TOOL_VERSION[npm]=/' || true ;;
                python3) python3 --version 2>/dev/null | sed 's/^/TOOL_VERSION[python3]=/' || true ;;
            esac
        else
            echo "TOOL[$tool]=absent"
        fi
    done

    section "Build inputs"

    BACKEND_PROJECT="src/backend/ProjectTime.Api/ProjectTime.Api.csproj"
    BACKEND_PROGRAM="src/backend/ProjectTime.Api/Program.cs"
    FRONTEND_ROOT="src/frontend/project-time-web"
    FRONTEND_PACKAGE="$FRONTEND_ROOT/package.json"
    FRONTEND_LOCK="$FRONTEND_ROOT/package-lock.json"
    VITE_CONFIG="$FRONTEND_ROOT/vite.config.js"
    LOCAL_FRONTEND_PROXY="deployment/rocky-linux/serve-frontend-local.py"

    for path in \
        "$BACKEND_PROJECT" \
        "$BACKEND_PROGRAM" \
        "$FRONTEND_PACKAGE" \
        "$FRONTEND_LOCK" \
        "$VITE_CONFIG" \
        "$LOCAL_FRONTEND_PROXY"; do
        echo "BUILD_INPUT[$path]=$([ -f "$path" ] && echo present || echo missing)"
    done

    DOCKERFILE_COUNT="$(find . -type f \( -name Dockerfile -o -name 'Dockerfile.*' -o -name Containerfile -o -name 'Containerfile.*' \) -not -path './.git/*' | wc -l | tr -d ' ')"
    echo "EXISTING_CONTAINERFILE_COUNT=$DOCKERFILE_COUNT"
    find . -type f \( -name Dockerfile -o -name 'Dockerfile.*' -o -name Containerfile -o -name 'Containerfile.*' \) -not -path './.git/*' -print | LC_ALL=C sort | sed 's#^./#EXISTING_CONTAINERFILE=#'

    python3 - "$ROOT" <<'PY'
import json
import re
import sys
from pathlib import Path

root = Path(sys.argv[1])
program = root / "src/backend/ProjectTime.Api/Program.cs"
project = root / "src/backend/ProjectTime.Api/ProjectTime.Api.csproj"
package = root / "src/frontend/project-time-web/package.json"
vite = root / "src/frontend/project-time-web/vite.config.js"
proxy = root / "deployment/rocky-linux/serve-frontend-local.py"


def read(path):
    return path.read_text(encoding="utf-8", errors="replace") if path.is_file() else ""

program_text = read(program)
project_text = read(project)
vite_text = read(vite)
proxy_text = read(proxy)

frameworks = sorted(set(re.findall(r"<TargetFramework>([^<]+)</TargetFramework>", project_text)))
print(f"BACKEND_TARGET_FRAMEWORK_COUNT={len(frameworks)}")
for value in frameworks:
    print(f"BACKEND_TARGET_FRAMEWORK={value}")

routes = re.findall(r'app\.Map(?:Get|Post|Put|Patch|Delete)\s*\(\s*"([^"]+)"', program_text)
print(f"BACKEND_ROUTE_COUNT={len(routes)}")
print(f"BACKEND_HEALTH_ROUTE_PRESENT={'yes' if '/health' in routes else 'no'}")
print(f"BACKEND_ROOT_ROUTE_PRESENT={'yes' if '/' in routes else 'no'}")
print(f"BACKEND_APP_RUN_PRESENT={'yes' if re.search(r'\bapp\.Run\s*\(', program_text) else 'no'}")
print(f"BACKEND_FORWARDED_HEADERS_PRESENT={'yes' if 'UseForwardedHeaders' in program_text else 'no'}")
print(f"BACKEND_HTTPS_REDIRECTION_PRESENT={'yes' if 'UseHttpsRedirection' in program_text else 'no'}")

patterns = [
    r'Environment\.GetEnvironmentVariable\s*\(\s*"([A-Za-z0-9_:\-.]+)"',
    r'GetEnvironmentVariable\s*\(\s*"([A-Za-z0-9_:\-.]+)"',
    r'(?:builder\.)?Configuration\s*\[\s*"([A-Za-z0-9_:\-.]+)"\s*\]',
    r'GetConnectionString\s*\(\s*"([A-Za-z0-9_:\-.]+)"',
]
keys = set()
for pattern in patterns:
    keys.update(re.findall(pattern, program_text))
print(f"BACKEND_CONFIGURATION_KEY_COUNT={len(keys)}")
for key in sorted(keys):
    print(f"BACKEND_CONFIGURATION_KEY={key}")

print(f"DATABASE_CONFIG_TYPE_PRESENT={'yes' if 'DatabaseConfig' in program_text else 'no'}")
print(f"DATABASE_CONFIG_FROM_ENVIRONMENT_PRESENT={'yes' if 'DatabaseConfig.FromEnvironment' in program_text else 'no'}")
print(f"NPGSQL_CONNECTION_PRESENT={'yes' if 'NpgsqlConnection' in program_text else 'no'}")

if package.is_file():
    data = json.loads(package.read_text(encoding="utf-8"))
    scripts = sorted((data.get("scripts") or {}).keys())
else:
    scripts = []
print(f"FRONTEND_SCRIPT_COUNT={len(scripts)}")
for script in scripts:
    print(f"FRONTEND_SCRIPT={script}")

vite_targets = sorted(set(re.findall(r"target:\s*['\"]([^'\"]+)['\"]", vite_text)))
print(f"VITE_PROXY_TARGET_COUNT={len(vite_targets)}")
for target in vite_targets:
    print(f"VITE_PROXY_TARGET={target}")

backend_host = re.search(r'^BACKEND_HOST\s*=\s*["\']([^"\']+)', proxy_text, re.MULTILINE)
backend_port = re.search(r'^BACKEND_PORT\s*=\s*([0-9]+)', proxy_text, re.MULTILINE)
listen_port = re.search(r'add_argument\("--port"[^\n]*default=([0-9]+)', proxy_text)
print(f"LOCAL_PROXY_BACKEND_HOST={backend_host.group(1) if backend_host else 'not-detected'}")
print(f"LOCAL_PROXY_BACKEND_PORT={backend_port.group(1) if backend_port else 'not-detected'}")
print(f"LOCAL_FRONTEND_LISTEN_PORT={listen_port.group(1) if listen_port else 'not-detected'}")
print(f"LOCAL_PROXY_API_SUPPORT={'yes' if 'startswith(\"/api/\")' in proxy_text else 'no'}")
print(f"LOCAL_PROXY_HEALTH_SUPPORT={'yes' if 'parsed.path == \"/health\"' in proxy_text else 'no'}")
PY

    section "Containerization decision"

    BRANCH_MATCH="$([ "$BRANCH" = "$EXPECTED_BRANCH" ] && echo yes || echo no)"
    HEAD_MATCH="$([ "$HEAD_SHA" = "$EXPECTED_COMMIT" ] && echo yes || echo no)"
    CLEAN_MATCH="$([ "$STATUS_COUNT" = "0" ] && echo yes || echo no)"

    if [ "$BRANCH_MATCH" = yes ] && [ "$HEAD_MATCH" = yes ] && [ "$CLEAN_MATCH" = yes ]; then
        RESULT="READY_FOR_CONTAINERFILE_DESIGN"
        NEXT_ACTION="ADD_API_AND_FRONTEND_CONTAINERFILES_THEN_VALIDATE_WITH_ACR_BUILD"
    else
        RESULT="SOURCE_STATE_REVIEW_REQUIRED"
        NEXT_ACTION="DO_NOT_CREATE_CONTAINERFILES_UNTIL_SOURCE_STATE_MATCHES"
    fi

    cat > "$STATE_FILE" <<EOF
SOURCE_REPOSITORY_ROOT=$ROOT
SOURCE_BRANCH=$BRANCH
SOURCE_HEAD=$HEAD_SHA
SOURCE_STATUS_ENTRY_COUNT=$STATUS_COUNT
CONTAINERIZATION_PREFLIGHT_RESULT=$RESULT
CONTAINERIZATION_NEXT_ACTION=$NEXT_ACTION
CONTAINERIZATION_PREFLIGHT_AT=$(date -u -Is)
EOF
    chmod 600 "$STATE_FILE"

    echo "CONTAINERIZATION_PREFLIGHT_RESULT=$RESULT"
    echo "SOURCE_IMAGE_BUILD_ALLOWED=false"
    echo "NEXT_ACTION=$NEXT_ACTION"
    echo "CONTAINERIZATION_STATE_FILE=$STATE_FILE"

    echo
    echo "************************************************************"
    echo "CONTAINERIZATION PREFLIGHT COMPLETE"
    echo "************************************************************"

} 2>&1 | tee "$LOG"

echo
echo "Preflight log: $LOG"
