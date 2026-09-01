#!/usr/bin/env bash
set -Eeuo pipefail

fail() {
  echo "ERROR: $*" >&2
  exit 1
}

: "${BASE:?BASE is required}"
: "${TEST_LOGIN_PASSWORD:?TEST_LOGIN_PASSWORD is required}"
: "${EVIDENCE_DIR:?EVIDENCE_DIR is required}"

BASE="${BASE%/}"
EXPECTED_DEPLOYED_SOURCE="${EXPECTED_DEPLOYED_SOURCE:-0906e7c0a30d724bb6099860ac46da6eb50f18f6}"
[[ "$BASE" == "https://phd-west-test.onenecklab.com" ]] \
  || fail "Module 025 live UAT is restricted to Protected Test."
[[ ${#TEST_LOGIN_PASSWORD} -ge 12 ]] \
  || fail "The Protected-Test login credential is unavailable."

install -d -m 0700 "$EVIDENCE_DIR"
umask 077

AUTHOR_SESSION=''
AUTHOR_IDENTITY=''
ENGAGEMENT_ID=''
ENGAGEMENT_NUMBER=''
CLEANUP_NEEDED=false

cleanup() {
  local exit_code=$?
  set +e
  if [[ "$CLEANUP_NEEDED" == true && -n "$AUTHOR_SESSION" && -n "$ENGAGEMENT_ID" ]]; then
    curl -sS --http1.1 --connect-timeout 20 --max-time 120 \
      -o "$EVIDENCE_DIR/cleanup-archive.json" \
      -X POST \
      -H 'Cache-Control: no-cache, no-store, max-age=0' \
      -H "Authorization: Bearer $AUTHOR_SESSION" \
      -H "X-ProjectPulse-Session: $AUTHOR_SESSION" \
      -H 'X-ProjectPulse-Module-Number: 025' \
      -H "Origin: $BASE" \
      -H 'Sec-Fetch-Site: same-origin' \
      "$BASE/api/module025/sow-gsd/$ENGAGEMENT_ID/archive" >/dev/null 2>&1 || true
  fi
  if [[ -n "$AUTHOR_SESSION" ]]; then
    curl -sS --http1.1 --connect-timeout 20 --max-time 60 \
      -o "$EVIDENCE_DIR/cleanup-logout.json" \
      -X POST \
      -H "Authorization: Bearer $AUTHOR_SESSION" \
      -H "X-ProjectPulse-Session: $AUTHOR_SESSION" \
      -H 'X-ProjectPulse-Module-Number: 025' \
      -H "Origin: $BASE" \
      -H 'Sec-Fetch-Site: same-origin' \
      "$BASE/api/auth/session/logout" >/dev/null 2>&1 || true
  fi
  exit "$exit_code"
}
trap cleanup EXIT

HEALTH_STATUS="$(curl -sS --http1.1 --connect-timeout 20 --max-time 60 \
  -o "$EVIDENCE_DIR/health.json" -w '%{http_code}' \
  -H 'Cache-Control: no-cache, no-store, max-age=0' \
  "$BASE/health?module025-live-uat=${GITHUB_RUN_ID:-local}-${GITHUB_RUN_ATTEMPT:-1}" || true)"
[[ "$HEALTH_STATUS" == 200 ]] || fail "Protected Test health returned HTTP $HEALTH_STATUS."
jq -e '.status == "healthy"' "$EVIDENCE_DIR/health.json" >/dev/null \
  || fail "Protected Test is not healthy."

PORTAL="$EVIDENCE_DIR/portal.html"
BUNDLE="$EVIDENCE_DIR/served-bundle.txt"
PORTAL_STATUS="$(curl -sS --http1.1 --connect-timeout 20 --max-time 60 \
  -o "$PORTAL" -w '%{http_code}' \
  -H 'Cache-Control: no-cache, no-store, max-age=0' \
  "$BASE/?module025-live-uat=${GITHUB_RUN_ID:-local}" || true)"
[[ "$PORTAL_STATUS" == 200 ]] || fail "Protected-Test portal returned HTTP $PORTAL_STATUS."
: > "$BUNDLE"
mapfile -t ASSETS < <(
  grep -oE '(src|href)="[^"]+\.(js|css)(\?[^"]*)?"' "$PORTAL" \
    | sed -E 's/^(src|href)="([^"]+)"$/\2/' \
    | sort -u
)
(( ${#ASSETS[@]} > 0 )) || fail "Protected-Test portal exposed no JS/CSS assets."
for asset in "${ASSETS[@]}"; do
  [[ "$asset" == /assets/* ]] || continue
  curl -fsS --http1.1 --connect-timeout 20 --max-time 90 \
    -H 'Cache-Control: no-cache, no-store, max-age=0' \
    "$BASE$asset" >> "$BUNDLE"
done
for marker in \
  'data-module025-sow-gsd-workspace' \
  'SOW & GSD Workspace' \
  '/api/module025/sow-gsd/bootstrap' \
  'Customer not listed — enter manually'; do
  grep -Fq -- "$marker" "$BUNDLE" \
    || fail "Served frontend bundle is missing Module 025 marker: $marker"
done
jq -n --argjson assetCount "${#ASSETS[@]}" \
  '{status:"passed",route:"#sow-generator",workspaceMarkersPresent:true,assetCount:$assetCount}' \
  > "$EVIDENCE_DIR/served-module025-route.json"

auth_headers_for() {
  local token="$1"
  printf '%s\n' "$token"
}

login() {
  local identity="$1" output="$2" payload status
  payload="$(mktemp)"
  jq -n --arg username "$identity" --arg password "$TEST_LOGIN_PASSWORD" \
    '{username:$username,password:$password}' > "$payload"
  status="$(curl -sS --http1.1 --connect-timeout 20 --max-time 90 \
    -o "$output" -w '%{http_code}' \
    -H 'Cache-Control: no-cache, no-store, max-age=0' \
    -H 'Content-Type: application/json' \
    -H "Origin: $BASE" \
    -H 'Sec-Fetch-Site: same-origin' \
    --data-binary @"$payload" \
    "$BASE/api/auth/local/login" || true)"
  rm -f "$payload"
  printf '%s' "$status"
}

BOOTSTRAP="$EVIDENCE_DIR/module025-bootstrap.json"
IDENTITY_PROBE="$EVIDENCE_DIR/identity-probe.tsv"
printf 'identity\tloginStatus\tbootstrapStatus\tcanCreate\n' > "$IDENTITY_PROBE"
CANDIDATES=(
  'project.team.coordinator@ussignal.local'
  'heather.schrock@ussignal.local'
  'demo.manager@ussignal.local'
  'demo.engineer@ussignal.local'
  'jason.mosier@ussignal.local'
  'ahmed.adeyemi@onenecklab.com'
)
for identity in "${CANDIDATES[@]}"; do
  LOGIN_OUTPUT="$(mktemp)"
  login_status="$(login "$identity" "$LOGIN_OUTPUT")"
  if [[ "$login_status" != 200 ]]; then
    printf '%s\t%s\t-\tfalse\n' "$identity" "$login_status" >> "$IDENTITY_PROBE"
    rm -f "$LOGIN_OUTPUT"
    continue
  fi

  token="$(jq -r '.sessionToken // empty' "$LOGIN_OUTPUT")"
  rm -f "$LOGIN_OUTPUT"
  if [[ -z "$token" ]]; then
    printf '%s\t%s\t-\tfalse\n' "$identity" "$login_status" >> "$IDENTITY_PROBE"
    continue
  fi
  echo "::add-mask::$token"

  bootstrap_status="$(curl -sS --http1.1 --connect-timeout 20 --max-time 120 \
    -o "$BOOTSTRAP" -w '%{http_code}' \
    -H 'Cache-Control: no-cache, no-store, max-age=0' \
    -H "Authorization: Bearer $token" \
    -H "X-ProjectPulse-Session: $token" \
    -H 'X-ProjectPulse-Module-Number: 025' \
    -H "Origin: $BASE" \
    -H 'Sec-Fetch-Site: same-origin' \
    "$BASE/api/module025/sow-gsd/bootstrap" || true)"
  can_create=false
  if [[ "$bootstrap_status" == 200 ]] \
    && jq -e '.status == "module025_workspace_ready" and .module == "025" and .migration == "099_module025_sow_gsd_workspace" and .access.canCreate == true' "$BOOTSTRAP" >/dev/null 2>&1; then
    can_create=true
  fi
  printf '%s\t%s\t%s\t%s\n' "$identity" "$login_status" "$bootstrap_status" "$can_create" >> "$IDENTITY_PROBE"

  if [[ "$can_create" == true ]]; then
    AUTHOR_SESSION="$token"
    AUTHOR_IDENTITY="$identity"
    break
  fi

  curl -sS --http1.1 --connect-timeout 20 --max-time 60 \
    -o /dev/null -X POST \
    -H "Authorization: Bearer $token" \
    -H "X-ProjectPulse-Session: $token" \
    -H 'X-ProjectPulse-Module-Number: 025' \
    -H "Origin: $BASE" \
    -H 'Sec-Fetch-Site: same-origin' \
    "$BASE/api/auth/session/logout" || true
done
[[ -n "$AUTHOR_SESSION" ]] \
  || fail "No known Protected-Test fixture identity has live Module 025 authoring authority."
echo "MODULE025_AUTHOR=$AUTHOR_IDENTITY"

jq -e '
  .status == "module025_workspace_ready"
  and .module == "025"
  and .migration == "099_module025_sow_gsd_workspace"
  and .contract == "module025-sow-gsd-workspace-v1-20260830"
  and .access.canCreate == true
  and .autosave.enabled == true
  and .autosave.optimisticRevision == true
  and (.phases | map(.code) == ["plan","design","implement","validate","release"])
' "$BOOTSTRAP" >/dev/null || fail "Module 025 bootstrap contract is incomplete."

AE_ID="$(jq -r '.accountExecutives[0].userId // empty' "$BOOTSTRAP")"
RESALE_ID="$(jq -r '.resalePeople[0].userId // empty' "$BOOTSTRAP")"
[[ "$AE_ID" =~ ^[0-9a-fA-F]{8}-[0-9a-fA-F-]{27}$ ]] \
  || fail "Module 025 bootstrap returned no Account Executive candidate."
[[ "$RESALE_ID" =~ ^[0-9a-fA-F]{8}-[0-9a-fA-F-]{27}$ ]] \
  || fail "Module 025 bootstrap returned no Resale candidate."

auth_json() {
  local method="$1" path="$2" output="$3" input="${4:-}"
  local args=(
    -sS --http1.1 --connect-timeout 20 --max-time 240
    -X "$method"
    -o "$output" -w '%{http_code}'
    -H 'Cache-Control: no-cache, no-store, max-age=0'
    -H "Authorization: Bearer $AUTHOR_SESSION"
    -H "X-ProjectPulse-Session: $AUTHOR_SESSION"
    -H 'X-ProjectPulse-Module-Number: 025'
    -H "Origin: $BASE"
    -H 'Sec-Fetch-Site: same-origin'
  )
  if [[ -n "$input" ]]; then
    args+=( -H 'Content-Type: application/json' --data-binary @"$input" )
  fi
  curl "${args[@]}" "$BASE$path" || true
}

# If Chrome is present on the hosted runner, prove the actual authenticated route
# renders the redesigned workspace DOM marker. Otherwise the served-bundle proof
# above remains mandatory and the browser check is recorded as unavailable.
DOM_ASSERTION='skipped_no_browser'
CHROME="$(command -v google-chrome-stable || command -v google-chrome || command -v chromium || true)"
if [[ -n "$CHROME" ]]; then
  BROWSER_DIR="$RUNNER_TEMP/module025-browser"
  install -d "$BROWSER_DIR"
  if npm install --silent --prefix "$BROWSER_DIR" playwright-core@1.55.0 >/dev/null 2>&1; then
    export NODE_PATH="$BROWSER_DIR/node_modules"
    export MODULE025_BROWSER_CHROME="$CHROME"
    export MODULE025_BROWSER_SESSION="$AUTHOR_SESSION"
    export MODULE025_BROWSER_BASE="$BASE"
    if node <<'NODE'
const fs = require('fs');
const { chromium } = require('playwright-core');
(async () => {
  const base = process.env.MODULE025_BROWSER_BASE;
  const token = process.env.MODULE025_BROWSER_SESSION;
  const browser = await chromium.launch({
    headless: true,
    executablePath: process.env.MODULE025_BROWSER_CHROME,
    args: ['--no-sandbox', '--disable-dev-shm-usage']
  });
  const context = await browser.newContext({
    extraHTTPHeaders: {
      Authorization: `Bearer ${token}`,
      'X-ProjectPulse-Session': token,
      'X-ProjectPulse-Module-Number': '025'
    }
  });
  const page = await context.newPage();
  await page.goto(`${base}/?module025-preauth=${Date.now()}`, { waitUntil: 'domcontentloaded', timeout: 60000 });
  await page.evaluate((sessionToken) => {
    localStorage.setItem('projectPulseAuthSession', JSON.stringify({ sessionToken }));
  }, token);
  await page.goto(`${base}/#sow-generator`, { waitUntil: 'networkidle', timeout: 90000 });
  const workspace = page.locator('[data-module025-sow-gsd-workspace="true"]');
  await workspace.waitFor({ state: 'attached', timeout: 60000 });
  const text = await workspace.innerText();
  if (!text.includes('SOW & GSD Workspace')) {
    throw new Error('Module 025 DOM marker rendered without the SOW & GSD Workspace heading.');
  }
  fs.writeFileSync(process.env.EVIDENCE_DIR + '/module025-dom-route.json', JSON.stringify({
    status: 'passed',
    route: '#sow-generator',
    marker: 'data-module025-sow-gsd-workspace=true',
    headingPresent: true
  }, null, 2));
  await browser.close();
})().catch((error) => {
  console.error(error.message);
  process.exit(1);
});
NODE
    then
      DOM_ASSERTION='passed'
    else
      fail "Authenticated #sow-generator route did not render the Module 025 workspace DOM marker."
    fi
  else
    DOM_ASSERTION='skipped_playwright_install_unavailable'
  fi
fi

CUSTOMER="Protected Test Module025 UAT ${GITHUB_RUN_ID:-local}"
CREATE_PAYLOAD="$EVIDENCE_DIR/create-payload.json"
jq -n \
  --arg customer "$CUSTOMER" \
  --arg ae "$AE_ID" \
  --arg resale "$RESALE_ID" \
  '{
    customerId:null,
    customerName:$customer,
    customerEntryMode:"manual",
    commercialModel:"time_and_materials",
    customerProgram:"standard",
    accountExecutiveUserId:$ae,
    resaleUserId:$resale,
    serviceOverview:"Protected Test governed acceptance for a customer network modernization engagement. Validate planning, design, implementation, validation, release, reviewed level of effort, document export, and lifecycle controls without asserting unsupported product versions, quantities, pricing, dates, or customer decisions."
  }' > "$CREATE_PAYLOAD"

CREATE="$EVIDENCE_DIR/create.json"
CREATE_STATUS="$(auth_json POST '/api/module025/sow-gsd' "$CREATE" "$CREATE_PAYLOAD")"
[[ "$CREATE_STATUS" == 201 ]] || fail "Module 025 create returned HTTP $CREATE_STATUS."
ENGAGEMENT_ID="$(jq -r '.engagement.engagementId // empty' "$CREATE")"
ENGAGEMENT_NUMBER="$(jq -r '.engagement.engagementNumber // empty' "$CREATE")"
[[ "$ENGAGEMENT_ID" =~ ^[0-9a-fA-F]{8}-[0-9a-fA-F-]{27}$ ]] \
  || fail "Module 025 create did not return an engagement ID."
[[ "$ENGAGEMENT_NUMBER" =~ ^SOW-[0-9]{4}-[0-9]{6}$ ]] \
  || fail "Module 025 immutable identifier is invalid: $ENGAGEMENT_NUMBER"
CLEANUP_NEEDED=true
jq -e --arg customer "$CUSTOMER" '
  .status == "module025_engagement_loaded"
  and .engagement.customerEntryMode == "manual"
  and .engagement.customerId == null
  and .engagement.customerName == $customer
  and .engagement.accountExecutiveUserId != null
  and .engagement.resaleUserId != null
  and .access.canEdit == true
' "$CREATE" >/dev/null || fail "Module 025 manual-customer create contract failed."

BOOTSTRAP_AFTER_CREATE="$EVIDENCE_DIR/bootstrap-after-manual-create.json"
BOOTSTRAP_AFTER_STATUS="$(auth_json GET '/api/module025/sow-gsd/bootstrap' "$BOOTSTRAP_AFTER_CREATE")"
[[ "$BOOTSTRAP_AFTER_STATUS" == 200 ]] || fail "Module 025 post-create bootstrap returned HTTP $BOOTSTRAP_AFTER_STATUS."
jq -e --arg customer "$CUSTOMER" '[.customers[]? | select(.customerName == $customer)] | length == 0' "$BOOTSTRAP_AFTER_CREATE" >/dev/null \
  || fail "Manual Module 025 customer was incorrectly inserted into the canonical clients directory."

LIST_ACTIVE="$EVIDENCE_DIR/list-active.json"
LIST_STATUS="$(auth_json GET "/api/module025/sow-gsd?state=active&search=$ENGAGEMENT_NUMBER" "$LIST_ACTIVE")"
[[ "$LIST_STATUS" == 200 ]] || fail "Module 025 active list returned HTTP $LIST_STATUS."
jq -e --arg id "$ENGAGEMENT_ID" 'any(.engagements[]?; .engagementId == $id)' "$LIST_ACTIVE" >/dev/null \
  || fail "Created Module 025 record was not visible in the active work queue."

GET_CREATED="$EVIDENCE_DIR/get-created.json"
GET_STATUS="$(auth_json GET "/api/module025/sow-gsd/$ENGAGEMENT_ID" "$GET_CREATED")"
[[ "$GET_STATUS" == 200 ]] || fail "Module 025 get returned HTTP $GET_STATUS."
jq -e --arg id "$ENGAGEMENT_ID" '.engagement.engagementId == $id and (.engagement.phases | length) == 5' "$GET_CREATED" >/dev/null \
  || fail "Module 025 get/readback contract failed."

GENERATE="$EVIDENCE_DIR/generate.json"
GENERATE_STATUS="$(auth_json POST "/api/module025/sow-gsd/$ENGAGEMENT_ID/generate" "$GENERATE")"
[[ "$GENERATE_STATUS" == 200 ]] || fail "Module 025 Celar generate returned HTTP $GENERATE_STATUS."
jq -e '
  .status == "module025_detailed_scope_generated"
  and (.revision | type == "number")
  and (.engagement.engagement.phases | length) == 5
  and .engagement.engagement.status == "review_ready"
' "$GENERATE" >/dev/null || fail "Module 025 Celar generation contract failed."
REVISION="$(jq -r '.revision' "$GENERATE")"

SAVE_PAYLOAD="$EVIDENCE_DIR/save-payload.json"
jq -n \
  --argjson revision "$REVISION" \
  --arg customer "$CUSTOMER" \
  --arg ae "$AE_ID" \
  --arg resale "$RESALE_ID" '
  def phase($code; $label): {
    phaseCode:$code,
    finalHours:2,
    objective:("Protected-Test reviewed " + $label + " objective."),
    detailedActivities:[("Execute the reviewed " + $label + " activities for this governed acceptance record.")],
    technicalTasks:[("Validate the " + $label + " technical work against approved customer inputs and dependencies.")],
    deliverables:[($label + " acceptance deliverable")],
    customerResponsibilities:[("Provide approved inputs required for " + $label + ".")],
    usSignalResponsibilities:[("Perform and document the reviewed " + $label + " work.")],
    prerequisites:[("Approved prerequisites for " + $label + " are available.")],
    dependencies:[("Upstream dependencies for " + $label + " are reviewed.")],
    assumptions:[("Protected-Test UAT assumption for " + $label + "; no unsupported technical fact is asserted.")],
    openQuestions:[("Confirm any customer-specific " + $label + " detail before delivery.")],
    acceptanceCriteria:[($label + " evidence is reviewed and accepted by the authorized project stakeholders.")],
    validationSteps:[("Review the recorded " + $label + " evidence and completion criteria.")],
    risks:[("Unconfirmed customer inputs may affect " + $label + " execution.")],
    loeRationale:("Two Protected-Test UAT hours are used only to prove editable reviewed LOE for " + $label + ".")
  };
  {
    expectedRevision:$revision,
    customerId:null,
    customerName:$customer,
    customerEntryMode:"manual",
    commercialModel:"time_and_materials",
    customerProgram:"standard",
    accountExecutiveUserId:$ae,
    resaleUserId:$resale,
    serviceOverview:"Protected Test governed acceptance for a customer network modernization engagement. Validate planning, design, implementation, validation, release, reviewed level of effort, document export, and lifecycle controls without asserting unsupported product versions, quantities, pricing, dates, or customer decisions.",
    phases:[
      phase("plan";"Plan"),
      phase("design";"Design"),
      phase("implement";"Implement"),
      phase("validate";"Validate"),
      phase("release";"Release")
    ]
  }
' > "$SAVE_PAYLOAD"

SAVE="$EVIDENCE_DIR/save.json"
SAVE_STATUS="$(auth_json PUT "/api/module025/sow-gsd/$ENGAGEMENT_ID" "$SAVE" "$SAVE_PAYLOAD")"
[[ "$SAVE_STATUS" == 200 ]] || fail "Module 025 autosave returned HTTP $SAVE_STATUS."
jq -e '
  .status == "module025_autosaved"
  and .requiresRegeneration == false
  and .engagement.engagement.finalHours == 10
  and (.engagement.engagement.phases | all(.finalHours == 2))
' "$SAVE" >/dev/null || fail "Module 025 autosave/reviewed-LOE contract failed."

CONFIRM="$EVIDENCE_DIR/confirm.json"
CONFIRM_STATUS="$(auth_json POST "/api/module025/sow-gsd/$ENGAGEMENT_ID/confirm" "$CONFIRM")"
[[ "$CONFIRM_STATUS" == 200 ]] || fail "Module 025 confirm returned HTTP $CONFIRM_STATUS."
jq -e '.status == "module025_confirmed" and .canDownload == true' "$CONFIRM" >/dev/null \
  || fail "Module 025 confirm contract failed."

download_file() {
  local path="$1" output="$2" headers="$3"
  curl -sS --http1.1 --connect-timeout 20 --max-time 180 \
    -o "$output" -D "$headers" -w '%{http_code}' \
    -H 'Cache-Control: no-cache, no-store, max-age=0' \
    -H "Authorization: Bearer $AUTHOR_SESSION" \
    -H "X-ProjectPulse-Session: $AUTHOR_SESSION" \
    -H 'X-ProjectPulse-Module-Number: 025' \
    -H "Origin: $BASE" \
    -H 'Sec-Fetch-Site: same-origin' \
    "$BASE$path" || true
}

SOW_FILE="$EVIDENCE_DIR/$ENGAGEMENT_NUMBER-SOW.docx"
SOW_HEADERS="$EVIDENCE_DIR/sow-download.headers"
SOW_STATUS="$(download_file "/api/module025/sow-gsd/$ENGAGEMENT_ID/sow.docx" "$SOW_FILE" "$SOW_HEADERS")"
[[ "$SOW_STATUS" == 200 ]] || fail "Module 025 SOW DOCX download returned HTTP $SOW_STATUS."
grep -Eiq '^content-type: application/vnd\.openxmlformats-officedocument\.wordprocessingml\.document' "$SOW_HEADERS" \
  || fail "SOW download content type is incorrect."
(( $(wc -c < "$SOW_FILE") > 1000 )) || fail "SOW DOCX download is unexpectedly small."
[[ "$(head -c 2 "$SOW_FILE")" == 'PK' ]] || fail "SOW DOCX is not an Open XML ZIP package."

GSD_FILE="$EVIDENCE_DIR/$ENGAGEMENT_NUMBER-GSD.xlsx"
GSD_HEADERS="$EVIDENCE_DIR/gsd-download.headers"
GSD_STATUS="$(download_file "/api/module025/sow-gsd/$ENGAGEMENT_ID/gsd.xlsx" "$GSD_FILE" "$GSD_HEADERS")"
[[ "$GSD_STATUS" == 200 ]] || fail "Module 025 GSD XLSX download returned HTTP $GSD_STATUS."
grep -Eiq '^content-type: application/vnd\.openxmlformats-officedocument\.spreadsheetml\.sheet' "$GSD_HEADERS" \
  || fail "GSD download content type is incorrect."
(( $(wc -c < "$GSD_FILE") > 1000 )) || fail "GSD XLSX download is unexpectedly small."
[[ "$(head -c 2 "$GSD_FILE")" == 'PK' ]] || fail "GSD XLSX is not an Open XML ZIP package."

REOPEN="$EVIDENCE_DIR/reopen.json"
REOPEN_STATUS="$(auth_json POST "/api/module025/sow-gsd/$ENGAGEMENT_ID/reopen" "$REOPEN")"
[[ "$REOPEN_STATUS" == 200 ]] || fail "Module 025 reopen returned HTTP $REOPEN_STATUS."
jq -e '.status == "module025_reopened"' "$REOPEN" >/dev/null || fail "Module 025 reopen contract failed."

ARCHIVE="$EVIDENCE_DIR/archive.json"
ARCHIVE_STATUS="$(auth_json POST "/api/module025/sow-gsd/$ENGAGEMENT_ID/archive" "$ARCHIVE")"
[[ "$ARCHIVE_STATUS" == 200 ]] || fail "Module 025 archive returned HTTP $ARCHIVE_STATUS."
jq -e '.status == "module025_archived"' "$ARCHIVE" >/dev/null || fail "Module 025 archive contract failed."

LIST_ARCHIVED="$EVIDENCE_DIR/list-archived.json"
LIST_ARCHIVED_STATUS="$(auth_json GET "/api/module025/sow-gsd?state=archived&search=$ENGAGEMENT_NUMBER" "$LIST_ARCHIVED")"
[[ "$LIST_ARCHIVED_STATUS" == 200 ]] || fail "Module 025 archived list returned HTTP $LIST_ARCHIVED_STATUS."
jq -e --arg id "$ENGAGEMENT_ID" 'any(.engagements[]?; .engagementId == $id and .status == "archived" and .isActive == false)' "$LIST_ARCHIVED" >/dev/null \
  || fail "Archived Module 025 record was not visible in the archived work queue."

UNARCHIVE="$EVIDENCE_DIR/unarchive.json"
UNARCHIVE_STATUS="$(auth_json POST "/api/module025/sow-gsd/$ENGAGEMENT_ID/unarchive" "$UNARCHIVE")"
[[ "$UNARCHIVE_STATUS" == 200 ]] || fail "Module 025 unarchive returned HTTP $UNARCHIVE_STATUS."
jq -e '.status == "module025_unarchived"' "$UNARCHIVE" >/dev/null || fail "Module 025 unarchive contract failed."

LIST_RESTORED="$EVIDENCE_DIR/list-restored.json"
LIST_RESTORED_STATUS="$(auth_json GET "/api/module025/sow-gsd?state=active&search=$ENGAGEMENT_NUMBER" "$LIST_RESTORED")"
[[ "$LIST_RESTORED_STATUS" == 200 ]] || fail "Module 025 restored active list returned HTTP $LIST_RESTORED_STATUS."
jq -e --arg id "$ENGAGEMENT_ID" 'any(.engagements[]?; .engagementId == $id and .isActive == true)' "$LIST_RESTORED" >/dev/null \
  || fail "Unarchived Module 025 record did not return to the active work queue."

FINAL_ARCHIVE="$EVIDENCE_DIR/final-archive.json"
FINAL_ARCHIVE_STATUS="$(auth_json POST "/api/module025/sow-gsd/$ENGAGEMENT_ID/archive" "$FINAL_ARCHIVE")"
[[ "$FINAL_ARCHIVE_STATUS" == 200 ]] || fail "Module 025 final cleanup archive returned HTTP $FINAL_ARCHIVE_STATUS."
jq -e '.status == "module025_archived"' "$FINAL_ARCHIVE" >/dev/null || fail "Module 025 final archive contract failed."
CLEANUP_NEEDED=false

FINAL_GET="$EVIDENCE_DIR/final-get.json"
FINAL_GET_STATUS="$(auth_json GET "/api/module025/sow-gsd/$ENGAGEMENT_ID" "$FINAL_GET")"
[[ "$FINAL_GET_STATUS" == 200 ]] || fail "Module 025 final archived readback returned HTTP $FINAL_GET_STATUS."
jq -e '.engagement.status == "archived" and .engagement.isActive == false' "$FINAL_GET" >/dev/null \
  || fail "Module 025 final archived state did not persist."

LOGOUT="$EVIDENCE_DIR/logout.json"
LOGOUT_STATUS="$(auth_json POST '/api/auth/session/logout' "$LOGOUT")"
[[ "$LOGOUT_STATUS" == 200 ]] || fail "Module 025 UAT logout returned HTTP $LOGOUT_STATUS."
AUTHOR_SESSION=''

jq -n \
  --arg source "$EXPECTED_DEPLOYED_SOURCE" \
  --arg workflowSha "${GITHUB_SHA:-local}" \
  --arg identity "$AUTHOR_IDENTITY" \
  --arg engagementId "$ENGAGEMENT_ID" \
  --arg engagementNumber "$ENGAGEMENT_NUMBER" \
  --arg domAssertion "$DOM_ASSERTION" \
  '{
    status:"passed",
    environment:"protected-test",
    deployedApplicationSource:$source,
    workflowBranchCommit:$workflowSha,
    identity:$identity,
    engagementId:$engagementId,
    engagementNumber:$engagementNumber,
    servedRouteBundle:true,
    domAssertion:$domAssertion,
    bootstrap:true,
    list:true,
    create:true,
    get:true,
    autosave:true,
    celarGenerate:true,
    confirm:true,
    sowDocx:true,
    gsdXlsx:true,
    reopen:true,
    archive:true,
    unarchive:true,
    finalState:"archived",
    manualCustomerDidNotCreateCanonicalClient:true,
    azureMutation:false,
    productionMutation:false
  }' > "$EVIDENCE_DIR/module025-live-uat-summary.json"

cat "$EVIDENCE_DIR/module025-live-uat-summary.json"
echo "MODULE025_PROTECTED_TEST_LIVE_UAT=PASS engagement=$ENGAGEMENT_NUMBER finalState=archived productionMutation=false"
