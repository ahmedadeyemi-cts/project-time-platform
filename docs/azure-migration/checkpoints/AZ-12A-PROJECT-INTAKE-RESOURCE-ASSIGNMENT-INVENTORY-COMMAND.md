# AZ-12A — Project Intake and Resource Assignment Inventory Command

Run the canonical read-only script from Azure Cloud Shell:

```bash
BRANCH="azure-migration/project-health-dashboard-foundation"
SCRIPT_PATH="deployment/azure/scripts/az12a-project-intake-resource-assignment-source-inventory.sh"
LOCAL_SCRIPT="/tmp/phd-azure-az12a-project-intake-resource-assignment-source-inventory.sh"

rm -f "$LOCAL_SCRIPT"

gh api \
  -H "Accept: application/vnd.github.raw+json" \
  "repos/ahmedadeyemi-cts/project-time-platform/contents/${SCRIPT_PATH}?ref=${BRANCH}" \
  > "$LOCAL_SCRIPT"

DOWNLOAD_RC=$?

if [ "$DOWNLOAD_RC" -ne 0 ] || [ ! -s "$LOCAL_SCRIPT" ]; then
    echo "ERROR: AZ-12A inventory script download failed. Cloud Shell remains open."
else
    chmod +x "$LOCAL_SCRIPT"

    if bash -n "$LOCAL_SCRIPT"; then
        bash "$LOCAL_SCRIPT"
    else
        echo "ERROR: AZ-12A syntax validation failed. Cloud Shell remains open."
    fi
fi
```
