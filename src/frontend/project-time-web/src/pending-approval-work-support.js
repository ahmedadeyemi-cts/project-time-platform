export const TARGET_STORAGE_KEY = 'projectPulsePendingApprovalTarget';

export const STAGES = Object.freeze({
  manager: {
    label: 'Manager review',
    shortLabel: 'Manager',
    help: 'Submitted days awaiting the employee’s manager.'
  },
  pm: {
    label: 'PM review',
    shortLabel: 'PM',
    help: 'Manager-approved project time awaiting the Project Manager.'
  },
  ptc: {
    label: 'PTC final review',
    shortLabel: 'PTC',
    help: 'PM-approved time awaiting final Project Team Coordinator review.'
  }
});

function getAuthHeaders() {
  try {
    const raw = window.localStorage.getItem('projectPulseAuthSession');
    const session = raw ? JSON.parse(raw) : null;
    return session?.sessionToken
      ? { 'X-ProjectPulse-Session': session.sessionToken }
      : {};
  } catch {
    return {};
  }
}

async function readResponse(response, path) {
  const raw = await response.text();
  let payload = {};
  try {
    payload = raw ? JSON.parse(raw) : {};
  } catch {
    payload = {};
  }
  if (!response.ok) {
    const error = new Error(
      payload.message || payload.detail || raw || `${path} returned HTTP ${response.status}.`
    );
    error.status = response.status;
    throw error;
  }
  return payload;
}

export async function fetchPendingWork() {
  const path = '/api/approval-work/pending';
  const response = await fetch(path, {
    headers: getAuthHeaders(),
    cache: 'no-store'
  });
  return readResponse(response, path);
}

export async function completePendingWork(payload) {
  const path = '/api/approval-work/bulk-complete';
  const response = await fetch(path, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      ...getAuthHeaders()
    },
    body: JSON.stringify(payload)
  });
  return readResponse(response, path);
}

export function readPendingTarget() {
  const raw = String(window.location.hash || '#dashboard').replace(/^#/, '');
  const [route, query = ''] = raw.split('?');
  const params = new URLSearchParams(query);
  const queryStage = params.get('pendingStage') || '';
  const queryWeekStart = params.get('weekStart') || '';

  if (queryStage && queryWeekStart) {
    return { route, pendingStage: queryStage, weekStart: queryWeekStart };
  }

  try {
    const stored = JSON.parse(window.sessionStorage.getItem(TARGET_STORAGE_KEY) || 'null');
    return {
      route,
      pendingStage: String(stored?.pendingStage || ''),
      weekStart: String(stored?.weekStart || '')
    };
  } catch {
    return { route, pendingStage: '', weekStart: '' };
  }
}

export function rememberPendingTarget(stage, weekStart) {
  try {
    window.sessionStorage.setItem(TARGET_STORAGE_KEY, JSON.stringify({
      pendingStage: stage,
      weekStart
    }));
  } catch {
    // Navigation still works when session storage is unavailable.
  }
}

export function itemKey(item) {
  return `${item.timesheetId}|${item.workDate}`;
}

export function groupKey(stage, weekStart) {
  return `${stage}|${weekStart}`;
}

export function displayDate(value, options = {}) {
  if (!value) return 'Unknown date';
  const date = new Date(`${value}T12:00:00`);
  if (Number.isNaN(date.getTime())) return value;
  return date.toLocaleDateString(undefined, {
    month: 'short',
    day: 'numeric',
    year: options.year === false ? undefined : 'numeric'
  });
}

export function displayDateTime(value) {
  if (!value) return 'Not recorded';
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? value : date.toLocaleString();
}

export function formatHours(value) {
  return Number(value || 0).toLocaleString(undefined, {
    minimumFractionDigits: 0,
    maximumFractionDigits: 2
  });
}

export function ensureHost(id, parent, beforeNode = null) {
  if (!parent) return null;
  let host = document.getElementById(id);
  if (!host) {
    host = document.createElement('div');
    host.id = id;
    host.dataset.projectpulsePendingApprovalHost = 'true';
    if (beforeNode) parent.insertBefore(host, beforeNode);
    else parent.appendChild(host);
  }
  return host;
}
