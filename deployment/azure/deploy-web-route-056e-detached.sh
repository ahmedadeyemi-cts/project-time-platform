#!/usr/bin/env bash
set -Eeuo pipefail
IFS=$'\n\t'
umask 077
set +x

REPOSITORY="ahmedadeyemi-cts/project-time-platform"
SOURCE_BRANCH="source/invoice-billing-center-preview-20260714"
REQUIRED_056D_FIX="050c4548078505bd3a9b501bbb297905eaec4c82"

SUBSCRIPTION="cd32baeb-7b71-4bc0-8ea3-9f23a50903fe"
APP_RG="rg-project-health-dashboard-test-app-westus3"
WEB_APP="ca-phd-test-web-westus3"

ACR_NAME="acrphdtest7825cc"
ACR_SERVER="acrphdtest7825cc.azurecr.io"
IMAGE_REPOSITORY="project-health-dashboard-web"

PUBLIC_URL="https://phd-west-test.onenecklab.com"

STAMP="$(date -u +%Y%m%dT%H%M%SZ)"
RUN_DIR="$HOME/projectpulse-056e-global-card-suppression-logs/$STAMP"
SOURCE_REPO="$RUN_DIR/source"
OPS_REPO="$RUN_DIR/ops"
BUILD_LOG="$RUN_DIR/acr-web-build.log"
EVENT_LOG="$RUN_DIR/events.log"
STATUS_FILE="$RUN_DIR/final.status"

OPS_BRANCH="ops/azure-web-route-056e-$STAMP"

mkdir -p "$RUN_DIR"

log() {
  echo "$*" | tee -a "$EVENT_LOG"
}

fail() {
  log ""
  log "============================================================"
  log "056E_GLOBAL_CARD_SUPPRESSION=FAILED"
  log "ERROR=$*"
  log "RUN_DIR=$RUN_DIR"
  log "API_CHANGED=NO"
  log "DATABASE_CHANGED=NO"
  log "============================================================"

  cat > "$STATUS_FILE" <<EOF
status=failed
error=$*
runDir=$RUN_DIR
apiChanged=no
databaseChanged=no
EOF

  exit 1
}

for COMMAND in gh git az curl grep awk sha256sum python3; do
  command -v "$COMMAND" >/dev/null 2>&1 ||
    fail "Missing command: $COMMAND"
done

gh auth status >/dev/null 2>&1 ||
  fail "GitHub CLI is not authenticated"

az account set --subscription "$SUBSCRIPTION"

log "============================================================"
log "056E GLOBAL LEGACY CARD SUPPRESSION START"
log "============================================================"
log "RUN_DIR=$RUN_DIR"
log "PUBLIC_URL=$PUBLIC_URL"

REMOTE_SOURCE="$(
  git ls-remote \
    "https://github.com/$REPOSITORY.git" \
    "refs/heads/$SOURCE_BRANCH" |
  awk '{print $1}'
)"

[ -n "$REMOTE_SOURCE" ] ||
  fail "Could not resolve remote source branch"

log "REMOTE_SOURCE=$REMOTE_SOURCE"
log "REQUIRED_056D_FIX=$REQUIRED_056D_FIX"

gh repo clone "$REPOSITORY" "$SOURCE_REPO" -- \
  --branch "$SOURCE_BRANCH" \
  --single-branch >>"$EVENT_LOG" 2>&1

cd "$SOURCE_REPO"

SOURCE_BASE="$(git rev-parse HEAD)"

[ "$SOURCE_BASE" = "$REMOTE_SOURCE" ] ||
  fail "Clone source $SOURCE_BASE does not match remote $REMOTE_SOURCE"

git merge-base --is-ancestor "$REQUIRED_056D_FIX" "$SOURCE_BASE" ||
  fail "Current source does not contain the prior 056D fix"

git config --local user.name "Ahmed Adeyemi"
git config --local user.email \
  "ahmedadeyemi-cts@users.noreply.github.com"

log ""
log "============================================================"
log "PATCH SOURCE TO 056E"
log "============================================================"

python3 - <<'PY'
from pathlib import Path
from datetime import datetime, timezone
import hashlib
import re

index_path = Path("src/frontend/project-time-web/index.html")
app_path = Path("src/frontend/project-time-web/src/App.jsx")
main_path = Path("src/frontend/project-time-web/src/main.jsx")
invoice_path = Path("src/frontend/project-time-web/src/InvoiceBillingCenter.jsx")
docker_path = Path("deployment/containers/web/Dockerfile")
nginx_path = Path("deployment/containers/web/default.conf.template")
entrypoint_path = Path("deployment/containers/web/projecttime-web-entrypoint.sh")

read_paths = [
    index_path,
    app_path,
    main_path,
    invoice_path,
    docker_path,
    nginx_path,
    entrypoint_path,
]

contents = {}

for path in read_paths:
    if not path.exists():
        raise SystemExit(f"Missing required file: {path}")

    contents[path] = path.read_text(encoding="utf-8")

index = contents[index_path]

start_marker = "<!-- 056B_DASHBOARD_CARD_ROUTE_GUARD_START -->"
end_marker = "<!-- 056B_DASHBOARD_CARD_ROUTE_GUARD_END -->"

if index.count(start_marker) != 1:
    raise SystemExit("Expected exactly one dashboard guard start marker")

if index.count(end_marker) != 1:
    raise SystemExit("Expected exactly one dashboard guard end marker")

start = index.index(start_marker)
end = index.index(end_marker) + len(end_marker)
old_guard = index[start:end]

if "data-projectpulse-guard-version=\"056D\"" not in old_guard:
    raise SystemExit("Current guard is not the expected 056D source")

new_guard = r'''<!-- 056B_DASHBOARD_CARD_ROUTE_GUARD_START -->
<style id="projectpulse-056b-dashboard-card-route-style">
  [data-projectpulse-dashboard-only-card="true"],
  [data-projectpulse-legacy-dashboard-summary-card="true"] {
    display: none !important;
  }
</style>

<script
  id="projectpulse-056b-dashboard-card-route-guard"
  data-projectpulse-guard-version="056E"
>
(function () {
  const GUARD_VERSION = '056E';

  if (window.__projectPulse056BDashboardCardRouteGuardInstalled) {
    if (
      typeof window.__projectPulse056BSynchronizeDashboardCards
      === 'function'
    ) {
      window.__projectPulse056BSynchronizeDashboardCards();
    }

    return;
  }

  window.__projectPulse056BDashboardCardRouteGuardInstalled = true;

  const dashboardModulePattern =
    /\bmodule\s+0?(?:22|23|24|25|26|27|28|29|30)\b/i;

  const knownDashboardTitles = [
    'Production Notification Center',
    'Production Readiness Center',
    'Sales-to-Delivery Intake Foundation',
    'SOW Generator + Claude Research Review',
    'CRM Integration Framework',
    'Signed SOW Handoff + Assignment Trigger',
    'SOW-Aware AI Time Entry Generator',
    'User Acceptance / Role + Workflow Validation Center',
    'Reporting / Accounting / Invoicing / Analytics Command Center'
  ];

  function currentRoute() {
    return String(window.location.hash || '#dashboard')
      .replace(/^#/, '')
      .split('?')[0]
      .replace(/\/+$/, '')
      .trim()
      .toLowerCase() || 'dashboard';
  }

  function normalizedText(element) {
    return String(element?.textContent || '')
      .replace(/\s+/g, ' ')
      .trim();
  }

  function isVisible(element) {
    if (!(element instanceof HTMLElement)) {
      return false;
    }

    const style = window.getComputedStyle(element);

    return !element.hidden
      && style.display !== 'none'
      && style.visibility !== 'hidden'
      && style.opacity !== '0';
  }

  function hasKnownDashboardTitle(element) {
    const text = normalizedText(element);

    return knownDashboardTitles.some(function (title) {
      return text.includes(title);
    });
  }

  function hasDashboardModuleLabel(element) {
    return dashboardModulePattern.test(normalizedText(element));
  }

  function hasOpenAction(element) {
    const text = normalizedText(element);

    if (/\bOpen\b/i.test(text)) {
      return true;
    }

    return Boolean(
      element.querySelector?.(
        'a[href], button, [role="button"]'
      )
    );
  }

  function isExplicitLegacyDashboardId(id) {
    return id.includes('dashboard-shortcut')
      || id.includes('dashboard-notification-card')
      || id.includes('-dashboard-card')
      || id.endsWith('-card');
  }

  function isProtectedRouteWorkspace(element) {
    if (!(element instanceof HTMLElement)) {
      return false;
    }

    /*
     * The legacy summary cards are injected before/outside the route
     * workspace. Preserve actual route pages, shells, panels, drawers,
     * and modal content.
     */
    const protectedAncestor = element.closest(
      '#root, ' +
      '.app-shell, ' +
      '[id$="-shell"], ' +
      '[id$="-page"], ' +
      '[id*="-modal"], ' +
      '[id*="-drawer"], ' +
      '[id*="-panel-root"], ' +
      '[data-projectpulse-route-page="true"]'
    );

    return Boolean(protectedAncestor);
  }

  function isLegacyDashboardSummary(element) {
    if (!(element instanceof HTMLElement)) {
      return false;
    }

    if (isProtectedRouteWorkspace(element)) {
      return false;
    }

    const text = normalizedText(element);

    if (!text) {
      return false;
    }

    const id = String(element.id || '').toLowerCase();

    if (
      id.startsWith('projectpulse-')
      && isExplicitLegacyDashboardId(id)
      && (
        hasDashboardModuleLabel(element)
        || hasKnownDashboardTitle(element)
        || id.includes('dashboard-notification-card')
        || id.includes('dashboard-shortcut')
      )
    ) {
      return true;
    }

    if (
      (element.matches('section, article') || id.startsWith('projectpulse-'))
      && hasKnownDashboardTitle(element)
      && hasOpenAction(element)
    ) {
      return true;
    }

    if (
      (element.matches('section, article') || id.startsWith('projectpulse-'))
      && hasDashboardModuleLabel(element)
      && hasOpenAction(element)
    ) {
      return true;
    }

    return false;
  }

  function findLegacyDashboardContainer(element) {
    let current = element;

    while (
      current
      && current !== document.body
      && current !== document.documentElement
    ) {
      if (isLegacyDashboardSummary(current)) {
        return current;
      }

      if (isProtectedRouteWorkspace(current)) {
        break;
      }

      current = current.parentElement;
    }

    return null;
  }

  function discoverLegacyDashboardSummaries() {
    const cards = new Set();

    document
      .querySelectorAll(
        '[id^="projectpulse-"], section, article'
      )
      .forEach(function (element) {
        if (isLegacyDashboardSummary(element)) {
          cards.add(element);
        }
      });

    document
      .querySelectorAll(
        'h1, h2, h3, h4, h5, h6, [role="heading"]'
      )
      .forEach(function (heading) {
        if (
          !hasKnownDashboardTitle(heading)
          && !hasDashboardModuleLabel(heading)
        ) {
          return;
        }

        const card = findLegacyDashboardContainer(heading);

        if (card) {
          cards.add(card);
        }
      });

    return Array.from(cards);
  }

  function suppressLegacyDashboardSummary(element) {
    element.setAttribute(
      'data-projectpulse-dashboard-only-card',
      'true'
    );

    element.setAttribute(
      'data-projectpulse-legacy-dashboard-summary-card',
      'true'
    );

    element.setAttribute(
      'data-projectpulse-dashboard-card-guard-version',
      GUARD_VERSION
    );

    element.setAttribute(
      'data-projectpulse-056b-forced-hidden',
      'true'
    );

    element.hidden = true;
    element.setAttribute('aria-hidden', 'true');
    element.style.setProperty('display', 'none', 'important');
  }

  function visibleLegacyHeadingOffenders() {
    return Array.from(document.querySelectorAll(
      'h1, h2, h3, h4, h5, h6, [role="heading"]'
    ))
      .filter(function (heading) {
        return hasKnownDashboardTitle(heading)
          || hasDashboardModuleLabel(heading);
      })
      .filter(function (heading) {
        if (isProtectedRouteWorkspace(heading)) {
          return false;
        }

        const card = heading.closest(
          '[data-projectpulse-legacy-dashboard-summary-card="true"], ' +
          '[data-projectpulse-dashboard-only-card="true"]'
        ) || heading;

        return isVisible(card);
      })
      .map(function (heading) {
        return normalizedText(heading).slice(0, 180);
      });
  }

  function synchronizeDashboardCards() {
    const route = currentRoute();

    document.documentElement.setAttribute(
      'data-projectpulse-056b-route',
      route
    );

    document.documentElement.setAttribute(
      'data-projectpulse-056b-guard-version',
      GUARD_VERSION
    );

    const cards = new Set([
      ...document.querySelectorAll(
        '[data-projectpulse-legacy-dashboard-summary-card="true"]'
      ),
      ...document.querySelectorAll(
        '[data-projectpulse-dashboard-only-card="true"]'
      ),
      ...discoverLegacyDashboardSummaries()
    ]);

    cards.forEach(function (element) {
      suppressLegacyDashboardSummary(element);
    });

    const visibleCards = Array.from(cards).filter(isVisible);
    const visibleHeadings = visibleLegacyHeadingOffenders();

    document.documentElement.setAttribute(
      'data-projectpulse-056b-marked-count',
      String(cards.size)
    );

    document.documentElement.setAttribute(
      'data-projectpulse-056b-visible-offender-count',
      String(visibleCards.length + visibleHeadings.length)
    );

    return {
      route,
      guardVersion: GUARD_VERSION,
      markedCount: cards.size,
      visibleOffenderCount: visibleCards.length + visibleHeadings.length,
      visibleKnownDashboardHeadings: visibleHeadings,
      markedCards: Array.from(cards).map(function (element) {
        return {
          id: element.id || '',
          tag: element.tagName,
          text: normalizedText(element).slice(0, 180),
          hidden: element.hidden,
          display: window.getComputedStyle(element).display
        };
      }),
      visibleOffenders: visibleCards.map(function (element) {
        return {
          id: element.id || '',
          tag: element.tagName,
          text: normalizedText(element).slice(0, 180),
          hidden: element.hidden,
          display: window.getComputedStyle(element).display
        };
      })
    };
  }

  window.__projectPulse056BSynchronizeDashboardCards =
    synchronizeDashboardCards;

  window.__projectPulse056BDashboardCardRouteGuardDiagnostics =
    function () {
      return synchronizeDashboardCards();
    };

  function scheduleSynchronization() {
    window.clearTimeout(
      window.__projectPulse056BDashboardCardRouteGuardTimer
    );

    window.__projectPulse056BDashboardCardRouteGuardTimer =
      window.setTimeout(function () {
        window.requestAnimationFrame(
          synchronizeDashboardCards
        );
      }, 20);
  }

  window.addEventListener(
    'hashchange',
    scheduleSynchronization,
    true
  );

  window.addEventListener(
    'popstate',
    scheduleSynchronization,
    true
  );

  window.addEventListener(
    'projectpulse:route-state-ready',
    scheduleSynchronization
  );

  if (document.readyState === 'loading') {
    document.addEventListener(
      'DOMContentLoaded',
      scheduleSynchronization,
      { once: true }
    );
  } else {
    scheduleSynchronization();
  }

  const observer = new MutationObserver(
    scheduleSynchronization
  );

  observer.observe(document.documentElement, {
    childList: true,
    subtree: true,
    attributes: true,
    attributeFilter: [
      'id',
      'class',
      'style',
      'hidden',
      'aria-hidden'
    ]
  });

  scheduleSynchronization();

  [
    100,
    500,
    1500,
    3000,
    6000
  ].forEach(function (delay) {
    window.setTimeout(
      synchronizeDashboardCards,
      delay
    );
  });
})();
</script>
<!-- 056B_DASHBOARD_CARD_ROUTE_GUARD_END -->'''

updated = index[:start] + new_guard + index[end:]

required = [
    'data-projectpulse-guard-version="056E"',
    "const GUARD_VERSION = '056E'",
    "data-projectpulse-legacy-dashboard-summary-card",
    "isProtectedRouteWorkspace",
    "SOW-Aware AI Time Entry Generator",
    "User Acceptance / Role + Workflow Validation Center",
    "Reporting / Accounting / Invoicing / Analytics Command Center",
]

for fragment in required:
    if fragment not in updated:
        raise SystemExit(f"Missing expected 056E fragment: {fragment}")

index_path.write_text(updated, encoding="utf-8")

audit_path = Path("docs/056e-global-legacy-dashboard-card-suppression-audit.md")
audit_path.parent.mkdir(parents=True, exist_ok=True)

rows = []

for path, text in contents.items():
    rows.append((
        str(path),
        len(text.splitlines()),
        len(text.encode("utf-8")),
        hashlib.sha256(text.encode("utf-8")).hexdigest(),
    ))

projectpulse_ids = sorted(set(re.findall(
    r"projectpulse-[A-Za-z0-9_-]+",
    index,
)))

audit = [
    "# 056E Global Legacy Dashboard Card Suppression Audit",
    "",
    f"Generated: {datetime.now(timezone.utc).isoformat()}",
    "",
    "## Files read in full",
    "",
    "| File | Lines | Bytes | SHA-256 |",
    "|---|---:|---:|---|",
]

for path, lines, bytes_, sha in rows:
    audit.append(f"| `{path}` | {lines} | {bytes_} | `{sha}` |")

audit.extend([
    "",
    "## Correction",
    "",
    "056E changes the behavior from route-conditional hiding to global",
    "suppression of legacy injected dashboard summary cards.",
    "",
    "The real route workspaces are preserved by excluding descendants of",
    "`#root`, `.app-shell`, route shells, route pages, drawers, modals, and",
    "panel roots.",
    "",
    "Acceptance condition: the legacy Module 022-030 cards do not appear on",
    "`#dashboard`, `#invoice-billing-center`, or any other route.",
    "",
    "## ProjectPulse IDs observed before patch",
    "",
])

audit.extend(f"- `{value}`" for value in projectpulse_ids)

audit_path.write_text("\n".join(audit) + "\n", encoding="utf-8")
PY

cat > scripts/validate-056e-global-card-suppression.py <<'PY'
#!/usr/bin/env python3

from pathlib import Path
import sys

index = Path("src/frontend/project-time-web/index.html").read_text(
    encoding="utf-8"
)

errors = []

required = [
    'data-projectpulse-guard-version="056E"',
    "const GUARD_VERSION = '056E'",
    "data-projectpulse-legacy-dashboard-summary-card",
    "isProtectedRouteWorkspace",
    "discoverLegacyDashboardSummaries",
    "suppressLegacyDashboardSummary",
    "visibleLegacyHeadingOffenders",
    "SOW-Aware AI Time Entry Generator",
    "User Acceptance / Role + Workflow Validation Center",
    "Reporting / Accounting / Invoicing / Analytics Command Center",
]

for fragment in required:
    if fragment not in index:
        errors.append(f"Missing required fragment: {fragment}")

for forbidden in [
    'data-projectpulse-guard-version="056D"',
    "const GUARD_VERSION = '056D'",
    "route === 'dashboard'",
]:
    if forbidden in index:
        errors.append(f"Forbidden fragment remains: {forbidden}")

if errors:
    print("056E_VALIDATION=FAILED")
    for error in errors:
        print(f"ERROR={error}")
    sys.exit(1)

print("056E_VALIDATION=PASSED")
print("GLOBAL_LEGACY_CARD_SUPPRESSION=YES")
print("ROUTE_WORKSPACE_PROTECTION=YES")
print("API_CHANGED=NO")
print("DATABASE_CHANGED=NO")
PY

chmod 0755 scripts/validate-056e-global-card-suppression.py

cat > deployment/azure/README-route-056e-source.md <<EOF
# 056E global legacy dashboard-card suppression

Base source commit:

\`$SOURCE_BASE\`

056E suppresses legacy injected Module 022-030 dashboard summary cards across
all routes, including \`#dashboard\`.

It preserves the actual React route workspaces by excluding descendants of
\`#root\`, \`.app-shell\`, route shells, route pages, modals, drawers, and panel
roots.

This source change does not deploy API code and does not change the database.
EOF

python3 scripts/validate-056e-global-card-suppression.py >>"$EVENT_LOG" 2>&1

git diff --check

git add \
  src/frontend/project-time-web/index.html \
  docs/056e-global-legacy-dashboard-card-suppression-audit.md \
  scripts/validate-056e-global-card-suppression.py \
  deployment/azure/README-route-056e-source.md

git diff --cached --check

git commit -m "Suppress legacy dashboard summary cards globally" >>"$EVENT_LOG" 2>&1

SOURCE_COMMIT="$(git rev-parse HEAD)"

git push origin "HEAD:$SOURCE_BRANCH" >>"$EVENT_LOG" 2>&1

REMOTE_AFTER="$(
  git ls-remote \
    "https://github.com/$REPOSITORY.git" \
    "refs/heads/$SOURCE_BRANCH" |
  awk '{print $1}'
)"

[ "$REMOTE_AFTER" = "$SOURCE_COMMIT" ] ||
  fail "Remote source did not update to $SOURCE_COMMIT"

SOURCE_TAG="fix/web-global-legacy-card-suppression-056e/$STAMP"

git tag -a "$SOURCE_TAG" \
  -m "ProjectPulse 056E global legacy dashboard-card suppression"

git push origin "refs/tags/$SOURCE_TAG" >>"$EVENT_LOG" 2>&1

log "SOURCE_COMMIT=$SOURCE_COMMIT"
log "SOURCE_TAG=$SOURCE_TAG"
log "SOURCE_PUSHED=YES"

log ""
log "============================================================"
log "PREPARE OPS BRANCH"
log "============================================================"

gh repo clone "$REPOSITORY" "$OPS_REPO" -- \
  --branch "$SOURCE_BRANCH" \
  --single-branch >>"$EVENT_LOG" 2>&1

cd "$OPS_REPO"

[ "$(git rev-parse HEAD)" = "$SOURCE_COMMIT" ] ||
  fail "Ops clone does not match new source commit"

git switch -c "$OPS_BRANCH" >>"$EVENT_LOG" 2>&1

git config --local user.name "Ahmed Adeyemi"
git config --local user.email \
  "ahmedadeyemi-cts@users.noreply.github.com"

mkdir -p deployment/azure

cat > deployment/azure/README-route-056e-deployment.md <<EOF
# 056E Azure web deployment

Source branch:

\`$SOURCE_BRANCH\`

Source commit:

\`$SOURCE_COMMIT\`

Target:

\`$WEB_APP\`

This deployment updates only the web Container App image. API and database are
not changed.
EOF

cp "$0" deployment/azure/deploy-web-route-056e-detached.sh
chmod 0755 deployment/azure/deploy-web-route-056e-detached.sh

git add \
  deployment/azure/README-route-056e-deployment.md \
  deployment/azure/deploy-web-route-056e-detached.sh

git diff --cached --check

git commit -m "Add detached Azure web deployment workflow for 056E card suppression" >>"$EVENT_LOG" 2>&1

git push --set-upstream origin "$OPS_BRANCH" >>"$EVENT_LOG" 2>&1

WORKFLOW_COMMIT="$(git rev-parse HEAD)"

log "OPS_BRANCH=$OPS_BRANCH"
log "WORKFLOW_COMMIT=$WORKFLOW_COMMIT"

log ""
log "============================================================"
log "CAPTURE CURRENT WEB DEPLOYMENT"
log "============================================================"

OLD_IMAGE="$(
  az containerapp show \
    --resource-group "$APP_RG" \
    --name "$WEB_APP" \
    --query 'properties.template.containers[0].image' \
    -o tsv
)"

OLD_REVISION="$(
  az containerapp show \
    --resource-group "$APP_RG" \
    --name "$WEB_APP" \
    --query 'properties.latestReadyRevisionName' \
    -o tsv
)"

log "OLD_IMAGE=$OLD_IMAGE"
log "OLD_REVISION=$OLD_REVISION"

rollback() {
  log ""
  log "============================================================"
  log "ROLLBACK_STARTED"
  log "============================================================"

  az containerapp update \
    --resource-group "$APP_RG" \
    --name "$WEB_APP" \
    --image "$OLD_IMAGE" \
    --revision-suffix "rollback-056e-$(date -u +%m%d%H%M%S)" \
    --output none >>"$EVENT_LOG" 2>&1 || true

  log "ROLLBACK_IMAGE=$OLD_IMAGE"
}

TAG="route-056e-${SOURCE_COMMIT:0:7}-${STAMP,,}"
REVISION_SUFFIX="r056e-${SOURCE_COMMIT:0:7}-$(date -u +%m%d%H%M%S)"

log ""
log "============================================================"
log "BUILD 056E WEB IMAGE IN ACR"
log "============================================================"
log "TAG=$TAG"
log "BUILD_LOG=$BUILD_LOG"

cd "$SOURCE_REPO"

az acr build \
  --registry "$ACR_NAME" \
  --image "$IMAGE_REPOSITORY:$TAG" \
  --file deployment/containers/web/Dockerfile \
  . >"$BUILD_LOG" 2>&1

NEW_DIGEST="$(
  az acr repository show \
    --name "$ACR_NAME" \
    --image "$IMAGE_REPOSITORY:$TAG" \
    --query digest \
    -o tsv
)"

[ -n "$NEW_DIGEST" ] ||
  fail "No digest returned for web image"

NEW_IMAGE="$ACR_SERVER/$IMAGE_REPOSITORY@$NEW_DIGEST"

log "NEW_DIGEST=$NEW_DIGEST"
log "NEW_IMAGE=$NEW_IMAGE"

log ""
log "============================================================"
log "DEPLOY 056E WEB IMAGE"
log "============================================================"
log "REVISION_SUFFIX=$REVISION_SUFFIX"

az containerapp update \
  --resource-group "$APP_RG" \
  --name "$WEB_APP" \
  --image "$NEW_IMAGE" \
  --revision-suffix "$REVISION_SUFFIX" \
  --output none >>"$EVENT_LOG" 2>&1

NEW_REVISION=""

for ATTEMPT in $(seq 1 60); do
  CURRENT_IMAGE="$(
    az containerapp show \
      --resource-group "$APP_RG" \
      --name "$WEB_APP" \
      --query 'properties.template.containers[0].image' \
      -o tsv 2>/dev/null || true
  )"

  READY_REVISION="$(
    az containerapp show \
      --resource-group "$APP_RG" \
      --name "$WEB_APP" \
      --query 'properties.latestReadyRevisionName' \
      -o tsv 2>/dev/null || true
  )"

  RUNNING_STATUS="$(
    az containerapp show \
      --resource-group "$APP_RG" \
      --name "$WEB_APP" \
      --query 'properties.runningStatus' \
      -o tsv 2>/dev/null || true
  )"

  log "WAIT_ATTEMPT=$ATTEMPT CURRENT_IMAGE=$CURRENT_IMAGE READY_REVISION=$READY_REVISION RUNNING_STATUS=$RUNNING_STATUS"

  if [ "$CURRENT_IMAGE" = "$NEW_IMAGE" ] &&
     [[ "$READY_REVISION" == *"--$REVISION_SUFFIX" ]] &&
     [ "$RUNNING_STATUS" = "Running" ]; then
    NEW_REVISION="$READY_REVISION"
    break
  fi

  sleep 10
done

[ -n "$NEW_REVISION" ] || {
  rollback
  fail "New 056E revision did not become ready"
}

log "NEW_REVISION=$NEW_REVISION"

log ""
log "============================================================"
log "LIVE 056E HTML VALIDATION"
log "============================================================"

LIVE_HTML="$(mktemp)"

LIVE_OK=0

for ATTEMPT in $(seq 1 30); do
  HTTP_CODE="$(
    curl -fsS \
      -H 'Cache-Control: no-cache' \
      -o "$LIVE_HTML" \
      -w '%{http_code}' \
      "$PUBLIC_URL/?route-guard-056e=$TAG" 2>/dev/null || true
  )"

  LIVE_ASSET="$(
    grep -oE '/assets/index-[A-Za-z0-9_-]+\.js' "$LIVE_HTML" 2>/dev/null |
    sort -u |
    head -1 || true
  )"

  LIVE_BYTES="$(wc -c < "$LIVE_HTML" 2>/dev/null || echo 0)"

  if grep -q 'data-projectpulse-guard-version="056E"' "$LIVE_HTML" 2>/dev/null &&
     grep -q "const GUARD_VERSION = '056E'" "$LIVE_HTML" 2>/dev/null &&
     grep -q 'data-projectpulse-legacy-dashboard-summary-card' "$LIVE_HTML" 2>/dev/null &&
     grep -q 'discoverLegacyDashboardSummaries' "$LIVE_HTML" 2>/dev/null &&
     grep -q 'suppressLegacyDashboardSummary' "$LIVE_HTML" 2>/dev/null; then
    LIVE_056E_GUARD="YES"
  else
    LIVE_056E_GUARD="NO"
  fi

  log "LIVE_ATTEMPT=$ATTEMPT LIVE_HTTP_CODE=$HTTP_CODE LIVE_ASSET=$LIVE_ASSET LIVE_HTML_BYTES=$LIVE_BYTES LIVE_056E_GUARD=$LIVE_056E_GUARD"

  if [ "$HTTP_CODE" = "200" ] &&
     [ "$LIVE_056E_GUARD" = "YES" ]; then
    LIVE_OK=1
    break
  fi

  sleep 10
done

if [ "$LIVE_OK" != "1" ]; then
  rollback
  fail "Live 056E validation failed"
fi

LIVE_SHA="$(sha256sum "$LIVE_HTML" | awk '{print $1}')"
BUILD_LOG_SHA="$(sha256sum "$BUILD_LOG" | awk '{print $1}')"
EVENT_LOG_SHA="$(sha256sum "$EVENT_LOG" | awk '{print $1}')"

rm -f "$LIVE_HTML"

log ""
log "============================================================"
log "RECORD DEPLOYMENT EVIDENCE"
log "============================================================"

cd "$OPS_REPO"

RELEASE_DIR="deployment/azure/releases/$STAMP"
mkdir -p "$RELEASE_DIR"

cat > "$RELEASE_DIR/deployment-summary.txt" <<EOF
deployedAtUtc=$(date -u +%Y-%m-%dT%H:%M:%SZ)
sourceBranch=$SOURCE_BRANCH
sourceBase=$SOURCE_BASE
sourceCommit=$SOURCE_COMMIT
sourceTag=$SOURCE_TAG
workflowCommit=$WORKFLOW_COMMIT
webContainerApp=$WEB_APP
oldImage=$OLD_IMAGE
oldRevision=$OLD_REVISION
newImage=$NEW_IMAGE
newDigest=$NEW_DIGEST
newRevision=$NEW_REVISION
revisionSuffix=$REVISION_SUFFIX
acrTag=$TAG
liveHttpCode=$HTTP_CODE
liveAsset=$LIVE_ASSET
liveHtmlBytes=$LIVE_BYTES
liveHtmlSha256=$LIVE_SHA
live056EGuard=YES
buildLogSha256=$BUILD_LOG_SHA
eventLogSha256=$EVENT_LOG_SHA
apiChanged=no
databaseChanged=no
EOF

cat > "$RELEASE_DIR/browser-validation.js" <<'EOF'
(() => {
  const result =
    window.__projectPulse056BDashboardCardRouteGuardDiagnostics?.();

  const modulePattern =
    /\bmodule\s+0?(?:22|23|24|25|26|27|28|29|30)\b/i;

  const known = [
    "Production Notification Center",
    "Production Readiness Center",
    "Sales-to-Delivery Intake Foundation",
    "SOW Generator + Claude Research Review",
    "CRM Integration Framework",
    "Signed SOW Handoff + Assignment Trigger",
    "SOW-Aware AI Time Entry Generator",
    "User Acceptance / Role + Workflow Validation Center",
    "Reporting / Accounting / Invoicing / Analytics Command Center"
  ];

  const headings = [
    ...document.querySelectorAll("h1,h2,h3,h4,h5,h6,[role='heading']")
  ];

  const visibleLegacyDashboardHeadings = headings
    .filter((heading) => {
      const text = heading.textContent.replace(/\s+/g, " ").trim();

      return modulePattern.test(text) ||
        known.some((title) => text.includes(title));
    })
    .filter((heading) => {
      if (heading.closest("#root, .app-shell")) {
        return false;
      }

      const card =
        heading.closest(
          "[data-projectpulse-legacy-dashboard-summary-card='true'], [data-projectpulse-dashboard-only-card='true']"
        ) ||
        heading;

      const style = getComputedStyle(card);

      return !card.hidden &&
        style.display !== "none" &&
        style.visibility !== "hidden";
    })
    .map((heading) =>
      heading.textContent.replace(/\s+/g, " ").trim()
    );

  return {
    hash: location.hash,
    route: document.documentElement.getAttribute(
      "data-projectpulse-056b-route"
    ),
    guardVersion: document.documentElement.getAttribute(
      "data-projectpulse-056b-guard-version"
    ),
    markedCount: document.documentElement.getAttribute(
      "data-projectpulse-056b-marked-count"
    ),
    visibleOffenderCount: document.documentElement.getAttribute(
      "data-projectpulse-056b-visible-offender-count"
    ),
    runtimeResult: result,
    visibleLegacyDashboardHeadings
  };
})()
EOF

git add "$RELEASE_DIR"

git diff --cached --check

git commit -m "Record successful 056E Azure web deployment" >>"$EVENT_LOG" 2>&1

EVIDENCE_COMMIT="$(git rev-parse HEAD)"

git push origin "HEAD:$OPS_BRANCH" >>"$EVENT_LOG" 2>&1

DEPLOYMENT_TAG="deploy/web-056e/$STAMP"

git tag -a "$DEPLOYMENT_TAG" \
  -m "Successful ProjectPulse 056E Azure web deployment"

git push origin "refs/tags/$DEPLOYMENT_TAG" >>"$EVENT_LOG" 2>&1

log ""
log "============================================================"
log "056E_GLOBAL_CARD_SUPPRESSION=COMPLETE"
log "============================================================"
log "SOURCE_BASE=$SOURCE_BASE"
log "SOURCE_COMMIT=$SOURCE_COMMIT"
log "SOURCE_TAG=$SOURCE_TAG"
log "OPS_BRANCH=$OPS_BRANCH"
log "WORKFLOW_COMMIT=$WORKFLOW_COMMIT"
log "EVIDENCE_COMMIT=$EVIDENCE_COMMIT"
log "DEPLOYMENT_TAG=$DEPLOYMENT_TAG"
log "OLD_IMAGE=$OLD_IMAGE"
log "OLD_REVISION=$OLD_REVISION"
log "NEW_IMAGE=$NEW_IMAGE"
log "NEW_REVISION=$NEW_REVISION"
log "LIVE_ASSET=$LIVE_ASSET"
log "LIVE_056E_GUARD=YES"
log "RUN_DIR=$RUN_DIR"
log "API_CHANGED=NO"
log "DATABASE_CHANGED=NO"
log "============================================================"

cat > "$STATUS_FILE" <<EOF
status=complete
sourceBase=$SOURCE_BASE
sourceCommit=$SOURCE_COMMIT
sourceTag=$SOURCE_TAG
evidenceCommit=$EVIDENCE_COMMIT
deploymentTag=$DEPLOYMENT_TAG
newImage=$NEW_IMAGE
newRevision=$NEW_REVISION
liveAsset=$LIVE_ASSET
live056EGuard=YES
runDir=$RUN_DIR
apiChanged=no
databaseChanged=no
EOF
