import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import IdentityAvatar from './identity/IdentityAvatar.jsx';
import module006CustomerBrands from './assets/module-006-customer-brands.svg';

const TABLE_EXPERIENCE = 'table';
const EXPERIENCE_STORAGE_KEY = 'pulse-enterprise-experience';
const EXPERIENCE_EVENT = 'projectpulse:experience-changed';
const OWNER_EVENT = 'projectpulse:module-owner-changed';
const PROFILE_EVENTS = [
  'projectpulse:identity-profile-changed',
  'projectpulse:profile-preferences-changed'
];
const MODULE_006_NUMBER = '006';
const MODULE_006_NAME = 'Customer Programs';
const MODULE_006_DESCRIPTION = 'Unified workspace for Toyota, Hyundai, and Turion Space programs, documents, and collaboration.';

const DETAIL_TABS = Object.freeze([
  { id: 'overview', label: 'Overview' },
  { id: 'access', label: 'Access' },
  { id: 'configuration', label: 'Configuration' },
  { id: 'history', label: 'History' }
]);

function cleanText(value) {
  return String(value ?? '').replace(/\s+/g, ' ').trim();
}

function normalizeEmail(value) {
  return cleanText(value).toLowerCase();
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

function profilePayload(body) {
  return body?.profile || body?.identity || body || {};
}

function preferencePayload(body) {
  return body?.preferences || body || {};
}

async function loadSignedInProfile(signal) {
  const [identityResult, preferencesResult] = await Promise.allSettled([
    fetch('/api/identity/profile', {
      cache: 'no-store',
      credentials: 'include',
      signal
    }).then(async (response) => ({ response, body: await readJson(response) })),
    fetch('/api/profile/preferences', {
      cache: 'no-store',
      credentials: 'include',
      signal
    }).then(async (response) => ({ response, body: await readJson(response) }))
  ]);

  const identity = identityResult.status === 'fulfilled' && identityResult.value.response.ok
    ? profilePayload(identityResult.value.body)
    : {};
  const preferences = preferencesResult.status === 'fulfilled' && preferencesResult.value.response.ok
    ? preferencePayload(preferencesResult.value.body)
    : {};

  return {
    ...identity,
    userId: identity.userId || identity.id || identity.profileUserId || '',
    email: identity.email || identity.userPrincipalName || identity.username || '',
    displayName: identity.displayName || identity.name || identity.email || '',
    profilePhotoDataUrl: preferences.profilePhotoDataUrl
      || identity.profilePhotoDataUrl
      || identity.profilePhoto
      || identity.photoUrl
      || ''
  };
}

function ownerAvatarProfile(owner, signedInProfile) {
  const ownerId = cleanText(owner?.ownerUserId).toLowerCase();
  const profileId = cleanText(signedInProfile?.userId).toLowerCase();
  const ownerEmail = normalizeEmail(owner?.email);
  const profileEmail = normalizeEmail(signedInProfile?.email);
  const isSignedInOwner = Boolean(
    (ownerId && profileId && ownerId === profileId)
      || (ownerEmail && profileEmail && ownerEmail === profileEmail)
  );

  return {
    displayName: cleanText(owner?.displayName || owner?.email) || 'Unassigned',
    email: cleanText(owner?.email),
    profilePhotoDataUrl: isSignedInOwner ? cleanText(signedInProfile?.profilePhotoDataUrl) : ''
  };
}

function eventTargetsInteractiveControl(event) {
  return Boolean(event.target?.closest?.('a, button, input, select, textarea, label, summary'));
}

function moduleLink(module) {
  const explicitHref = cleanText(module?.href);
  if (explicitHref) return explicitHref.startsWith('#') ? explicitHref : `#${explicitHref}`;
  const route = cleanText(module?.route).replace(/^#/, '');
  return route ? `#${route}` : '#modules';
}

function moduleDisplayName(module) {
  return module?.moduleNumber === MODULE_006_NUMBER
    ? MODULE_006_NAME
    : cleanText(module?.label) || 'Module';
}

function moduleDescription(module) {
  if (module?.moduleNumber === MODULE_006_NUMBER) return MODULE_006_DESCRIPTION;
  return cleanText(module?.description) || `Open the ${moduleDisplayName(module)} workspace.`;
}

function accessScopeLabel(availability) {
  return availability?.access?.isSuperAdministrator ? 'Organization-wide' : 'Role-scoped';
}

function DetailIcon({ module }) {
  return (
    <span className="module-detail-icon" aria-hidden="true">
      {module?.moduleNumber?.slice(0, 3) || '—'}
    </span>
  );
}

export default function ModuleManagementTableView({
  modules,
  availability,
  canManage,
  busyModule,
  onToggleModule
}) {
  const tableMode = useTableLayout();
  const closeButtonRef = useRef(null);
  const [ownership, setOwnership] = useState({
    loaded: false,
    owners: new Map(),
    candidates: [],
    canManage: false,
    isViewAs: false,
    error: ''
  });
  const [signedInProfile, setSignedInProfile] = useState({});
  const [selectedModuleNumber, setSelectedModuleNumber] = useState('');
  const [activeTab, setActiveTab] = useState('overview');
  const [busyOwnerModule, setBusyOwnerModule] = useState('');
  const [status, setStatus] = useState('');

  const loadOwnership = useCallback(async ({ preserveStatus = false } = {}) => {
    try {
      const response = await fetch('/api/module-catalog/owners', {
        cache: 'no-store',
        credentials: 'include'
      });
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

  const refreshSignedInProfile = useCallback(() => {
    const controller = new AbortController();
    void loadSignedInProfile(controller.signal)
      .then(setSignedInProfile)
      .catch((error) => {
        if (error?.name !== 'AbortError') setSignedInProfile({});
      });
    return () => controller.abort();
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

  useEffect(() => {
    if (!tableMode) return undefined;
    let cancelCurrent = refreshSignedInProfile();
    const refresh = () => {
      cancelCurrent?.();
      cancelCurrent = refreshSignedInProfile();
    };
    PROFILE_EVENTS.forEach((eventName) => window.addEventListener(eventName, refresh));
    return () => {
      cancelCurrent?.();
      PROFILE_EVENTS.forEach((eventName) => window.removeEventListener(eventName, refresh));
    };
  }, [refreshSignedInProfile, tableMode]);

  const candidates = useMemo(
    () => [...ownership.candidates].sort((left, right) => (
      cleanText(left?.displayName || left?.email).localeCompare(cleanText(right?.displayName || right?.email))
    )),
    [ownership.candidates]
  );

  const selectedModule = useMemo(
    () => modules.find((module) => module.moduleNumber === selectedModuleNumber) || null,
    [modules, selectedModuleNumber]
  );

  useEffect(() => {
    if (selectedModuleNumber && !selectedModule) setSelectedModuleNumber('');
  }, [selectedModule, selectedModuleNumber]);

  const closeDetailPanel = useCallback(() => {
    const moduleNumber = selectedModuleNumber;
    setSelectedModuleNumber('');
    window.requestAnimationFrame(() => {
      const selector = `[data-module-number="${CSS.escape(moduleNumber)}"]`;
      document.querySelector(selector)?.focus?.();
    });
  }, [selectedModuleNumber]);

  useEffect(() => {
    if (!selectedModule) return undefined;
    window.requestAnimationFrame(() => closeButtonRef.current?.focus?.());
    const onKeyDown = (event) => {
      if (event.key !== 'Escape') return;
      event.preventDefault();
      closeDetailPanel();
    };
    window.addEventListener('keydown', onKeyDown);
    return () => window.removeEventListener('keydown', onKeyDown);
  }, [closeDetailPanel, selectedModule]);

  function selectModule(module) {
    setSelectedModuleNumber(module.moduleNumber);
    setActiveTab('overview');
  }

  async function changeOwner(module, ownerUserId) {
    if (!canManage || !ownership.canManage || !ownerUserId || busyOwnerModule) return;
    const current = ownership.owners.get(module.moduleNumber) || {};
    setBusyOwnerModule(module.moduleNumber);
    setStatus('');
    try {
      const response = await fetch(`/api/module-catalog/${encodeURIComponent(module.moduleNumber)}/owner`, {
        method: 'PUT',
        credentials: 'include',
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
        return { ...state, owners, error: '' };
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

  async function copyModuleLink(module) {
    const link = new URL(moduleLink(module), window.location.href).href;
    try {
      await navigator.clipboard.writeText(link);
      setStatus(`Module ${module.moduleNumber} link copied.`);
    } catch {
      window.prompt('Copy this module link:', link);
    }
  }

  if (!tableMode || !modules.length) return null;

  const selectedOwner = selectedModule
    ? (ownership.owners.get(selectedModule.moduleNumber) || {})
    : {};
  const selectedOwnerProfile = ownerAvatarProfile(selectedOwner, signedInProfile);
  const selectedLastUpdated = selectedOwner.updatedAt || selectedModule?.updatedAt;
  const viewAsReadOnly = ownership.isViewAs || availability?.access?.isViewAs === true;
  const canChangeOwner = canManage && ownership.canManage && !viewAsReadOnly;

  return (
    <section className="module-management-table-section" aria-label="Module management table">
      {ownership.error ? (
        <div className="module-management-table-notice warning" role="alert">
          <span>{ownership.error}</span>
          <button type="button" onClick={() => void loadOwnership()}>Retry ownership</button>
        </div>
      ) : null}
      {status ? <div className="module-management-table-notice success" role="status">{status}</div> : null}
      {viewAsReadOnly ? (
        <div className="module-management-table-notice warning">View-As is read-only. Exit preview to change module ownership.</div>
      ) : null}

      <div className={selectedModule ? 'module-management-table-workspace has-detail-panel' : 'module-management-table-workspace'}>
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
                const ownerProfile = ownerAvatarProfile(owner, signedInProfile);
                const ownerName = ownerProfile.displayName;
                const ownerEmail = ownerProfile.email;
                const lastUpdated = owner.updatedAt || module.updatedAt;
                const isSelected = selectedModuleNumber === module.moduleNumber;
                return (
                  <tr
                    key={module.route}
                    data-module-number={module.moduleNumber}
                    className={isSelected ? 'selected' : ''}
                    tabIndex={0}
                    role="button"
                    aria-selected={isSelected}
                    aria-expanded={isSelected}
                    aria-controls="module-management-detail-panel"
                    onClick={(event) => {
                      if (!eventTargetsInteractiveControl(event)) selectModule(module);
                    }}
                    onKeyDown={(event) => {
                      if (eventTargetsInteractiveControl(event)) return;
                      if (event.key === 'Enter' || event.key === ' ') {
                        event.preventDefault();
                        selectModule(module);
                      }
                    }}
                  >
                    <td>
                      <div className="module-management-table-identity">
                        <span className="module-management-table-icon" aria-hidden="true">{module.moduleNumber?.slice(0, 3) || '—'}</span>
                        <div>
                          <strong className="module-management-table-number">{module.moduleNumber || '—'}</strong>
                          <a href={moduleLink(module)}>{moduleDisplayName(module)}</a>
                          <small>{moduleDescription(module)}</small>
                        </div>
                      </div>
                    </td>
                    <td><span className="module-management-table-category">{module.group}</span></td>
                    <td><span className="module-management-table-scope">{accessScopeLabel(availability)}</span></td>
                    <td>
                      <span className={module.isEnabled ? 'module-management-state enabled' : 'module-management-state disabled'}>
                        {module.isEnabled ? 'Enabled' : 'Disabled'}
                      </span>
                    </td>
                    <td>
                      <span className="module-owner-readonly">
                        <span className="module-owner-avatar-shell">
                          <IdentityAvatar profile={ownerProfile} size="small" showPresence={false} />
                        </span>
                        <span>
                          <strong>{ownerName}</strong>
                          {ownerEmail ? <small>{ownerEmail}</small> : null}
                        </span>
                      </span>
                    </td>
                    <td><time dateTime={lastUpdated || undefined}>{displayTimestamp(lastUpdated)}</time></td>
                    <td>
                      <div className="module-management-table-actions">
                        <a href={moduleLink(module)} aria-label={`Open Module ${module.moduleNumber} — ${moduleDisplayName(module)}`}>Open ↗</a>
                        <button type="button" className="details" onClick={() => selectModule(module)}>Details</button>
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

        {selectedModule ? (
          <>
            <button
              type="button"
              className="module-management-drawer-backdrop"
              aria-label="Close module details"
              onClick={closeDetailPanel}
            />
            <aside
              id="module-management-detail-panel"
              className="module-management-detail-panel"
              role="dialog"
              aria-modal="true"
              aria-labelledby="module-management-detail-title"
            >
              <header className="module-management-detail-header">
                <div className="module-management-detail-title-row">
                  <DetailIcon module={selectedModule} />
                  <div>
                    <span>Module {selectedModule.moduleNumber}</span>
                    <h2 id="module-management-detail-title">{moduleDisplayName(selectedModule)}</h2>
                  </div>
                  <span className={selectedModule.isEnabled ? 'module-management-state enabled' : 'module-management-state disabled'}>
                    {selectedModule.isEnabled ? 'Enabled' : 'Disabled'}
                  </span>
                  <button
                    ref={closeButtonRef}
                    type="button"
                    className="module-management-detail-close"
                    aria-label="Close module details"
                    onClick={closeDetailPanel}
                  >
                    ×
                  </button>
                </div>
                {selectedModule.moduleNumber === MODULE_006_NUMBER ? (
                  <div className="module-management-customer-brands">
                    <img src={module006CustomerBrands} alt="Toyota, Hyundai, and Turion Space" />
                  </div>
                ) : null}
              </header>

              <div className="module-management-detail-tabs" role="tablist" aria-label="Module details">
                {DETAIL_TABS.map((tab) => (
                  <button
                    type="button"
                    id={`module-detail-tab-${tab.id}`}
                    key={tab.id}
                    role="tab"
                    aria-selected={activeTab === tab.id}
                    aria-controls={`module-detail-panel-${tab.id}`}
                    tabIndex={activeTab === tab.id ? 0 : -1}
                    onClick={() => setActiveTab(tab.id)}
                  >
                    {tab.label}
                  </button>
                ))}
              </div>

              {activeTab === 'overview' ? (
                <div
                  id="module-detail-panel-overview"
                  role="tabpanel"
                  aria-labelledby="module-detail-tab-overview"
                  className="module-management-detail-content"
                >
                  <section className="module-detail-card">
                    <h3>Overview</h3>
                    <p>{moduleDescription(selectedModule)}</p>
                    <dl>
                      <div><dt>Category</dt><dd>{selectedModule.group}</dd></div>
                      <div><dt>Route</dt><dd>{moduleLink(selectedModule)}</dd></div>
                      <div>
                        <dt>Module owner</dt>
                        <dd className="module-detail-owner">
                          <IdentityAvatar profile={selectedOwnerProfile} size="small" showPresence={false} />
                          <span>{selectedOwnerProfile.displayName}</span>
                        </dd>
                      </div>
                      <div><dt>Access scope</dt><dd>{accessScopeLabel(availability)}</dd></div>
                      <div><dt>Last updated</dt><dd>{displayTimestamp(selectedLastUpdated)}</dd></div>
                    </dl>
                  </section>

                  <section className="module-detail-card">
                    <h3>Availability</h3>
                    <p className={selectedModule.isEnabled ? 'module-detail-availability enabled' : 'module-detail-availability disabled'}>
                      {selectedModule.isEnabled ? 'Enabled' : 'Disabled'}
                    </p>
                    <p>{selectedModule.isEnabled
                      ? 'This module is available to users who are authorized by the existing role and scope policy.'
                      : 'This module is disabled. Its existing role and scope policy remains unchanged.'}</p>
                    {canManage ? (
                      <button
                        type="button"
                        className={selectedModule.isEnabled ? 'module-detail-danger' : 'module-detail-primary'}
                        disabled={Boolean(busyModule)}
                        onClick={() => void onToggleModule(selectedModule)}
                      >
                        {busyModule === selectedModule.moduleNumber
                          ? 'Saving…'
                          : selectedModule.isEnabled ? 'Disable module' : 'Enable module'}
                      </button>
                    ) : null}
                  </section>

                  <section className="module-detail-card">
                    <h3>Quick actions</h3>
                    <div className="module-detail-actions">
                      <a href="#role-admin">Configure access</a>
                      <a href="#audit-history">Audit History</a>
                      <button type="button" onClick={() => setActiveTab('configuration')}>Review configuration</button>
                      <button type="button" onClick={() => void copyModuleLink(selectedModule)}>Copy Module Link</button>
                    </div>
                  </section>
                </div>
              ) : null}

              {activeTab === 'access' ? (
                <div
                  id="module-detail-panel-access"
                  role="tabpanel"
                  aria-labelledby="module-detail-tab-access"
                  className="module-management-detail-content"
                >
                  <section className="module-detail-card">
                    <h3>Access</h3>
                    <dl>
                      <div><dt>Effective scope</dt><dd>{accessScopeLabel(availability)}</dd></div>
                      <div><dt>Availability</dt><dd>{selectedModule.isEnabled ? 'Enabled' : 'Disabled'}</dd></div>
                      <div><dt>View-As</dt><dd>{viewAsReadOnly ? 'Read-only preview active' : 'Inactive'}</dd></div>
                    </dl>
                    <p className="module-detail-policy-note">Module ownership is accountability metadata only. It never grants module, record, team, department, or organization access.</p>
                    <a className="module-detail-primary-link" href="#role-admin">Configure Access</a>
                  </section>
                </div>
              ) : null}

              {activeTab === 'configuration' ? (
                <div
                  id="module-detail-panel-configuration"
                  role="tabpanel"
                  aria-labelledby="module-detail-tab-configuration"
                  className="module-management-detail-content"
                >
                  <section className="module-detail-card">
                    <h3>Module owner</h3>
                    <div className="module-detail-owner-summary">
                      <IdentityAvatar profile={selectedOwnerProfile} size="medium" showPresence={false} />
                      <div>
                        <strong>{selectedOwnerProfile.displayName}</strong>
                        {selectedOwnerProfile.email ? <span>{selectedOwnerProfile.email}</span> : null}
                      </div>
                    </div>
                    {canChangeOwner ? (
                      <label className="module-detail-owner-editor">
                        <span>Change owner</span>
                        <select
                          value={selectedOwner.ownerUserId || ''}
                          disabled={busyOwnerModule === selectedModule.moduleNumber || !ownership.loaded}
                          onChange={(event) => void changeOwner(selectedModule, event.target.value)}
                          aria-label={`Owner for Module ${selectedModule.moduleNumber} ${moduleDisplayName(selectedModule)}`}
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
                      <p>Only an actual Super Administrator session can change module ownership. View-As remains read-only.</p>
                    )}
                  </section>
                </div>
              ) : null}

              {activeTab === 'history' ? (
                <div
                  id="module-detail-panel-history"
                  role="tabpanel"
                  aria-labelledby="module-detail-tab-history"
                  className="module-management-detail-content"
                >
                  <section className="module-detail-card">
                    <h3>Recorded ownership state</h3>
                    <dl>
                      <div><dt>Owner</dt><dd>{selectedOwnerProfile.displayName}</dd></div>
                      <div><dt>Revision</dt><dd>{Number(selectedOwner.revision || 0)}</dd></div>
                      <div><dt>Last updated</dt><dd>{displayTimestamp(selectedLastUpdated)}</dd></div>
                    </dl>
                    <p>Immutable ownership changes remain available in Audit History.</p>
                    <a className="module-detail-primary-link" href="#audit-history">Audit History</a>
                  </section>
                </div>
              ) : null}
            </aside>
          </>
        ) : null}
      </div>
    </section>
  );
}
