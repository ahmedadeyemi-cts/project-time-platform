#!/usr/bin/env bash
set -euo pipefail

REPO_DIR="/opt/project-time-platform/app/project-time-platform"
PANEL_FILE="$REPO_DIR/src/frontend/project-time-web/src/ManagerApprovalPanel.jsx"
CSS_FILE="$REPO_DIR/src/frontend/project-time-web/src/manager-approval.css"

python3 - <<'PY'
from pathlib import Path

panel = Path('/opt/project-time-platform/app/project-time-platform/src/frontend/project-time-web/src/ManagerApprovalPanel.jsx')
text = panel.read_text()

text = text.replace("import { useEffect, useState } from 'react';", "import { useEffect, useMemo, useState } from 'react';")

if 'function itemKey(item)' not in text:
    text = text.replace("function statusLabel(status) {", "function itemKey(item) {\n  return `${item.timesheetId}|${item.workDate}`;\n}\n\nfunction statusLabel(status) {")

if 'const [selectedKeys, setSelectedKeys]' not in text:
    text = text.replace("  const [isWorking, setIsWorking] = useState(false);", "  const [isWorking, setIsWorking] = useState(false);\n  const [selectedKeys, setSelectedKeys] = useState(new Set());")

text = text.replace("      setApprovalData({ loading: false, data: result, error: null });", "      setApprovalData({ loading: false, data: result, error: null });\n      setSelectedKeys(new Set());")

if 'function toggleItemSelection(item)' not in text:
    insert = r'''
  function toggleItemSelection(item) {
    if (item.status !== 'submitted') return;
    const key = itemKey(item);
    setSelectedKeys((current) => {
      const next = new Set(current);
      if (next.has(key)) next.delete(key);
      else next.add(key);
      return next;
    });
  }

  function toggleAllPending() {
    const keys = pendingItems.map(itemKey);
    const allSelected = keys.length > 0 && keys.every((key) => selectedKeys.has(key));
    setSelectedKeys(allSelected ? new Set() : new Set(keys));
  }

  async function approveSelected() {
    if (selectedItems.length === 0 || isWorking) return;
    setIsWorking(true);
    setActionStatus(`Approving ${selectedItems.length} selected day(s)...`);

    try {
      const result = await postJson('/api/manager/approvals/bulk-approve', {
        items: selectedItems.map((item) => ({
          timesheetId: item.timesheetId,
          workDate: item.workDate,
          comment: 'Bulk approved by manager.'
        })),
        comment: 'Bulk approved by manager.'
      });
      setActionStatus(result.message ?? `Approved ${selectedItems.length} selected day(s).`);
      setSelectedKeys(new Set());
      await loadApprovals();
    } catch (error) {
      setActionStatus(error instanceof Error ? error.message : 'Bulk approval failed');
    } finally {
      setIsWorking(false);
    }
  }

'''
    text = text.replace("  const items = approvalData.data?.items ?? [];", insert + "  const items = approvalData.data?.items ?? [];")

if 'const pendingItems = useMemo' not in text:
    text = text.replace(
        "  const items = approvalData.data?.items ?? [];",
        "  const items = approvalData.data?.items ?? [];\n  const pendingItems = useMemo(() => items.filter((item) => item.status === 'submitted'), [items]);\n  const selectedItems = useMemo(() => items.filter((item) => selectedKeys.has(itemKey(item)) && item.status === 'submitted'), [items, selectedKeys]);\n  const allPendingSelected = pendingItems.length > 0 && pendingItems.every((item) => selectedKeys.has(itemKey(item)));"
    )

text = text.replace('<p className="eyebrow">Manager Approval</p>', '<p className="eyebrow">Approval Inbox</p>')

text = text.replace(
"""        <span>Items: <strong>{approvalData.loading ? 'Loading...' : items.length}</strong></span>
        <span>Action: <strong>{actionStatus}</strong></span>
      </div>
""",
"""        <span>Pending: <strong>{approvalData.loading ? 'Loading...' : pendingItems.length}</strong></span>
        <span>Selected: <strong>{selectedItems.length}</strong></span>
        <span>Action: <strong>{actionStatus}</strong></span>
      </div>

      {pendingItems.length > 0 ? (
        <div className="bulk-approval-bar">
          <div><strong>{pendingItems.length}</strong> pending item(s) for this week.</div>
          <div className="bulk-actions">
            <button type="button" onClick={toggleAllPending}>{allPendingSelected ? 'Clear selection' : 'Select all pending'}</button>
            <button type="button" className="approve-selected" onClick={approveSelected} disabled={selectedItems.length === 0 || isWorking}>
              Approve selected ({selectedItems.length})
            </button>
          </div>
        </div>
      ) : null}
""")

text = text.replace("""                <th>Resource</th>
                <th>Date</th>
""", """                <th>Select</th>
                <th>Resource</th>
                <th>Date</th>
""")

old_loop = """              {items.map((item) => (
                <tr key={`${item.timesheetId}-${item.workDate}`}>
                  <td>
                    <strong>{item.resourceName}</strong>
                    <span>{item.resourceEmail}</span>
                  </td>
"""
new_loop = """              {items.map((item) => {
                const key = itemKey(item);
                const canSelect = item.status === 'submitted';
                return (
                  <tr key={key}>
                    <td>
                      <input
                        aria-label={`Select ${item.resourceName} ${item.workDate}`}
                        type="checkbox"
                        checked={selectedKeys.has(key)}
                        disabled={!canSelect || isWorking}
                        onChange={() => toggleItemSelection(item)}
                      />
                    </td>
                    <td>
                      <strong>{item.resourceName}</strong>
                      <span>{item.resourceEmail}</span>
                    </td>
"""
text = text.replace(old_loop, new_loop)
text = text.replace("""                </tr>
              ))}
""", """                  </tr>
                );
              })}
""", 1)

panel.write_text(text)

css = Path('/opt/project-time-platform/app/project-time-platform/src/frontend/project-time-web/src/manager-approval.css')
styles = css.read_text()
if '.bulk-approval-bar' not in styles:
    styles += r'''

.bulk-approval-bar {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 14px;
  margin-bottom: 18px;
  border: 1px solid var(--border);
  border-radius: 16px;
  padding: 14px;
  background: var(--surface-strong);
  color: var(--muted);
}

.bulk-approval-bar strong {
  color: var(--text);
}

.bulk-actions {
  display: flex;
  flex-wrap: wrap;
  gap: 10px;
}

.bulk-actions button {
  border: 1px solid var(--border);
  border-radius: 999px;
  padding: 9px 12px;
  background: var(--surface);
  color: var(--muted);
  cursor: pointer;
  font-size: 0.8rem;
  font-weight: 900;
}

.bulk-actions .approve-selected {
  border-color: color-mix(in srgb, var(--brand-blue) 42%, var(--border));
  background: var(--brand-blue);
  color: #ffffff;
}

.bulk-actions .approve-selected:disabled {
  cursor: not-allowed;
  opacity: 0.5;
}
'''
    css.write_text(styles)
PY

echo "==> Approval inbox bulk UI patch applied"
