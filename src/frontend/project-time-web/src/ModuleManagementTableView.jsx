import { useCallback, useEffect, useMemo, useState } from 'react';

const TABLE_EXPERIENCE = 'table';
const EXPERIENCE_STORAGE_KEY = 'pulse-enterprise-experience';
const EXPERIENCE_EVENT = 'projectpulse:experience-changed';
const OWNER_EVENT = 'projectpulse:module-owner-changed';

function cleanText(value) {
  return String(value ?? '').replace(/\s+/g, ' ').trim();
}

function readLayout() {
  try {
    return cleanText(
      document.documentElement.dataset.pulseLayout
        || document.body?.dataset.pulseLayout
        || window.localStorage.getItem(EXPERIENCE_STORAGE_KEY)
    ).toLowerCase();
  } catch {
    return TABLE_EXPERIENCE;
  }
}

function useTableLayout() {
  const [layout, setLayout] = useState(readLayout);
  useEffect(() => {
    const synchronize = (event) => {
      const next = cleanText(event?.detail?.experience || readLayout()).toLowerCase();
      setLayout(next || TABLE_EXPERIENCE);
    };
    window.addEventListener(EXPERIENCE_EVENT, synchronize);
    window.addEventListener('storage', synchronize);
    window.addEventListener('pageshow', synchronize);
    return () => {
      window.removeEventListener(EXPERIENCE_EVENT, synchronize);
      window.removeEventListener('storage', synchronize);
      window.removeEventListener('pageshow', synchronize);
    };
  }, []);
  return layout === TABLE_EXPERIENCE;
}

async function readJson(response) {
  const raw = await response.text();
  if (!raw.trim()) return {};
  try {
    return JSON.parse(raw);
  } catch {
    return { message: raw };
  }
}

function displayTimestamp(value) {
  if (!value) return 'Not recorded';
  const parsed = new Date(value);
  if (Number.isNaN(parsed.getTime())) return 'Not recorded';
  return parsed.toLocaleString([], {
    month: 'short',
    day: 'numeric',
    year: 'numeric',
    hour: 'numeric',
    minute: '2-digit'
  });
}

function initials(value) {
  const parts = cleanText(value).split(' ').filter(Boolean);
  if (!parts.length) return '—';
  return parts.slice(0, 2).map((part) => part[0]?.toUpperCase() || '').join('');
}

function normalizedOwnership(body) {
  const owners = new Map();
  for (const owner of Array.isArray(body?.owners) ? body.owners : []) {
    const moduleNumber = cleanText(owner?.moduleNumber).toUpperCase();
    if (moduleNumber) owners.set(moduleNumber, owner);
  }
  return {
    loaded: true,
    owners,
    candidates: Array.isArray(body?.ownerCandidates) ? body.ownerCandidates : [],
    canManage: body?.access?.canManage === true,
    isViewAs: body?.access?.isViewAs === true,
    error: ''
  };
}

export default function ModuleManagementTableView({
  modules,
  availability,
  canManage,
  busyModule,
  onToggleModule
}) {
  const tableMode = useTableLayout();
  const [ownership, setOwnership] = useState({
    loaded: false,
    owners: new Map(),
    candidates: [],
    canManage: false,
    isViewAs: false,
    error: ''
  });
  const [busyOwnerModule, setBusyOwnerModule] = useState('');
  const [status, setStatus] = useState('');

  const loadOwnership = useCallback(async ({ preserveStatus = false } = {}) => {
    try {
      const response = await fetch('/api/module-catalog/owners', { cache: 'no-store' });
      const body = await readJson(response);
      if (!response.ok) throw new Error(body?.message || 'Module ownership could not be loaded.');
      setOwnership(normalizedOwnership(body));
      if (!preserveStatus) setStatus('');
    } catch (error) {
      setOwnership((current) => ({
        ...current,
        loaded: false,
        error: error?.message || 'Module ownership could not be loaded.'
      }));
    }
  }, []);

  useEffect(() => {
    if (!tableMode) return undefined;
    void loadOwnership();
    const refresh = () => void loadOwnership({ preserveStatus: true });
    window.addEventListener(OWNER_EVENT, refresh);
    window.addEventListener('projectpulse:view-as-changed', refresh);
    return () => {
      window.removeEventListener(OWNER_EVENT, refresh);
      window.removeEventListener('projectpulse:view-as-changed', refresh);
    };
  }, [loadOwnership, tableMode]);

  const candidates = useMemo(
    () => [...ownership.candidates].sort((left, right) => (
      cleanText(left?.displayName || left?.email).localeCompare(cleanText(right?.displayName || right?.email))
    )),
    [ownership.candidates]
  );

  async function changeOwner(module, ownerUserId) {
    if (!canManage || !ownership.canManage || !ownerUserId || busyOwnerModule) return;
    const current = ownership.owners.get(module.moduleNumber) || {};
    setBusyOwnerModule(module.moduleNumber);
    setStatus('');
    try {
      const response = await fetch(`/api/module-catalog/${encodeURIComponent(module.moduleNumber)}/owner`, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          ownerUserId,
          expectedRevision: Number(current.revision || 0)
        })
      });
      const body = await readJson(response);
      if (!response.ok) throw new Error(body?.message || `Module ${module.moduleNumber} owner could not be updated.`);
      const nextOwner = body?.owner || {};
      setOwnership((state) => {
        const owners = new Map(state.owners);
        owners.set(module.moduleNumber, nextOwner);
        return { ...state, owners };
      });
      setStatus(body?.message || `Module ${module.moduleNumber} owner updated.`);
      window.dispatchEvent(new CustomEvent(OWNER_EVENT, { detail: body }));
    } catch (error) {
      setOwnership((state) => ({
        ...state,
        error: error?.message || `Module ${module.moduleNumber} owner could not be updated.`
      }));
    } finally {
      setBusyOwnerModule('');
    }
  }

  if (!tableMode || !modules.length) return null;

  return (
    <section className="module-management-table-section" aria-label="Module management table">
      {ownership.error ? <div className="module-management-table-notice warning">{ownership.error}</div> : null}
      {status ? <div className="module-management-table-notice success">{status}</div> : null}
      {ownership.isViewAs ? (
        <div className="module-management-table-notice warning">View-As is read-only. Exit preview to change module ownership.</div>
      ) : null}

      <div className="module-management-table-scroll">
        <table className="module-management-table">
          <thead>
            <tr>
              <th scope="col">Module / Name</th>
              <th scope="col">Category</th>
              <th scope="col">Access Scope</th>
              <th scope="col">Availability</th>
              <th scope="col">Owner</th>
              <th scope="col">Last Updated</th>
              <th scope="col">Actions</th>
            </tr>
          </thead>
          <tbody>
            {modules.map((module) => {
              const owner = ownership.owners.get(module.moduleNumber) || {};
              const ownerName = cleanText(owner.displayName || owner.email) || 'Unassigned';
              const ownerEmail = cleanText(owner.email);
              const lastUpdated = owner.updatedAt || module.updatedAt;
              return (
                <tr key={module.route} data-module-number={module.moduleNumber}>
                  <td>
                    <div className="module-management-table-identity">
                      <span className="module-management-table-icon" aria-hidden="true">{module.moduleNumber?.slice(0, 3) || '—'}</span>
                      <div>
                        <strong className="module-management-table-number">{module.moduleNumber || '—'}</strong>
                        <a href={module.href || `#${module.route}`}>{module.label}</a>
                        <small>{module.description || `Open the ${module.label} workspace.`}</small>
                      </div>
                    </div>
                  </td>
                  <td><span className="module-management-table-category">{module.group}</span></td>
                  <td>
                    <span className="module-management-table-scope">
                      {availability?.access?.isSuperAdministrator ? 'Organization-wide' : 'Role-scoped'}
                    </span>
                  </td>
                  <td>
                    <span className={module.isEnabled ? 'module-management-state enabled' : 'module-management-state disabled'}>
                      {module.isEnabled ? 'Enabled' : 'Disabled'}
                    </span>
                  </td>
                  <td>
                    {(canManage && ownership.canManage) ? (
                      <label className="module-owner-editor">
                        <span className="module-owner-avatar" aria-hidden="true">{initials(ownerName)}</span>
                        <span className="sr-only">Owner for Module {module.moduleNumber}</span>
                        <select
                          value={owner.ownerUserId || ''}
                          disabled={busyOwnerModule === module.moduleNumber || !ownership.loaded}
                          onChange={(event) => void changeOwner(module, event.target.value)}
                          aria-label={`Owner for Module ${module.moduleNumber} ${module.label}`}
                        >
                          <option value="" disabled>Select owner</option>
                          {candidates.map((candidate) => (
                            <option value={candidate.userId} key={candidate.userId}>
                              {candidate.displayName || candidate.email} · {candidate.email}
                            </option>
                          ))}
                        </select>
                      </label>
                    ) : (
                      <span className="module-owner-readonly">
                        <span className="module-owner-avatar" aria-hidden="true">{initials(ownerName)}</span>
                        <span><strong>{ownerName}</strong>{ownerEmail ? <small>{ownerEmail}</small> : null}</span>
                      </span>
                    )}
                  </td>
                  <td><time dateTime={lastUpdated || undefined}>{displayTimestamp(lastUpdated)}</time></td>
                  <td>
                    <div className="module-management-table-actions">
                      <a href={module.href || `#${module.route}`} aria-label={`Open Module ${module.moduleNumber} — ${module.label}`}>Open ↗</a>
                      {canManage ? (
                        <button
                          type="button"
                          disabled={Boolean(busyModule)}
                          onClick={() => void onToggleModule(module)}
                          className={module.isEnabled ? 'danger' : 'enable'}
                        >
                          {busyModule === module.moduleNumber ? 'Saving…' : module.isEnabled ? 'Disable' : 'Enable'}
                        </button>
                      ) : null}
                    </div>
                  </td>
                </tr>
              );
            })}
          </tbody>
        </table>
      </div>
    </section>
  );
}
