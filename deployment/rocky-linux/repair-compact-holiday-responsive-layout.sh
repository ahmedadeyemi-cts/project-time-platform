#!/usr/bin/env bash
set -euo pipefail

REPO_DIR="/opt/project-time-platform/app/project-time-platform"
APP_FILE="$REPO_DIR/src/frontend/project-time-web/src/App.jsx"
CSS_FILE="$REPO_DIR/src/frontend/project-time-web/src/timesheet.css"
DIST_DIR="$REPO_DIR/src/frontend/project-time-web/dist"

if [ ! -f "$APP_FILE" ]; then
  echo "ERROR: Missing $APP_FILE"
  exit 1
fi

if [ ! -f "$CSS_FILE" ]; then
  echo "ERROR: Missing $CSS_FILE"
  exit 1
fi

python3 - <<'PY'
from pathlib import Path
import re

app_file = Path('/opt/project-time-platform/app/project-time-platform/src/frontend/project-time-web/src/App.jsx')
css_file = Path('/opt/project-time-platform/app/project-time-platform/src/frontend/project-time-web/src/timesheet.css')
app = app_file.read_text()
css = css_file.read_text()

# 1) Holiday year dropdown: current year through current year + 10.
# For 2026 this displays 2026 through 2036.
app = re.sub(
    r"const holidayYearOptions = useMemo\(\(\) => \{\s*const currentYear = new Date\(\)\.getFullYear\(\);\s*return Array\.from\(\{ length: \d+ \}, \(_, index\) => String\(currentYear [^;]+\);\s*\}, \[\]\);",
    "const holidayYearOptions = useMemo(() => {\n    const currentYear = new Date().getFullYear();\n    return Array.from({ length: 11 }, (_, index) => String(currentYear + index));\n  }, []);",
    app,
    flags=re.S,
)

# If the previous useMemo was not present, add it near the activity source state.
if "const holidayYearOptions = useMemo" not in app:
    app = app.replace(
        "  const [activitySource, setActivitySource] = useState('nonProject');",
        "  const [activitySource, setActivitySource] = useState('nonProject');\n  const holidayYearOptions = useMemo(() => {\n    const currentYear = new Date().getFullYear();\n    return Array.from({ length: 11 }, (_, index) => String(currentYear + index));\n  }, []);",
        1,
    )

# 2) Make the holiday admin title/copy tighter if the section exists.
app = app.replace('Yearly paid holiday upload', 'Holiday calendar')
app = app.replace(
    'Select a year to view currently uploaded holidays, then upload or paste a CSV. Fixed holidays auto-populate 8.00 Holiday hours for eligible users when their selected week includes the holiday.',
    'View uploaded holidays by year, import a CSV, and keep company-paid holidays ready for automatic 8.00-hour Holiday entries.'
)
app = app.replace('Currently uploaded holidays', 'Uploaded holidays')

# 3) Add a compact class hook to the holiday list card when present.
app = app.replace('className="holiday-list-card"', 'className="holiday-list-card compact-holiday-list"')

# 4) Add a mobile hint above the time grid if not already present.
if 'timesheet-mobile-hint' not in app:
    app = app.replace(
        '<div className="entry-grid-wrap">',
        '<p className="timesheet-mobile-hint">Tip: on smaller screens, swipe horizontally to view all days and actions.</p>\n            <div className="entry-grid-wrap">',
        1,
    )

# 5) Append responsive/layout polish CSS. These rules intentionally override older styles.
css += r'''

/* Compact holiday administration + responsive timesheet layout refresh */
.holiday-admin-panel {
  padding: clamp(1.1rem, 1.8vw, 1.75rem) !important;
}

.holiday-admin-panel .section-header.compact {
  align-items: flex-start;
  gap: 1rem;
  margin-bottom: 0.9rem;
}

.holiday-admin-panel h2 {
  margin-bottom: 0.35rem;
}

.holiday-upload-grid {
  display: grid !important;
  grid-template-columns: minmax(120px, 180px) minmax(240px, 1fr) !important;
  gap: 0.85rem !important;
  align-items: end;
  margin-top: 0.85rem !important;
}

.holiday-upload-grid label {
  gap: 0.35rem !important;
  font-size: 0.9rem;
}

.holiday-upload-grid input,
.holiday-upload-grid select {
  min-height: 42px;
}

.holiday-upload-textarea {
  min-height: 92px !important;
  max-height: 150px;
  resize: vertical;
  margin-top: 0.85rem !important;
}

.holiday-upload-actions {
  margin-top: 0.75rem !important;
  gap: 0.8rem !important;
}

.compact-holiday-list {
  margin-top: 0.9rem !important;
  padding: 0.9rem !important;
  background: linear-gradient(180deg, rgba(248, 250, 252, 0.95), rgba(255, 255, 255, 0.98)) !important;
}

.compact-holiday-list .holiday-list-header {
  margin-bottom: 0.65rem !important;
  padding-bottom: 0.65rem;
  border-bottom: 1px solid var(--border-color, #d8dee8);
}

.compact-holiday-list .holiday-list-header h3 {
  font-size: 1.05rem;
}

.compact-holiday-list .module-list-row {
  display: grid !important;
  grid-template-columns: minmax(180px, 1.2fr) minmax(260px, 2fr);
  align-items: center;
  gap: 0.75rem;
  padding: 0.55rem 0 !important;
  border-top: 1px solid rgba(148, 163, 184, 0.22) !important;
}

.compact-holiday-list .module-list-row:first-of-type {
  border-top: 0 !important;
}

.compact-holiday-list .module-list-row strong {
  font-size: 0.95rem;
}

.compact-holiday-list .module-list-row span {
  font-size: 0.9rem;
  color: var(--muted-text, #5b6b89);
}

/* Make the time grid consume available width instead of leaving unused space after Action. */
.timesheet-page {
  overflow: hidden;
}

.timesheet-workspace {
  display: grid !important;
  grid-template-columns: minmax(220px, 280px) minmax(0, 1fr) !important;
  gap: 0.9rem !important;
  width: 100%;
}

.entry-grid-wrap {
  width: 100% !important;
  max-width: 100% !important;
  overflow-x: auto !important;
  border-radius: 1rem;
}

.entry-grid {
  width: 100% !important;
  min-width: 1180px !important;
}

.entry-grid-row {
  grid-template-columns:
    minmax(58px, 0.55fr)
    minmax(138px, 1.15fr)
    minmax(170px, 1.25fr)
    repeat(7, minmax(92px, 1fr))
    minmax(72px, 0.55fr)
    minmax(74px, 0.55fr) !important;
}

.time-cell-pair {
  display: grid !important;
  grid-template-columns: repeat(2, minmax(0, 1fr)) !important;
  gap: 0.35rem !important;
}

.time-entry-button {
  width: 100% !important;
  min-width: 0 !important;
  padding-left: 0.2rem !important;
  padding-right: 0.2rem !important;
}

.row-action-stack {
  justify-items: stretch !important;
  width: 100%;
}

.row-action-stack .link-button {
  width: 100%;
  text-align: center;
  white-space: normal;
  line-height: 1.15;
}

.timesheet-mobile-hint {
  display: none;
  margin: 0 0 0.6rem;
  color: var(--muted-text, #5b6b89);
  font-size: 0.9rem;
}

@media (max-width: 1100px) {
  .timesheet-workspace {
    grid-template-columns: 1fr !important;
  }

  .activities-panel {
    max-height: none !important;
  }

  .activity-results {
    display: grid;
    grid-template-columns: repeat(auto-fit, minmax(180px, 1fr));
    gap: 0.7rem;
  }

  .timesheet-mobile-hint {
    display: block;
  }
}

@media (max-width: 760px) {
  .app-shell {
    padding-left: 0.5rem !important;
    padding-right: 0.5rem !important;
  }

  .top-bar {
    position: sticky;
    top: 0;
    z-index: 20;
    flex-wrap: wrap;
    gap: 0.75rem;
  }

  .top-bar nav {
    order: 3;
    width: 100%;
    overflow-x: auto;
    justify-content: flex-start;
  }

  .timesheet-toolbar,
  .section-header.compact,
  .holiday-list-header {
    display: grid !important;
    grid-template-columns: 1fr !important;
    align-items: start !important;
  }

  .toolbar-actions {
    justify-content: flex-start !important;
    overflow-x: auto;
    padding-bottom: 0.25rem;
  }

  .holiday-upload-grid {
    grid-template-columns: 1fr !important;
  }

  .compact-holiday-list .module-list-row {
    grid-template-columns: 1fr !important;
    gap: 0.25rem;
  }

  .entry-grid {
    min-width: 980px !important;
  }
}
'''

app_file.write_text(app)
css_file.write_text(css)
PY

if [ -d "$DIST_DIR" ]; then
  rm -rf "$DIST_DIR"
fi

echo "==> Compact holiday and responsive layout repair applied"
echo "==> Holiday year dropdown now uses current year through current year + 10."
echo "==> Timesheet grid now uses available width and adapts for mobile/tablet."
