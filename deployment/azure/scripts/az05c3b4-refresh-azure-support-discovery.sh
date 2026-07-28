#!/usr/bin/env bash
set -Eeuo pipefail

SUBSCRIPTION_ID="cd32baeb-7b71-4bc0-8ea3-9f23a50903fe"
SERVICES_JSON="/tmp/azure-support-services.json"
CANDIDATES_TSV="/tmp/azure-support-service-candidates.tsv"
ERROR_LOG="/tmp/azure-support-services.stderr.log"
NEW_SERVICES="/tmp/azure-support-services.new.json"

section() {
    echo
    echo "============================================================"
    echo "$1"
    echo "============================================================"
}

fail() {
    echo "ERROR: $*" >&2
    exit 1
}

section "AZ-05C3B4 - Refresh Azure Support Discovery"

echo "TIME=$(date -u -Is)"
echo "SUBSCRIPTION_ID=$SUBSCRIPTION_ID"

az account set --subscription "$SUBSCRIPTION_ID"

CURRENT_SUBSCRIPTION_ID="$(az account show --query id --output tsv)"
[ "$CURRENT_SUBSCRIPTION_ID" = "$SUBSCRIPTION_ID" ] \
    || fail "Current subscription does not match the requested subscription."

echo "CURRENT_SUBSCRIPTION_MATCH=yes"

SUPPORT_PROVIDER_STATE="$(az provider show \
    --namespace Microsoft.Support \
    --query registrationState \
    --output tsv)"

[ "$SUPPORT_PROVIDER_STATE" = "Registered" ] \
    || fail "Microsoft.Support is not Registered: $SUPPORT_PROVIDER_STATE"

echo "SUPPORT_PROVIDER_STATE=$SUPPORT_PROVIDER_STATE"

SUPPORT_EXTENSION_VERSION="$(az extension show \
    --name support \
    --query version \
    --output tsv)"

[ -n "$SUPPORT_EXTENSION_VERSION" ] || fail "Azure support extension is not installed."
echo "SUPPORT_EXTENSION_VERSION=$SUPPORT_EXTENSION_VERSION"

section "Removing contaminated temporary discovery files"

rm -f \
    "$SERVICES_JSON" \
    "$CANDIDATES_TSV" \
    "$ERROR_LOG" \
    "$NEW_SERVICES" \
    /tmp/azure-support-classifications-*.json \
    /tmp/azure-support-classifications-*.stderr.log

echo "STALE_SUPPORT_DISCOVERY_FILES=removed"

section "Downloading support service catalog"

set +e
az support services list \
    --subscription "$SUBSCRIPTION_ID" \
    --only-show-errors \
    --output json \
    > "$NEW_SERVICES" \
    2> "$ERROR_LOG"
SERVICES_RC=$?
set -e

echo "SUPPORT_SERVICES_COMMAND_RC=$SERVICES_RC"
echo "SUPPORT_SERVICES_STDOUT_BYTES=$(wc -c < "$NEW_SERVICES")"
echo "SUPPORT_SERVICES_STDERR_BYTES=$(wc -c < "$ERROR_LOG")"

if [ "$SERVICES_RC" -ne 0 ]; then
    echo "SUPPORT_SERVICES_ERROR_BEGIN"
    sed -n '1,40p' "$ERROR_LOG"
    echo "SUPPORT_SERVICES_ERROR_END"
    fail "Azure support service catalog download failed."
fi

SERVICE_COUNT="$(python3 - "$NEW_SERVICES" <<'PY'
import json
import sys
from pathlib import Path

path = Path(sys.argv[1])
text = path.read_text(encoding="utf-8-sig")
obj = json.loads(text)
items = obj if isinstance(obj, list) else obj.get("value", [])
if not isinstance(items, list) or not items:
    raise SystemExit("Support service catalog is not a non-empty list.")
print(len(items))
PY
)"

[ "$SERVICE_COUNT" -gt 0 ] || fail "Support service catalog is empty."
mv "$NEW_SERVICES" "$SERVICES_JSON"

echo "SERVICES_JSON_VALID=yes"
echo "SUPPORT_SERVICE_COUNT=$SERVICE_COUNT"
echo "SERVICES_JSON=$SERVICES_JSON"

section "Selecting support service candidates"

python3 - "$SERVICES_JSON" "$CANDIDATES_TSV" <<'PY'
import json
import sys
from pathlib import Path

source = json.loads(Path(sys.argv[1]).read_text(encoding="utf-8-sig"))
items = source if isinstance(source, list) else source.get("value", [])
terms = (
    "postgres",
    "database for postgresql",
    "service and subscription limits",
    "subscription management",
    "quota",
)

matches = []
for item in items:
    display = str(item.get("displayName") or "")
    name = str(item.get("name") or "")
    resource_id = str(item.get("id") or "")
    haystack = f"{display} {name} {resource_id}".lower()
    if any(term in haystack for term in terms):
        matches.append((name, display, resource_id))

with Path(sys.argv[2]).open("w", encoding="utf-8") as handle:
    for name, display, resource_id in matches:
        handle.write(f"{name}\t{display}\t{resource_id}\n")

print(f"SUPPORT_SERVICE_CANDIDATE_COUNT={len(matches)}")
print("SUPPORT_SERVICE_CANDIDATES_BEGIN")
for name, display, resource_id in matches:
    print(f"name={name}")
    print(f"displayName={display}")
    print(f"id={resource_id}")
    print("---")
print("SUPPORT_SERVICE_CANDIDATES_END")
PY

CANDIDATE_COUNT="$(awk -F '\t' 'NF >= 2 {count++} END {print count+0}' "$CANDIDATES_TSV")"
[ "$CANDIDATE_COUNT" -gt 0 ] || fail "No matching Azure support service candidate was found."

section "Downloading problem classifications"

VALID_CLASSIFICATION_FILES=0
TOTAL_CLASSIFICATIONS=0

while IFS=$'\t' read -r SERVICE_NAME SERVICE_DISPLAY SERVICE_ID; do
    [ -n "$SERVICE_NAME" ] || continue

    SAFE_NAME="$(printf '%s' "$SERVICE_NAME" | tr -cd '[:alnum:]_-')"
    CLASS_JSON="/tmp/azure-support-classifications-${SAFE_NAME}.json"
    CLASS_NEW="${CLASS_JSON}.new"
    CLASS_ERROR="/tmp/azure-support-classifications-${SAFE_NAME}.stderr.log"

    echo "SERVICE_NAME=$SERVICE_NAME"
    echo "SERVICE_DISPLAY=$SERVICE_DISPLAY"

    set +e
    az support services problem-classifications list \
        --subscription "$SUBSCRIPTION_ID" \
        --service-name "$SERVICE_NAME" \
        --only-show-errors \
        --output json \
        > "$CLASS_NEW" \
        2> "$CLASS_ERROR"
    CLASS_RC=$?
    set -e

    echo "CLASSIFICATION_COMMAND_RC=$CLASS_RC"

    if [ "$CLASS_RC" -ne 0 ]; then
        echo "CLASSIFICATION_ERROR_BEGIN"
        sed -n '1,30p' "$CLASS_ERROR"
        echo "CLASSIFICATION_ERROR_END"
        continue
    fi

    CLASS_COUNT="$(python3 - "$CLASS_NEW" <<'PY'
import json
import sys
from pathlib import Path

obj = json.loads(Path(sys.argv[1]).read_text(encoding="utf-8-sig"))
items = obj if isinstance(obj, list) else obj.get("value", [])
if not isinstance(items, list):
    raise SystemExit("Problem classification response is not a list.")
print(len(items))
PY
    )"

    mv "$CLASS_NEW" "$CLASS_JSON"
    VALID_CLASSIFICATION_FILES=$((VALID_CLASSIFICATION_FILES + 1))
    TOTAL_CLASSIFICATIONS=$((TOTAL_CLASSIFICATIONS + CLASS_COUNT))

    echo "CLASSIFICATION_JSON_VALID=yes"
    echo "CLASSIFICATION_COUNT=$CLASS_COUNT"

    python3 - "$CLASS_JSON" <<'PY'
import json
import sys
from pathlib import Path

source = json.loads(Path(sys.argv[1]).read_text(encoding="utf-8-sig"))
items = source if isinstance(source, list) else source.get("value", [])
terms = ("quota", "limit", "region", "location", "subscription", "provision", "postgres")

print("RELEVANT_PROBLEM_CLASSIFICATIONS_BEGIN")
for item in items:
    display = str(item.get("displayName") or "")
    name = str(item.get("name") or "")
    resource_id = str(item.get("id") or "")
    haystack = f"{display} {name} {resource_id}".lower()
    if any(term in haystack for term in terms):
        print(f"name={name}")
        print(f"displayName={display}")
        print(f"id={resource_id}")
        print("---")
print("RELEVANT_PROBLEM_CLASSIFICATIONS_END")
PY

done < "$CANDIDATES_TSV"

echo "VALID_CLASSIFICATION_FILE_COUNT=$VALID_CLASSIFICATION_FILES"
echo "TOTAL_PROBLEM_CLASSIFICATIONS=$TOTAL_CLASSIFICATIONS"

[ "$VALID_CLASSIFICATION_FILES" -gt 0 ] \
    || fail "No valid problem-classification file was downloaded."

echo
echo "************************************************************"
echo "AZURE SUPPORT DISCOVERY REFRESH PASSED"
echo "************************************************************"
