import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import IdentityAvatar from './identity/IdentityAvatar.jsx';
import module006CustomerBrands from './assets/module-006-customer-brands.svg';

const TABLE_EXPERIENCE = 'table';
const CLASSIC_EXPERIENCE = 'classic';
const EXPERIENCE_STORAGE_KEY = 'pulse-enterprise-experience';
const EXPERIENCE_EVENT = 'projectpulse:experience-changed';
const OWNER_EVENT = 'projectpulse:module-owner-changed';
const PROFILE_EVENTS = [
  'projectpulse:identity-profile-changed',
  'projectpulse:profile-preferences-changed'
];
const MODULE_006_NUMBER = '006';
const MODULE_006_NAME = 'Customer Programs';
const MODULE_006_DESCRIPTION = 'Pipeline management and reporting for Toyota, Hyundai, Turion Space, and other authorized customer programs.';
const DEFAULT_PAGE_SIZE = 10;
const OWNER_CATALOG_READ_CONTRACT = 'OWNER_CATALOG_READ_THROUGH_FOR_AUTHENTICATED_USERS_V1';
const OWNER_LOAD_RETRY_DELAYS_MS = Object.freeze([0, 250, 750, 1500]);
const OWNER_LOAD_RETRYABLE_STATUS = new Set([401, 408, 425, 429, 502, 503, 504]);

const DETAIL_TABS = Object.freeze([
  { id: 'overview', label: 'Overview' },
  { id: 'access', label: 'Access' },
  { id: 'configuration', label: 'Configuration' },
  { id: 'history', label: 'History' }
]);

const VIEW_DEFINITIONS = Object.freeze([
  { id: 'all', label: 'All Modules', icon: 'modules' },
  { id: 'available', label: 'My Available Modules', icon: 'users' },
  { id: 'customer', label: 'Customer Solutions', icon: 'customer' },
  { id: 'core', label: 'Core Operations', icon: 'building' },
  { id: 'project', label: 'Project Management', icon: 'project' },
  { id: 'administration', label: 'Administration', icon: 'admin' },
  { id: 'recent', label: 'Recently Updated', icon: 'clock' },
  { id: 'disabled', label: 'Disabled Modules', icon: 'disabled' }
]);

const DEFAULT_COLUMNS = Object.freeze({
  category: true,
  scope: true,
  availability: true,
  owner: true,
  updated: true,
  actions: true
});

const ADMIN_MODULES = new Set([
  '004', '008', '009', '010', '012', '013', '014', '015', '016', '017',
  '029', '037', '038', '058', '064', '065', '067', '068', '071', '072',
  '074', '075', '077', '078', '079', '081', '083', '997', '998'
]);

const PROJECT_MODULES = new Set([
  '005', '018', '019', '020', '027', '033', '039', '040', '041', '042',
  '055B', '055C', '055D', '057', '060', '066', '070', '080', '082'
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

function applyLayout(nextLayout) {
  const normalized = nextLayout === CLASSIC_EXPERIENCE ? CLASSIC_EXPERIENCE : TABLE_EXPERIENCE;
  try {
    window.localStorage.setItem(EXPERIENCE_STORAGE_KEY, normalized);
  } catch {
    // Hardened browser storage is optional; DOM state remains authoritative.
  }

  const presentation = normalized === TABLE_EXPERIENCE ? 'enterprise' : normalized;
  document.documentElement.dataset.pulseLayout = normalized;
  document.documentElement.dataset.pulseExperience = presentation;
  if (document.body) {
    document.body.dataset.pulseLayout = normalized;
    document.body.dataset.pulseExperience = presentation;
  }
  window.dispatchEvent(new CustomEvent(EXPERIENCE_EVENT, {
    detail: { experience: normalized }
  }));
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

function wait(milliseconds) {
  return new Promise((resolve) => window.setTimeout(resolve, milliseconds));
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
    canManage: body?.access?.canManageOwners === true || body?.access?.canManage === true,
    isViewAs: body?.access?.isViewAs === true,
    authoritySource: cleanText(body?.access?.authoritySource),
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
  return Boolean(event.target?.closest?.('a, button, input, select, textarea, label, summary, details'));
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

function accessScopeLabel(module) {
  const number = cleanText(module?.moduleNumber).toUpperCase();
  if (number === '001') return 'Organization-wide';
  if (number === '001A') return 'Engineers & Leads';
  if (number === '002' || number === '007') return 'Approver roles';
  if (number === '003') return 'Assigned scope';
  if (number === '006') return 'Project Managers';
  if (ADMIN_MODULES.has(number)) return 'Administrators';
  if (PROJECT_MODULES.has(number)) return 'Project teams';
  if (/Sales|Customer/i.test(cleanText(module?.group))) return 'Authorized commercial roles';
  return 'Authorized roles';
}

function assignedRoleLabel(module) {
  const number = cleanText(module?.moduleNumber).toUpperCase();
  if (number === '001A') return 'Engineer / Engineering Lead';
  if (number === '002' || number === '007') return 'Approver';
  if (number === '003') return 'Engineer / Leadership';
  if (number === '006') return 'Project Manager';
  if (ADMIN_MODULES.has(number)) return 'Administrator';
  if (PROJECT_MODULES.has(number)) return 'Project delivery roles';
  return 'Authorized role';
}

function moduleBucket(module) {
  const number = cleanText(module?.moduleNumber).toUpperCase();
  const group = cleanText(module?.group);
  if (number === MODULE_006_NUMBER || /Customer|Sales|Opportunities/i.test(group)) return 'customer';
  if (PROJECT_MODULES.has(number) || /Project/i.test(group)) return 'project';
  if (ADMIN_MODULES.has(number) || /Administration|Security|Integration/i.test(group)) return 'administration';
  return 'core';
}

function isRecentlyChanged(value, days = 30) {
  if (!value) return false;
  const parsed = new Date(value);
  if (Number.isNaN(parsed.getTime())) return false;
  return Date.now() - parsed.getTime() <= days * 24 * 60 * 60 * 1000;
}

function moduleNumberParts(value) {
  const match = cleanText(value).toUpperCase().match(/^(\d+)(.*)$/);
  return match ? [Number(match[1]), match[2]] : [Number.MAX_SAFE_INTEGER, cleanText(value)];
}

function compareModuleNumbers(left, right) {
  const [leftNumber, leftSuffix] = moduleNumberParts(left);
  const [rightNumber, rightSuffix] = moduleNumberParts(right);
  if (leftNumber !== rightNumber) return leftNumber - rightNumber;
  return leftSuffix.localeCompare(rightSuffix);
}

function paginationTokens(page, totalPages) {
  if (totalPages <= 7) return Array.from({ length: totalPages }, (_, index) => index + 1);
  const tokens = [1];
  if (page > 4) tokens.push('start-ellipsis');
  const start = Math.max(2, page - 1);
  const end = Math.min(totalPages - 1, page + 1);
  for (let value = start; value <= end; value += 1) tokens.push(value);
  if (page < totalPages - 3) tokens.push('end-ellipsis');
  tokens.push(totalPages);
  return tokens;
}

function UiIcon({ name }) {
  if (name === 'search') {
    return <svg viewBox="0 0 24 24" aria-hidden="true"><circle cx="11" cy="11" r="6.5" /><path d="m16 16 4 4" /></svg>;
  }
  if (name === 'filter') {
    return <svg viewBox="0 0 24 24" aria-hidden="true"><path d="M4 5h16M7 12h10M10 19h4" /></svg>;
  }
  if (name === 'sort') {
    return <svg viewBox="0 0 24 24" aria-hidden="true"><path d="M8 4v16m0-16L4 8m4-4 4 4M16 20V4m0 16-4-4m4 4 4-4" /></svg>;
  }
  if (name === 'columns') {
    return <svg viewBox="0 0 24 24" aria-hidden="true"><rect x="3" y="4" width="18" height="16" rx="2" /><path d="M9 4v16m6-16v16" /></svg>;
  }
  if (name === 'grid') {
    return <svg viewBox="0 0 24 24" aria-hidden="true"><rect x="3" y="3" width="7" height="7" rx="1.5" /><rect x="14" y="3" width="7" height="7" rx="1.5" /><rect x="3" y="14" width="7" height="7" rx="1.5" /><rect x="14" y="14" width="7" height="7" rx="1.5" /></svg>;
  }
  if (name === 'list') {
    return <svg viewBox="0 0 24 24" aria-hidden="true"><path d="M9 6h11M9 12h11M9 18h11" /><circle cx="4.5" cy="6" r="1" /><circle cx="4.5" cy="12" r="1" /><circle cx="4.5" cy="18" r="1" /></svg>;
  }
  if (name === 'users') {
    return <svg viewBox="0 0 24 24" aria-hidden="true"><circle cx="9" cy="8" r="3" /><path d="M3.5 19c.5-4 2.4-6 5.5-6s5 2 5.5 6M16 5.5a3 3 0 0 1 0 5.5M16 13c2.8.4 4.3 2.4 4.5 6" /></svg>;
  }
  if (name === 'customer') {
    return <svg viewBox="0 0 24 24" aria-hidden="true"><path d="M4 7h6l2 3h8M4 17h6l2-3h8" /><circle cx="4" cy="7" r="2" /><circle cx="4" cy="17" r="2" /><circle cx="20" cy="10" r="2" /><circle cx="20" cy="14" r="2" /></svg>;
  }
  if (name === 'building') {
    return <svg viewBox="0 0 24 24" aria-hidden="true"><path d="M4 21V7l8-4 8 4v14M8 10h2m4 0h2M8 14h2m4 0h2M8 18h2m4 0h2" /></svg>;
  }
  if (name === 'project') {
    return <svg viewBox="0 0 24 24" aria-hidden="true"><rect x="3" y="6" width="18" height="14" rx="2" /><path d="M8 6V4h8v2M3 11h18M10 11v2h4v-2" /></svg>;
  }
  if (name === 'admin') {
    return <svg viewBox="0 0 24 24" aria-hidden="true"><path d="M12 3 4.5 6.5v5.8c0 4.4 2.9 7.4 7.5 8.7 4.6-1.3 7.5-4.3 7.5-8.7V6.5L12 3Z" /><path d="m9 12 2 2 4-4" /></svg>;
  }
  if (name === 'clock') {
    return <svg viewBox="0 0 24 24" aria-hidden="true"><circle cx="12" cy="12" r="9" /><path d="M12 7v5l3 2" /></svg>;
  }
  if (name === 'disabled') {
    return <svg viewBox="0 0 24 24" aria-hidden="true"><circle cx="12" cy="12" r="9" /><path d="m8.5 8.5 7 7m0-7-7 7" /></svg>;
  }
  return <svg viewBox="0 0 24 24" aria-hidden="true"><rect x="3" y="3" width="7" height="7" rx="1.5" /><rect x="14" y="3" width="7" height="7" rx="1.5" /><rect x="3" y="14" width="7" height="7" rx="1.5" /><rect x="14" y="14" width="7" height="7" rx="1.5" /></svg>;
}

function ModuleIcon({ module, large = false }) {
  if (module?.moduleNumber === MODULE_006_NUMBER) {
    return (
      <span className={large ? 'module-enterprise-icon customer large' : 'module-enterprise-icon customer'} aria-hidden="true">
        <img src={module006CustomerBrands} alt="" />
      </span>
    );
  }

  const group = cleanText(module?.group);
  const icon = /Project/i.test(group)
    ? 'project'
    : /Customer|Sales|Opportunities/i.test(group)
      ? 'customer'
      : /Administration|Security/i.test(group)
        ? 'admin'
        : /Time|Resource/i.test(group)
          ? 'clock'
          : /Approval/i.test(group)
            ? 'modules'
            : 'building';
  return (
    <span className={large ? 'module-enterprise-icon large' : 'module-enterprise-icon'} aria-hidden="true">
      <UiIcon name={icon} />
    </span>
  );
}

function FilterChip({ label, onClear }) {
  return (
    <span className="module-management-filter-chip">
      {label}
      <button type="button" aria-label={`Clear ${label}`} onClick={onClear}>×</button>
    </span>
  );
}

export default function ModuleManagementTableView({
  modules,
  directoryResolved,
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
    authoritySource: '',
    error: ''
  });
  const [signedInProfile, setSignedInProfile] = useState({});
  const [selectedModuleNumber, setSelectedModuleNumber] = useState('');
  const [activeTab, setActiveTab] = useState('overview');
  const [busyOwnerModule, setBusyOwnerModule] = useState('');
  const [status, setStatus] = useState('');
  const [query, setQuery] = useState('');
  const [selectedView, setSelectedView] = useState('all');
  const [categoryFilter, setCategoryFilter] = useState('all');
  const [customerFilter, setCustomerFilter] = useState('all');
  const [scopeFilter, setScopeFilter] = useState('all');
  const [availabilityFilter, setAvailabilityFilter] = useState('all');
  const [roleFilter, setRoleFilter] = useState('all');
  const [ownerFilter, setOwnerFilter] = useState('all');
  const [recentFilter, setRecentFilter] = useState('all');
  const [sortBy, setSortBy] = useState('module');
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(DEFAULT_PAGE_SIZE);
  const [displayMode, setDisplayMode] = useState('list');
  const [selectedRows, setSelectedRows] = useState(() => new Set());
  const [columns, setColumns] = useState(DEFAULT_COLUMNS);
  const [railOpen, setRailOpen] = useState(false);

  useEffect(() => {
    if (!tableMode) return undefined;
    document.body?.classList.add('module-management-enterprise-active');
    return () => document.body?.classList.remove('module-management-enterprise-active');
  }, [tableMode]);

  const loadOwnership = useCallback(async ({ preserveStatus = false } = {}) => {
    let lastError = null;
    for (let attempt = 0; attempt < OWNER_LOAD_RETRY_DELAYS_MS.length; attempt += 1) {
      if (OWNER_LOAD_RETRY_DELAYS_MS[attempt] > 0) {
        await wait(OWNER_LOAD_RETRY_DELAYS_MS[attempt]);
      }

      try {
        const response = await fetch('/api/module-catalog/owners', {
          cache: 'no-store',
          credentials: 'include',
          headers: {
            'X-ProjectPulse-Owner-Read-Contract': OWNER_CATALOG_READ_CONTRACT
          }
        });
        const body = await readJson(response);
        if (!response.ok) {
          const error = new Error(body?.message || 'Module ownership could not be loaded.');
          error.status = response.status;
          throw error;
        }
        if (!Array.isArray(body?.owners)) {
          throw new Error('Module ownership returned an invalid read response.');
        }
        setOwnership(normalizedOwnership(body));
        if (!preserveStatus) setStatus('');
        return;
      } catch (error) {
        lastError = error;
        if (!OWNER_LOAD_RETRYABLE_STATUS.has(Number(error?.status || 0))
            || attempt === OWNER_LOAD_RETRY_DELAYS_MS.length - 1) {
          break;
        }
      }
    }

    setOwnership((current) => ({
      ...current,
      loaded: current.loaded,
      error: lastError?.message || 'Module ownership could not be loaded.'
    }));
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
    void loadOwnership();
    const refresh = () => void loadOwnership({ preserveStatus: true });
    window.addEventListener(OWNER_EVENT, refresh);
    window.addEventListener('projectpulse:view-as-changed', refresh);
    window.addEventListener('projectpulse:auth-session-ready', refresh);
    window.addEventListener('projectpulse:permission-navigation-updated', refresh);
    window.addEventListener('pageshow', refresh);
    window.addEventListener('focus', refresh);
    return () => {
      window.removeEventListener(OWNER_EVENT, refresh);
      window.removeEventListener('projectpulse:view-as-changed', refresh);
      window.removeEventListener('projectpulse:auth-session-ready', refresh);
      window.removeEventListener('projectpulse:permission-navigation-updated', refresh);
      window.removeEventListener('pageshow', refresh);
      window.removeEventListener('focus', refresh);
    };
  }, [loadOwnership]);

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

  const ownerOptions = useMemo(() => {
    const options = new Map();
    for (const candidate of candidates) {
      const id = cleanText(candidate?.userId);
      if (id) options.set(id, candidate);
    }
    for (const owner of ownership.owners.values()) {
      const id = cleanText(owner?.ownerUserId);
      if (id && !options.has(id)) options.set(id, owner);
    }
    return [...options.values()].sort((left, right) => (
      cleanText(left?.displayName || left?.email).localeCompare(cleanText(right?.displayName || right?.email))
    ));
  }, [candidates, ownership.owners]);

  const signedInOwnerCandidate = useMemo(() => {
    const profileId = cleanText(signedInProfile?.userId).toLowerCase();
    const profileEmail = normalizeEmail(signedInProfile?.email);
    return candidates.find((candidate) => (
      (profileId && cleanText(candidate?.userId).toLowerCase() === profileId)
      || (profileEmail && normalizeEmail(candidate?.email) === profileEmail)
    )) || null;
  }, [candidates, signedInProfile]);

  const selectedModule = useMemo(
    () => modules.find((module) => module.moduleNumber === selectedModuleNumber) || null,
    [modules, selectedModuleNumber]
  );

  const categories = useMemo(
    () => [...new Set(modules.map((module) => cleanText(module.group)).filter(Boolean))]
      .sort((left, right) => left.localeCompare(right)),
    [modules]
  );

  const scopes = useMemo(
    () => [...new Set(modules.map(accessScopeLabel))].sort((left, right) => left.localeCompare(right)),
    [modules]
  );

  const roles = useMemo(
    () => [...new Set(modules.map(assignedRoleLabel))].sort((left, right) => left.localeCompare(right)),
    [modules]
  );

  const moduleWithState = useCallback((module) => {
    const owner = ownership.owners.get(module.moduleNumber) || {};
    const ownerProfile = ownerAvatarProfile(owner, signedInProfile);
    const updatedAt = owner.updatedAt || module.updatedAt || null;
    return {
      ...module,
      owner,
      ownerProfile,
      ownerLoaded: ownership.loaded,
      updatedAt,
      accessScope: accessScopeLabel(module),
      assignedRole: assignedRoleLabel(module),
      bucket: moduleBucket(module)
    };
  }, [ownership.loaded, ownership.owners, signedInProfile]);

  const allModuleStates = useMemo(
    () => modules.map(moduleWithState),
    [moduleWithState, modules]
  );

  const viewCounts = useMemo(() => {
    const counts = {
      all: allModuleStates.length,
      available: allModuleStates.filter((module) => module.isEnabled).length,
      customer: allModuleStates.filter((module) => module.bucket === 'customer').length,
      core: allModuleStates.filter((module) => module.bucket === 'core').length,
      project: allModuleStates.filter((module) => module.bucket === 'project').length,
      administration: allModuleStates.filter((module) => module.bucket === 'administration').length,
      recent: allModuleStates.filter((module) => isRecentlyChanged(module.updatedAt)).length,
      disabled: allModuleStates.filter((module) => !module.isEnabled).length
    };
    return counts;
  }, [allModuleStates]);

  const filteredModules = useMemo(() => {
    const term = cleanText(query).toLowerCase();
    const recentDays = recentFilter === 'all' ? null : Number(recentFilter);
    return allModuleStates.filter((module) => {
      if (selectedView === 'available' && !module.isEnabled) return false;
      if (selectedView === 'disabled' && module.isEnabled) return false;
      if (['customer', 'core', 'project', 'administration'].includes(selectedView)
          && module.bucket !== selectedView) return false;
      if (selectedView === 'recent' && !isRecentlyChanged(module.updatedAt)) return false;
      if (categoryFilter !== 'all' && module.group !== categoryFilter) return false;
      if (customerFilter !== 'all' && module.moduleNumber !== MODULE_006_NUMBER) return false;
      if (scopeFilter !== 'all' && module.accessScope !== scopeFilter) return false;
      if (availabilityFilter === 'enabled' && !module.isEnabled) return false;
      if (availabilityFilter === 'disabled' && module.isEnabled) return false;
      if (roleFilter !== 'all' && module.assignedRole !== roleFilter) return false;
      if (ownerFilter === 'unassigned' && module.owner?.ownerUserId) return false;
      if (ownerFilter !== 'all' && ownerFilter !== 'unassigned'
          && cleanText(module.owner?.ownerUserId) !== ownerFilter) return false;
      if (recentDays && !isRecentlyChanged(module.updatedAt, recentDays)) return false;
      if (!term) return true;
      const searchable = [
        module.moduleNumber,
        moduleDisplayName(module),
        moduleDescription(module),
        module.route,
        module.group,
        module.accessScope,
        module.assignedRole,
        module.ownerProfile.displayName,
        module.ownerProfile.email,
        module.moduleNumber === MODULE_006_NUMBER ? 'Toyota Hyundai Turion Space customer programs' : ''
      ].join(' ').toLowerCase();
      return searchable.includes(term);
    }).sort((left, right) => {
      if (sortBy === 'name') return moduleDisplayName(left).localeCompare(moduleDisplayName(right));
      if (sortBy === 'category') return cleanText(left.group).localeCompare(cleanText(right.group));
      if (sortBy === 'owner') return left.ownerProfile.displayName.localeCompare(right.ownerProfile.displayName);
      if (sortBy === 'updated') {
        const leftTime = left.updatedAt ? new Date(left.updatedAt).getTime() : 0;
        const rightTime = right.updatedAt ? new Date(right.updatedAt).getTime() : 0;
        return rightTime - leftTime;
      }
      return compareModuleNumbers(left.moduleNumber, right.moduleNumber);
    });
  }, [
    allModuleStates,
    availabilityFilter,
    categoryFilter,
    customerFilter,
    ownerFilter,
    query,
    recentFilter,
    roleFilter,
    scopeFilter,
    selectedView,
    sortBy
  ]);

  const totalPages = Math.max(1, Math.ceil(filteredModules.length / pageSize));
  const pagedModules = useMemo(() => {
    const start = (page - 1) * pageSize;
    return filteredModules.slice(start, start + pageSize);
  }, [filteredModules, page, pageSize]);

  useEffect(() => {
    setPage(1);
  }, [
    availabilityFilter,
    categoryFilter,
    customerFilter,
    ownerFilter,
    query,
    recentFilter,
    roleFilter,
    scopeFilter,
    selectedView,
    sortBy,
    pageSize
  ]);

  useEffect(() => {
    if (page > totalPages) setPage(totalPages);
  }, [page, totalPages]);

  useEffect(() => {
    if (selectedModuleNumber && !selectedModule) setSelectedModuleNumber('');
  }, [selectedModule, selectedModuleNumber]);

  const closeDetailPanel = useCallback(() => {
    const moduleNumber = selectedModuleNumber;
    setSelectedModuleNumber('');
    window.requestAnimationFrame(() => {
      if (!moduleNumber) return;
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

  function selectModule(module, tab = 'overview') {
    setSelectedModuleNumber(module.moduleNumber);
    setActiveTab(tab);
  }

  async function changeOwner(module, ownerUserId) {
    if (!ownership.canManage || ownership.isViewAs || !ownerUserId || busyOwnerModule) return;
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

  function toggleRow(moduleNumber) {
    setSelectedRows((current) => {
      const next = new Set(current);
      if (next.has(moduleNumber)) next.delete(moduleNumber);
      else next.add(moduleNumber);
      return next;
    });
  }

  function togglePageRows() {
    setSelectedRows((current) => {
      const next = new Set(current);
      const pageNumbers = pagedModules.map((module) => module.moduleNumber);
      const allSelected = pageNumbers.length > 0 && pageNumbers.every((number) => next.has(number));
      pageNumbers.forEach((number) => {
        if (allSelected) next.delete(number);
        else next.add(number);
      });
      return next;
    });
  }

  function clearFilters() {
    setQuery('');
    setSelectedView('all');
    setCategoryFilter('all');
    setCustomerFilter('all');
    setScopeFilter('all');
    setAvailabilityFilter('all');
    setRoleFilter('all');
    setOwnerFilter('all');
    setRecentFilter('all');
  }

  if (!tableMode) return null;

  const viewAsReadOnly = ownership.isViewAs || availability?.access?.isViewAs === true;
  const canChangeOwner = ownership.canManage && !viewAsReadOnly;
  const canToggleAvailability = canManage && !viewAsReadOnly;
  const selectedOwner = selectedModule
    ? (ownership.owners.get(selectedModule.moduleNumber) || {})
    : {};
  const selectedOwnerProfile = ownership.loaded
    ? ownerAvatarProfile(selectedOwner, signedInProfile)
    : { displayName: 'Loading owner…', email: '', profilePhotoDataUrl: '' };
  const selectedLastUpdated = selectedOwner.updatedAt || selectedModule?.updatedAt;
  const selectedStart = filteredModules.length ? (page - 1) * pageSize + 1 : 0;
  const selectedEnd = Math.min(page * pageSize, filteredModules.length);
  const activeFilterCount = [
    selectedView !== 'all',
    categoryFilter !== 'all',
    customerFilter !== 'all',
    scopeFilter !== 'all',
    availabilityFilter !== 'all',
    roleFilter !== 'all',
    ownerFilter !== 'all',
    recentFilter !== 'all'
  ].filter(Boolean).length;
  const allPageSelected = pagedModules.length > 0
    && pagedModules.every((module) => selectedRows.has(module.moduleNumber));

  const filterChips = [
    selectedView !== 'all' ? {
      key: 'view',
      label: `View: ${VIEW_DEFINITIONS.find((view) => view.id === selectedView)?.label || selectedView}`,
      clear: () => setSelectedView('all')
    } : null,
    categoryFilter !== 'all' ? { key: 'category', label: `Category: ${categoryFilter}`, clear: () => setCategoryFilter('all') } : null,
    customerFilter !== 'all' ? { key: 'customer', label: `Customer: ${customerFilter}`, clear: () => setCustomerFilter('all') } : null,
    scopeFilter !== 'all' ? { key: 'scope', label: `Access: ${scopeFilter}`, clear: () => setScopeFilter('all') } : null,
    availabilityFilter !== 'all' ? { key: 'availability', label: `Availability: ${availabilityFilter === 'enabled' ? 'Enabled' : 'Disabled'}`, clear: () => setAvailabilityFilter('all') } : null,
    roleFilter !== 'all' ? { key: 'role', label: `Role: ${roleFilter}`, clear: () => setRoleFilter('all') } : null,
    ownerFilter !== 'all' ? {
      key: 'owner',
      label: ownerFilter === 'unassigned'
        ? 'Owner: Unassigned'
        : `Owner: ${cleanText(ownerOptions.find((owner) => cleanText(owner?.userId || owner?.ownerUserId) === ownerFilter)?.displayName) || 'Selected'}`,
      clear: () => setOwnerFilter('all')
    } : null,
    recentFilter !== 'all' ? { key: 'recent', label: `Changed: Last ${recentFilter} days`, clear: () => setRecentFilter('all') } : null
  ].filter(Boolean);

  return (
    <section className="module-management-enterprise" aria-label="Module Management enterprise workspace">
      <header className="module-management-enterprise-header">
        <div className="module-management-enterprise-heading">
          <p>Workspace</p>
          <h1>Module Management</h1>
          <span>Manage availability, access, ownership, and configuration for all modules.</span>
        </div>
        <div className="module-management-enterprise-summary" aria-label="Module availability summary">
          <div><strong>{viewCounts.available}</strong><span>Enabled</span></div>
          <i aria-hidden="true" />
          <div><strong>{viewCounts.disabled}</strong><span>Disabled</span></div>
        </div>
        <div className="module-management-layout-switcher" role="group" aria-label="Module Management layout">
          <span>Layout</span>
          <div>
            <button type="button" className="active" aria-pressed="true" onClick={() => applyLayout(TABLE_EXPERIENCE)}>Enterprise</button>
            <button type="button" aria-pressed="false" onClick={() => applyLayout(CLASSIC_EXPERIENCE)}>Classic</button>
          </div>
        </div>
      </header>

      {ownership.error ? (
        <div className="module-management-table-notice warning" role="alert">
          <span>{ownership.error}</span>
          <button type="button" onClick={() => void loadOwnership()}>Retry ownership</button>
        </div>
      ) : null}
      {status ? <div className="module-management-table-notice success" role="status">{status}</div> : null}
      {viewAsReadOnly ? (
        <div className="module-management-table-notice warning">View-As is read-only. Exit preview to change availability or ownership.</div>
      ) : null}

      <div className={selectedModule ? 'module-management-enterprise-layout has-detail-panel' : 'module-management-enterprise-layout'}>
        <button
          type="button"
          className={railOpen ? 'module-management-rail-backdrop visible' : 'module-management-rail-backdrop'}
          aria-label="Close filters"
          onClick={() => setRailOpen(false)}
        />
        <aside className={railOpen ? 'module-management-rail open' : 'module-management-rail'} aria-label="Module views and filters">
          <section>
            <header><h2>Views</h2><button type="button" aria-label="Create saved view" title="Saved views">+</button></header>
            <nav aria-label="Module views">
              {VIEW_DEFINITIONS.map((view) => (
                <button
                  type="button"
                  key={view.id}
                  className={selectedView === view.id ? 'active' : ''}
                  aria-pressed={selectedView === view.id}
                  onClick={() => { setSelectedView(view.id); setRailOpen(false); }}
                >
                  <span className="module-management-view-icon"><UiIcon name={view.icon} /></span>
                  <span>{view.label}</span>
                  <strong>{viewCounts[view.id] ?? 0}</strong>
                </button>
              ))}
            </nav>
          </section>

          <section className="module-management-filter-section">
            <header><h2>Filters</h2><button type="button" onClick={clearFilters}>Clear all</button></header>
            <label><span>Category</span><select value={categoryFilter} onChange={(event) => setCategoryFilter(event.target.value)}><option value="all">All</option>{categories.map((category) => <option value={category} key={category}>{category}</option>)}</select></label>
            <label><span>Customer</span><select value={customerFilter} onChange={(event) => setCustomerFilter(event.target.value)}><option value="all">All</option><option value="Toyota">Toyota</option><option value="Hyundai">Hyundai</option><option value="Turion Space">Turion Space</option><option value="Other Customers">Other Customers</option></select></label>
            <label><span>Access Scope</span><select value={scopeFilter} onChange={(event) => setScopeFilter(event.target.value)}><option value="all">All</option>{scopes.map((scope) => <option value={scope} key={scope}>{scope}</option>)}</select></label>
            <label><span>Availability</span><select value={availabilityFilter} onChange={(event) => setAvailabilityFilter(event.target.value)}><option value="all">All</option><option value="enabled">Enabled</option><option value="disabled">Disabled</option></select></label>
            <label><span>Assigned Roles</span><select value={roleFilter} onChange={(event) => setRoleFilter(event.target.value)}><option value="all">All</option>{roles.map((role) => <option value={role} key={role}>{role}</option>)}</select></label>
            <label><span>Module Owner</span><select value={ownerFilter} onChange={(event) => setOwnerFilter(event.target.value)}><option value="all">All</option><option value="unassigned">Unassigned</option>{ownerOptions.map((owner) => { const id = cleanText(owner?.userId || owner?.ownerUserId); return id ? <option value={id} key={id}>{owner.displayName || owner.email}</option> : null; })}</select></label>
            <label><span>Recently Changed</span><select value={recentFilter} onChange={(event) => setRecentFilter(event.target.value)}><option value="all">All</option><option value="7">Last 7 days</option><option value="30">Last 30 days</option><option value="90">Last 90 days</option></select></label>
          </section>
        </aside>

        <div className="module-management-workspace">
          <div className="module-management-command-bar">
            <label className="module-management-search">
              <UiIcon name="search" />
              <input
                type="search"
                value={query}
                onChange={(event) => setQuery(event.target.value)}
                placeholder="Search by module number, name, route, or customer…"
              />
            </label>
            <button type="button" className="module-management-command" onClick={() => setRailOpen(true)}><UiIcon name="filter" /><span>Filters</span>{activeFilterCount ? <strong>{activeFilterCount}</strong> : null}</button>
            <label className="module-management-command select"><UiIcon name="sort" /><span className="sr-only">Sort modules</span><select value={sortBy} onChange={(event) => setSortBy(event.target.value)}><option value="module">Sort: Module number</option><option value="name">Sort: Name</option><option value="category">Sort: Category</option><option value="owner">Sort: Owner</option><option value="updated">Sort: Last updated</option></select></label>
            <details className="module-management-columns">
              <summary className="module-management-command"><UiIcon name="columns" /><span>Columns</span></summary>
              <div>
                {Object.keys(DEFAULT_COLUMNS).map((column) => (
                  <label key={column}><input type="checkbox" checked={columns[column]} onChange={(event) => setColumns((current) => ({ ...current, [column]: event.target.checked }))} /><span>{column.charAt(0).toUpperCase() + column.slice(1)}</span></label>
                ))}
              </div>
            </details>
            <div className="module-management-display-mode" role="group" aria-label="Module display">
              <button type="button" className={displayMode === 'list' ? 'active' : ''} aria-pressed={displayMode === 'list'} onClick={() => setDisplayMode('list')}><UiIcon name="list" /><span className="sr-only">List view</span></button>
              <button type="button" className={displayMode === 'grid' ? 'active' : ''} aria-pressed={displayMode === 'grid'} onClick={() => setDisplayMode('grid')}><UiIcon name="grid" /><span className="sr-only">Grid view</span></button>
            </div>
          </div>

          {(filterChips.length || query) ? (
            <div className="module-management-active-filters">
              {query ? <FilterChip label={`Search: ${query}`} onClear={() => setQuery('')} /> : null}
              {filterChips.map((chip) => <FilterChip key={chip.key} label={chip.label} onClear={chip.clear} />)}
              <button type="button" onClick={clearFilters}>Clear all</button>
            </div>
          ) : null}

          {selectedRows.size ? (
            <div className="module-management-selection-bar" role="status">
              <strong>{selectedRows.size} selected</strong>
              <button type="button" onClick={() => setSelectedRows(new Set())}>Clear selection</button>
            </div>
          ) : null}

          {!directoryResolved ? (
            <div className="module-management-loading" role="status">
              <span className="module-management-loading-spinner" aria-hidden="true" />
              <div><strong>Loading authorized modules</strong><p>Your navigation and role-scoped module catalog remain intact while the directory initializes.</p></div>
            </div>
          ) : displayMode === 'grid' ? (
            <div className="module-management-grid" aria-label="Module cards">
              {pagedModules.map((module) => (
                <article key={module.route} data-module-number={module.moduleNumber} className={selectedModuleNumber === module.moduleNumber ? 'selected' : ''}>
                  <button type="button" className="module-management-grid-main" onClick={() => selectModule(module)}>
                    <ModuleIcon module={module} large />
                    <span className="module-management-grid-number">{module.moduleNumber}</span>
                    <h3>{moduleDisplayName(module)}</h3>
                    <p>{moduleDescription(module)}</p>
                    <span className={module.isEnabled ? 'module-management-state enabled' : 'module-management-state disabled'}>{module.isEnabled ? 'Enabled' : 'Disabled'}</span>
                  </button>
                  <footer><span>{module.accessScope}</span><a href={moduleLink(module)}>Open ↗</a></footer>
                </article>
              ))}
            </div>
          ) : (
            <div className="module-management-table-scroll">
              <table className="module-management-table">
                <thead>
                  <tr>
                    <th className="selection" scope="col"><input type="checkbox" checked={allPageSelected} onChange={togglePageRows} aria-label="Select visible modules" /></th>
                    <th scope="col">Module / Name</th>
                    {columns.category ? <th scope="col">Category</th> : null}
                    {columns.scope ? <th scope="col">Access Scope</th> : null}
                    {columns.availability ? <th scope="col">Availability</th> : null}
                    {columns.owner ? <th scope="col">Owner</th> : null}
                    {columns.updated ? <th scope="col">Last Updated</th> : null}
                    {columns.actions ? <th scope="col">Actions</th> : null}
                  </tr>
                </thead>
                <tbody>
                  {pagedModules.map((module) => {
                    const ownerName = module.ownerLoaded ? module.ownerProfile.displayName : 'Loading owner…';
                    const ownerEmail = module.ownerLoaded ? module.ownerProfile.email : '';
                    const isSelected = selectedModuleNumber === module.moduleNumber;
                    const isChecked = selectedRows.has(module.moduleNumber);
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
                        <td className="selection"><input type="checkbox" checked={isChecked} onChange={() => toggleRow(module.moduleNumber)} aria-label={`Select Module ${module.moduleNumber}`} /></td>
                        <td>
                          <div className="module-management-table-identity">
                            <ModuleIcon module={module} />
                            <div>
                              <strong className="module-management-table-number">{module.moduleNumber || '—'}</strong>
                              <button type="button" className="module-management-name-button" onClick={() => selectModule(module)}>{moduleDisplayName(module)}</button>
                              <small>{moduleDescription(module)}</small>
                              {module.moduleNumber === MODULE_006_NUMBER ? <span className="module-management-brand-chips"><i>Toyota</i><i>Hyundai</i><i>Turion Space</i></span> : null}
                            </div>
                          </div>
                        </td>
                        {columns.category ? <td><span className="module-management-table-category">{module.group}</span></td> : null}
                        {columns.scope ? <td><span className="module-management-table-scope">{module.accessScope}</span></td> : null}
                        {columns.availability ? <td><span className={module.isEnabled ? 'module-management-state enabled' : 'module-management-state disabled'}><i aria-hidden="true" />{module.isEnabled ? 'Enabled' : 'Disabled'}</span></td> : null}
                        {columns.owner ? (
                          <td>
                            <span className={!module.ownerLoaded
                              ? 'module-owner-readonly loading'
                              : module.owner?.ownerUserId
                                ? 'module-owner-readonly'
                                : 'module-owner-readonly unassigned'}>
                              <span className="module-owner-avatar-shell"><IdentityAvatar profile={module.ownerLoaded ? module.ownerProfile : { displayName: ownerName }} size="small" showPresence={false} /></span>
                              <span><strong>{ownerName}</strong>{!module.ownerLoaded
                                ? <small>Retrieving saved owner</small>
                                : ownerEmail
                                  ? <small>{ownerEmail}</small>
                                  : canChangeOwner
                                    ? <small>Open Configuration to assign</small>
                                    : null}</span>
                            </span>
                          </td>
                        ) : null}
                        {columns.updated ? <td><time dateTime={module.updatedAt || undefined}>{displayTimestamp(module.updatedAt)}</time></td> : null}
                        {columns.actions ? (
                          <td>
                            <div className="module-management-table-actions">
                              <a href={moduleLink(module)} aria-label={`Open Module ${module.moduleNumber} — ${moduleDisplayName(module)}`}>Open ↗</a>
                              <details>
                                <summary aria-label={`More actions for Module ${module.moduleNumber}`}>⋮</summary>
                                <div>
                                  <button type="button" onClick={() => selectModule(module)}>View details</button>
                                  <button type="button" onClick={() => selectModule(module, 'configuration')}>Configuration</button>
                                  <button type="button" onClick={() => void copyModuleLink(module)}>Copy link</button>
                                  {canChangeOwner && !module.owner?.ownerUserId && signedInOwnerCandidate ? <button type="button" onClick={() => void changeOwner(module, signedInOwnerCandidate.userId)}>Assign to me</button> : null}
                                  {canToggleAvailability ? <button type="button" className={module.isEnabled ? 'danger' : 'enable'} disabled={Boolean(busyModule)} onClick={() => void onToggleModule(module)}>{busyModule === module.moduleNumber ? 'Saving…' : module.isEnabled ? 'Disable module' : 'Enable module'}</button> : null}
                                </div>
                              </details>
                            </div>
                          </td>
                        ) : null}
                      </tr>
                    );
                  })}
                </tbody>
              </table>
              {!pagedModules.length ? <div className="module-management-empty"><strong>No modules match these filters.</strong><button type="button" onClick={clearFilters}>Clear filters</button></div> : null}
            </div>
          )}

          <footer className="module-management-pagination">
            <span>Showing {selectedStart} to {selectedEnd} of {filteredModules.length} modules</span>
            <nav aria-label="Module result pages">
              <button type="button" disabled={page <= 1} onClick={() => setPage((current) => Math.max(1, current - 1))} aria-label="Previous page">‹</button>
              {paginationTokens(page, totalPages).map((token) => typeof token === 'number' ? (
                <button type="button" key={token} className={page === token ? 'active' : ''} aria-current={page === token ? 'page' : undefined} onClick={() => setPage(token)}>{token}</button>
              ) : <span key={token}>…</span>)}
              <button type="button" disabled={page >= totalPages} onClick={() => setPage((current) => Math.min(totalPages, current + 1))} aria-label="Next page">›</button>
            </nav>
            <label><span>Rows per page:</span><select value={pageSize} onChange={(event) => setPageSize(Number(event.target.value))}><option value="10">10</option><option value="25">25</option><option value="50">50</option></select></label>
          </footer>
        </div>

        {selectedModule ? (
          <>
            <button type="button" className="module-management-drawer-backdrop" aria-label="Close module details" onClick={closeDetailPanel} />
            <aside id="module-management-detail-panel" className="module-management-detail-panel" role="dialog" aria-modal="true" aria-labelledby="module-management-detail-title">
              <header className="module-management-detail-header">
                <div className="module-management-detail-toolbar"><button type="button" aria-label="Collapse module details" onClick={closeDetailPanel}>↑</button><button ref={closeButtonRef} type="button" className="module-management-detail-close" aria-label="Close module details" onClick={closeDetailPanel}>×</button></div>
                <div className="module-management-detail-title-row">
                  <ModuleIcon module={selectedModule} />
                  <div><span>Module {selectedModule.moduleNumber}</span><h2 id="module-management-detail-title">{moduleDisplayName(selectedModule)}</h2></div>
                  <span className={selectedModule.isEnabled ? 'module-management-state enabled' : 'module-management-state disabled'}>{selectedModule.isEnabled ? 'Enabled' : 'Disabled'}</span>
                </div>
                {selectedModule.moduleNumber === MODULE_006_NUMBER ? <div className="module-management-customer-brands"><img src={module006CustomerBrands} alt="Toyota, Hyundai, and Turion Space" /></div> : null}
              </header>

              <div className="module-management-detail-tabs" role="tablist" aria-label="Module details">
                {DETAIL_TABS.map((tab) => <button type="button" id={`module-detail-tab-${tab.id}`} key={tab.id} role="tab" aria-selected={activeTab === tab.id} aria-controls={`module-detail-panel-${tab.id}`} tabIndex={activeTab === tab.id ? 0 : -1} onClick={() => setActiveTab(tab.id)}>{tab.label}</button>)}
              </div>

              {activeTab === 'overview' ? (
                <div id="module-detail-panel-overview" role="tabpanel" aria-labelledby="module-detail-tab-overview" className="module-management-detail-content">
                  <section className="module-detail-card"><h3>About</h3><p>{moduleDescription(selectedModule)}</p><dl><div><dt>Category</dt><dd>{selectedModule.group}</dd></div><div><dt>Route</dt><dd>{moduleLink(selectedModule)}</dd></div><div><dt>Module Owner</dt><dd className="module-detail-owner"><IdentityAvatar profile={selectedOwnerProfile} size="small" showPresence={false} /><span>{selectedOwnerProfile.displayName}</span></dd></div><div><dt>Access Scope</dt><dd>{accessScopeLabel(selectedModule)}</dd></div><div><dt>Dependencies</dt><dd>Review in System Architecture</dd></div><div><dt>Last Updated</dt><dd>{displayTimestamp(selectedLastUpdated)}</dd></div></dl></section>
                  <section className="module-detail-card"><h3>Availability</h3><p className={selectedModule.isEnabled ? 'module-detail-availability enabled' : 'module-detail-availability disabled'}><i aria-hidden="true" />{selectedModule.isEnabled ? 'Enabled' : 'Disabled'}</p><p>{selectedModule.isEnabled ? 'This module is available to users authorized by its existing role and scope policy.' : 'This module is disabled. Existing role and scope policy remains unchanged.'}</p>{canToggleAvailability ? <button type="button" className={selectedModule.isEnabled ? 'module-detail-danger' : 'module-detail-primary'} disabled={Boolean(busyModule)} onClick={() => void onToggleModule(selectedModule)}>{busyModule === selectedModule.moduleNumber ? 'Saving…' : selectedModule.isEnabled ? 'Disable Module' : 'Enable Module'}</button> : null}</section>
                  <section className="module-detail-card"><h3>Quick Actions</h3><div className="module-detail-actions"><a href="#role-admin">Configure Access</a><a href="#audit-history">View Change History</a><a href="#system-architecture">Review Dependencies</a><button type="button" onClick={() => void copyModuleLink(selectedModule)}>Copy Module Link</button></div></section>
                </div>
              ) : null}

              {activeTab === 'access' ? (
                <div id="module-detail-panel-access" role="tabpanel" aria-labelledby="module-detail-tab-access" className="module-management-detail-content"><section className="module-detail-card"><h3>Access</h3><dl><div><dt>Effective scope</dt><dd>{accessScopeLabel(selectedModule)}</dd></div><div><dt>Assigned roles</dt><dd>{assignedRoleLabel(selectedModule)}</dd></div><div><dt>Availability</dt><dd>{selectedModule.isEnabled ? 'Enabled' : 'Disabled'}</dd></div><div><dt>View-As</dt><dd>{viewAsReadOnly ? 'Read-only preview active' : 'Inactive'}</dd></div></dl><p className="module-detail-policy-note">Module ownership is accountability metadata only. It never grants module, record, team, department, or organization access.</p><a className="module-detail-primary-link" href="#role-admin">Configure Access</a></section></div>
              ) : null}

              {activeTab === 'configuration' ? (
                <div id="module-detail-panel-configuration" role="tabpanel" aria-labelledby="module-detail-tab-configuration" className="module-management-detail-content"><section className="module-detail-card"><h3>Module Owner</h3><div className="module-detail-owner-summary"><IdentityAvatar profile={selectedOwnerProfile} size="medium" showPresence={false} /><div><strong>{selectedOwnerProfile.displayName}</strong>{selectedOwnerProfile.email ? <span>{selectedOwnerProfile.email}</span> : <span>Accountability owner has not been assigned.</span>}</div></div>{canChangeOwner ? <><label className="module-detail-owner-editor"><span>Change owner</span><select value={selectedOwner.ownerUserId || ''} disabled={busyOwnerModule === selectedModule.moduleNumber || !ownership.loaded} onChange={(event) => void changeOwner(selectedModule, event.target.value)} aria-label={`Owner for Module ${selectedModule.moduleNumber} ${moduleDisplayName(selectedModule)}`}><option value="" disabled>Select owner</option>{candidates.map((candidate) => <option value={candidate.userId} key={candidate.userId}>{candidate.displayName || candidate.email} · {candidate.email}</option>)}</select></label>{!selectedOwner.ownerUserId && signedInOwnerCandidate ? <button type="button" className="module-detail-primary" disabled={Boolean(busyOwnerModule)} onClick={() => void changeOwner(selectedModule, signedInOwnerCandidate.userId)}>Assign to me</button> : null}</> : <p>{viewAsReadOnly ? 'Ownership changes are disabled during View-As preview.' : ownership.loaded ? 'Your actual session is read-only for module ownership.' : 'Ownership authority is still loading. Retry ownership if this message remains.'}</p>}<p className="module-detail-policy-note">Changing an owner records accountability and immutable history. It does not change access.</p></section></div>
              ) : null}

              {activeTab === 'history' ? (
                <div id="module-detail-panel-history" role="tabpanel" aria-labelledby="module-detail-tab-history" className="module-management-detail-content"><section className="module-detail-card"><h3>Recorded Ownership State</h3><dl><div><dt>Owner</dt><dd>{selectedOwnerProfile.displayName}</dd></div><div><dt>Revision</dt><dd>{Number(selectedOwner.revision || 0)}</dd></div><div><dt>Last updated</dt><dd>{displayTimestamp(selectedLastUpdated)}</dd></div><div><dt>Authority source</dt><dd>{ownership.authoritySource || 'Authenticated read-only'}</dd></div></dl><p>Immutable ownership changes remain available in Audit History.</p><a className="module-detail-primary-link" href="#audit-history">Audit History</a></section></div>
              ) : null}
            </aside>
          </>
        ) : null}
      </div>
    </section>
  );
}
