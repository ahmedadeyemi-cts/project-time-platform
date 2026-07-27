import { useEffect, useMemo, useState } from 'react';
import './user-administration-panel.css';
import './admin-experience-theme.css';
import './admin-experience-theme.js';

function authHeaders() {
  try {
    const raw = window.localStorage.getItem('projectPulseAuthSession');
    if (!raw) return {};
    const session = JSON.parse(raw);
    return session?.sessionToken
      ? { 'X-ProjectPulse-Session': session.sessionToken }
      : {};
  } catch {
    return {};
  }
}

async function readError(response, path) {
  const raw = await response.text();
  if (!raw) return `${path} returned HTTP ${response.status}`;
  try {
    const parsed = JSON.parse(raw);
    return parsed.message || parsed.detail || parsed.status || raw;
  } catch {
    return raw;
  }
}

async function requestJson(path, options = {}) {
  const response = await fetch(path, {
    ...options,
    headers: {
      ...(options.body ? { 'Content-Type': 'application/json' } : {}),
      ...authHeaders(),
      ...(options.headers || {})
    }
  });
  if (!response.ok) throw new Error(await readError(response, path));
  const text = await response.text();
  return text.trim() ? JSON.parse(text) : {};
}

const fetchJson = (path) => requestJson(path);
const postJson = (path, payload) => requestJson(path, {
  method: 'POST',
  body: JSON.stringify(payload)
});
const putJson = (path, payload) => requestJson(path, {
  method: 'PUT',
  body: JSON.stringify(payload)
});

const FALLBACK_TEAMS = [
  'Collaboration Engineering',
  'Systems Engineering',
  'Enterprise Network Engineering',
  'Sales',
  'Project Management',
  'Account Executive/Sales',
  'Solution Architecture',
  'Accounting',
  'Executive',
  'Resale',
  'Human Resources'
];

const FALLBACK_DEPARTMENTS = [
  'Collaboration Engineering',
  'Systems Engineering',
  'Enterprise Network Engineering',
  'Sales',
  'Project Management Office',
  'Account Executive/Sales',
  'Solution Architecture',
  'Accounting',
  'Executive',
  'Resale',
  'Human Resources'
];

function optionName(value, keys) {
  if (typeof value === 'string') return value.trim();
  for (const key of keys) {
    if (value?.[key] && String(value[key]).trim()) return String(value[key]).trim();
  }
  return '';
}

function roleLabel(role) {
  return role?.roleName || role?.roleCode || 'Role';
}

function userIsLocal(user) {
  return Boolean(user?.localUsername) || String(user?.email || '').toLowerCase().endsWith('.local');
}

function userStatusLabel(user) {
  if (!user?.isActive) return 'Inactive';
  if (!user?.loginEnabled) return 'Login disabled';
  return 'Active';
}

function uniqueSorted(values) {
  return [...new Set(values.filter(Boolean))].sort((a, b) => a.localeCompare(b));
}

function defaultLocalDraft(defaultRoleCode = '') {
  return {
    email: '',
    displayName: '',
    temporaryPassword: '',
    mustChangePassword: true,
    jobTitle: '',
    departmentName: '',
    teamName: '',
    officeLocation: '',
    managerEmail: '',
    roleCodes: defaultRoleCode ? [defaultRoleCode] : []
  };
}

function defaultBulkDraft() {
  return {
    applyJobTitle: false,
    jobTitle: '',
    applyDepartmentName: false,
    departmentName: '',
    applyTeamName: false,
    teamName: '',
    applyOfficeLocation: false,
    officeLocation: '',
    applyManagerEmail: false,
    managerEmail: '',
    applyLoginEnabled: false,
    loginEnabled: true,
    applyIsActive: false,
    isActive: true,
    roleUpdateMode: 'none',
    roleCodes: []
  };
}

export default function UserAdministrationPanel() {
  const [activeTab, setActiveTab] = useState('users');
  const [data, setData] = useState({
    loading: true,
    users: [],
    roles: [],
    departments: [],
    teams: [],
    error: ''
  });
  const [teamScope, setTeamScope] = useState({
    loading: true,
    migrationReady: false,
    managers: [],
    teams: [],
    assignments: [],
    error: ''
  });
  const [status, setStatus] = useState('Ready');
  const [selectedUserId, setSelectedUserId] = useState('');
  const [profileDraft, setProfileDraft] = useState(null);
  const [selectedRoleCodes, setSelectedRoleCodes] = useState([]);
  const [selectedUserIds, setSelectedUserIds] = useState([]);
  const [temporaryPassword, setTemporaryPassword] = useState('');
  const [mustChangePassword, setMustChangePassword] = useState(true);
  const [localUserDraft, setLocalUserDraft] = useState(defaultLocalDraft());
  const [bulkDraft, setBulkDraft] = useState(defaultBulkDraft());
  const [filters, setFilters] = useState({
    search: '',
    role: 'all',
    team: 'all',
    status: 'all',
    account: 'all'
  });
  const [selectedManagerId, setSelectedManagerId] = useState('');
  const [selectedManagerTeams, setSelectedManagerTeams] = useState([]);
  const [managerScopeReason, setManagerScopeReason] = useState('Administrative manager team assignment update.');

  async function loadTeamScope() {
    setTeamScope((current) => ({ ...current, loading: true, error: '' }));
    try {
      const result = await fetchJson('/api/admin/user-admin/manager-team-assignments');
      const next = {
        loading: false,
        migrationReady: Boolean(result.migration?.ready),
        managers: result.managers ?? [],
        teams: result.teams ?? [],
        assignments: result.assignments ?? [],
        error: ''
      };
      setTeamScope(next);
      const managerId = selectedManagerId || next.managers[0]?.userId || '';
      setSelectedManagerId(managerId);
      setSelectedManagerTeams(
        next.assignments
          .filter((assignment) => assignment.isActive && assignment.managerUserId === managerId)
          .map((assignment) => assignment.teamName)
      );
    } catch (error) {
      setTeamScope({
        loading: false,
        migrationReady: false,
        managers: [],
        teams: [],
        assignments: [],
        error: error instanceof Error ? error.message : 'Manager team scope could not be loaded.'
      });
    }
  }

  async function loadUserAdministration() {
    setData((current) => ({ ...current, loading: true, error: '' }));
    try {
      const [usersResult, referenceResult] = await Promise.all([
        fetchJson('/api/admin/user-admin/users'),
        fetchJson('/api/admin/user-admin/reference')
      ]);
      const users = usersResult.users ?? [];
      const roles = referenceResult.roles ?? [];
      const selected = users.find((user) => user.userId === selectedUserId) ?? users[0] ?? null;
      const preferredRole = roles.find((role) => role.roleCode === 'ENGINEERING')?.roleCode
        || roles.find((role) => role.roleCode === 'ENGINEER')?.roleCode
        || roles[0]?.roleCode
        || '';

      setData({
        loading: false,
        users,
        roles,
        departments: referenceResult.departments ?? [],
        teams: referenceResult.teams ?? [],
        error: ''
      });
      setSelectedUserIds((current) => current.filter((id) => users.some((user) => user.userId === id)));
      setLocalUserDraft((current) => current.roleCodes.length ? current : defaultLocalDraft(preferredRole));

      if (selected) {
        setSelectedUserId(selected.userId);
        setProfileDraft({ ...selected });
        setSelectedRoleCodes(selected.roleCodes ?? []);
      }
    } catch (error) {
      setData((current) => ({
        ...current,
        loading: false,
        error: error instanceof Error ? error.message : 'User Administration could not be loaded.'
      }));
    }
  }

  useEffect(() => {
    void loadUserAdministration();
    void loadTeamScope();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const teamOptions = useMemo(() => uniqueSorted([
    ...FALLBACK_TEAMS,
    ...data.teams.map((team) => optionName(team, ['teamName', 'name'])),
    ...data.users.map((user) => user.teamName),
    ...teamScope.teams.map((team) => team.teamName)
  ]), [data.teams, data.users, teamScope.teams]);

  const departmentOptions = useMemo(() => uniqueSorted([
    ...FALLBACK_DEPARTMENTS,
    ...data.departments.map((department) => optionName(department, ['departmentName', 'name'])),
    ...data.users.map((user) => user.departmentName)
  ]), [data.departments, data.users]);

  const selectedUser = useMemo(
    () => data.users.find((user) => user.userId === selectedUserId) ?? null,
    [data.users, selectedUserId]
  );

  const filteredUsers = useMemo(() => {
    const search = filters.search.trim().toLowerCase();
    return data.users.filter((user) => {
      const roles = [...(user.roleCodes ?? []), ...(user.roleNames ?? [])];
      const haystack = [
        user.displayName,
        user.email,
        user.jobTitle,
        user.departmentName,
        user.teamName,
        user.managerEmail,
        ...roles
      ].join(' ').toLowerCase();

      if (search && !haystack.includes(search)) return false;
      if (filters.role !== 'all' && !(user.roleCodes ?? []).includes(filters.role)) return false;
      if (filters.team !== 'all' && user.teamName !== filters.team) return false;
      if (filters.status === 'active' && (!user.isActive || !user.loginEnabled)) return false;
      if (filters.status === 'inactive' && user.isActive) return false;
      if (filters.status === 'login_disabled' && (!user.isActive || user.loginEnabled)) return false;
      if (filters.account === 'local' && !userIsLocal(user)) return false;
      if (filters.account === 'entra' && userIsLocal(user)) return false;
      return true;
    });
  }, [data.users, filters]);

  const activeManagerAssignments = useMemo(
    () => teamScope.assignments.filter((assignment) => assignment.isActive),
    [teamScope.assignments]
  );

  function managerEmailForTeam(teamName) {
    if (!teamName) return '';
    return activeManagerAssignments.find((assignment) => assignment.teamName?.toLowerCase() === teamName.toLowerCase())?.managerEmail || '';
  }

  function selectUser(userId) {
    const user = data.users.find((candidate) => candidate.userId === userId) ?? null;
    setSelectedUserId(userId);
    setProfileDraft(user ? { ...user } : null);
    setSelectedRoleCodes(user?.roleCodes ?? []);
    setTemporaryPassword('');
    setMustChangePassword(true);
    setStatus('Ready');
  }

  function toggleRole(roleCode, setter) {
    setter((current) => current.includes(roleCode)
      ? current.filter((code) => code !== roleCode)
      : [...current, roleCode]);
  }

  function toggleSelectedUser(userId) {
    setSelectedUserIds((current) => current.includes(userId)
      ? current.filter((id) => id !== userId)
      : [...current, userId]);
  }

  function selectAllFiltered() {
    const ids = filteredUsers.map((user) => user.userId);
    const allSelected = ids.length > 0 && ids.every((id) => selectedUserIds.includes(id));
    setSelectedUserIds(allSelected
      ? selectedUserIds.filter((id) => !ids.includes(id))
      : uniqueSorted([...selectedUserIds, ...ids]));
  }

  async function saveProfile() {
    if (!profileDraft) return;
    const email = String(profileDraft.email || '').trim().toLowerCase();
    if (!/^[^@\s]+@[^@\s]+\.[^@\s]+$/.test(email)) {
      setStatus('Enter a valid email address before saving.');
      return;
    }

    const assignedManagerEmail = managerEmailForTeam(profileDraft.teamName);
    setStatus(`Saving ${profileDraft.displayName || email}…`);
    try {
      const profileResult = await postJson('/api/admin/user-admin/users/profile', {
        userId: profileDraft.userId,
        email,
        displayName: profileDraft.displayName,
        jobTitle: profileDraft.jobTitle ?? '',
        departmentName: profileDraft.departmentName ?? '',
        teamName: profileDraft.teamName ?? '',
        officeLocation: profileDraft.officeLocation ?? '',
        managerEmail: assignedManagerEmail || profileDraft.managerEmail || '',
        loginEnabled: Boolean(profileDraft.loginEnabled),
        isActive: Boolean(profileDraft.isActive)
      });
      const roleResult = await postJson('/api/admin/user-admin/users/roles', {
        userId: profileDraft.userId,
        roleCodes: selectedRoleCodes,
        reason: 'Updated from the Module 009 individual user workspace.'
      });
      setStatus(profileResult.message || roleResult.message || 'User saved.');
      await loadUserAdministration();
    } catch (error) {
      setStatus(error instanceof Error ? error.message : 'User could not be saved.');
    }
  }

  async function updateLocalPassword() {
    if (!selectedUser || !temporaryPassword.trim()) {
      setStatus('Enter a temporary password first.');
      return;
    }
    setStatus('Updating the local temporary password…');
    try {
      const result = await postJson('/api/admin/user-admin/local-password', {
        userId: selectedUser.userId,
        temporaryPassword,
        mustChangePassword,
        notes: 'Updated from the Module 009 individual user workspace.'
      });
      setTemporaryPassword('');
      setStatus(result.message || 'Local temporary password updated.');
      await loadUserAdministration();
    } catch (error) {
      setStatus(error instanceof Error ? error.message : 'Local password could not be updated.');
    }
  }

  async function deactivateSelectedUser() {
    if (!selectedUser || !window.confirm(`Deactivate ${selectedUser.email}?`)) return;
    setStatus(`Deactivating ${selectedUser.email}…`);
    try {
      const result = await postJson('/api/admin/user-admin/users/deactivate', {
        userId: selectedUser.userId,
        reason: 'Deactivated from Module 009 User Administration.'
      });
      setStatus(result.message || 'User deactivated.');
      await loadUserAdministration();
    } catch (error) {
      setStatus(error instanceof Error ? error.message : 'User could not be deactivated.');
    }
  }

  async function deleteSelectedUser() {
    if (!selectedUser || !window.confirm(`Delete ${selectedUser.email}? Users with history will be safely deactivated.`)) return;
    setStatus(`Processing ${selectedUser.email}…`);
    try {
      const result = await postJson('/api/admin/user-admin/users/delete', {
        userId: selectedUser.userId,
        reason: 'Delete workflow initiated from Module 009 User Administration.'
      });
      setStatus(result.message || 'User delete workflow completed.');
      await loadUserAdministration();
    } catch (error) {
      setStatus(error instanceof Error ? error.message : 'User delete workflow failed.');
    }
  }

  async function createLocalUser() {
    const email = localUserDraft.email.trim().toLowerCase();
    if (!email.endsWith('@ussignal.local')) {
      setStatus('Local users must use an @ussignal.local address.');
      return;
    }
    if (!localUserDraft.displayName.trim() || !localUserDraft.temporaryPassword.trim()) {
      setStatus('Display name and temporary password are required.');
      return;
    }

    setStatus(`Creating ${email}…`);
    try {
      const result = await postJson('/api/admin/user-admin/users/local', {
        ...localUserDraft,
        email,
        managerEmail: managerEmailForTeam(localUserDraft.teamName) || localUserDraft.managerEmail || ''
      });
      setStatus(result.message || 'Local user created.');
      setLocalUserDraft(defaultLocalDraft(localUserDraft.roleCodes[0] || ''));
      await loadUserAdministration();
    } catch (error) {
      setStatus(error instanceof Error ? error.message : 'Local user could not be created.');
    }
  }

  async function applyBulkUpdate() {
    if (!selectedUserIds.length) {
      setStatus('Select at least one user before applying a bulk update.');
      return;
    }
    const assignedManagerEmail = bulkDraft.applyTeamName
      ? managerEmailForTeam(bulkDraft.teamName)
      : '';
    setStatus(`Updating ${selectedUserIds.length} user(s)…`);
    try {
      const result = await postJson('/api/admin/user-admin/users/bulk-update', {
        userIds: selectedUserIds,
        ...bulkDraft,
        applyManagerEmail: assignedManagerEmail ? true : bulkDraft.applyManagerEmail,
        managerEmail: assignedManagerEmail || bulkDraft.managerEmail,
        reason: 'Bulk update from Module 009 User Administration.'
      });
      setStatus(result.message || 'Bulk update completed.');
      await loadUserAdministration();
    } catch (error) {
      setStatus(error instanceof Error ? error.message : 'Bulk update failed.');
    }
  }

  function selectManager(managerId) {
    setSelectedManagerId(managerId);
    setSelectedManagerTeams(
      activeManagerAssignments
        .filter((assignment) => assignment.managerUserId === managerId)
        .map((assignment) => assignment.teamName)
    );
  }

  function toggleManagerTeam(teamName) {
    setSelectedManagerTeams((current) => current.includes(teamName)
      ? current.filter((team) => team !== teamName)
      : [...current, teamName]);
  }

  async function saveManagerTeamScope() {
    if (!selectedManagerId) {
      setStatus('Select a manager before saving team scope.');
      return;
    }
    setStatus(`Saving ${selectedManagerTeams.length} manager team assignment(s)…`);
    try {
      const result = await putJson(
        `/api/admin/user-admin/manager-team-assignments/${selectedManagerId}`,
        { teamNames: selectedManagerTeams, reason: managerScopeReason }
      );
      setStatus(result.message || 'Manager team scope saved.');
      await Promise.all([loadTeamScope(), loadUserAdministration()]);
    } catch (error) {
      setStatus(error instanceof Error ? error.message : 'Manager team scope could not be saved.');
    }
  }

  const tabs = [
    ['users', 'Manage users'],
    ['bulk', `Bulk updates${selectedUserIds.length ? ` (${selectedUserIds.length})` : ''}`],
    ['create', 'Create local user'],
    ['managers', 'Manager team scope']
  ];

  return (
    <div className="user-admin-v2-shell">
      <header className="user-admin-v2-hero">
        <div>
          <p className="eyebrow">Module 009 · Admin & Identity</p>
          <h1>User Administration</h1>
          <p>
            Search and manage one user at a time, apply controlled bulk changes, create local users,
            and assign managers to one or more teams without navigating a single oversized form.
          </p>
        </div>
        <button type="button" className="secondary-action" onClick={() => Promise.all([loadUserAdministration(), loadTeamScope()])}>
          Refresh users
        </button>
      </header>

      <nav className="user-admin-v2-tabs" aria-label="User Administration sections">
        {tabs.map(([tab, label]) => (
          <button
            type="button"
            key={tab}
            className={activeTab === tab ? 'active' : ''}
            onClick={() => setActiveTab(tab)}
          >
            {label}
          </button>
        ))}
      </nav>

      <div className="user-admin-v2-status" aria-live="polite">
        <span>Users <strong>{data.loading ? '…' : data.users.length}</strong></span>
        <span>Active <strong>{data.users.filter((user) => user.isActive && user.loginEnabled).length}</strong></span>
        <span>Local accounts <strong>{data.users.filter(userIsLocal).length}</strong></span>
        <span>Action <strong>{status}</strong></span>
      </div>

      {data.error ? <div className="user-admin-v2-message error">{data.error}</div> : null}

      {activeTab === 'users' ? (
        <section className="user-admin-v2-workspace">
          <div className="user-admin-v2-toolbar">
            <label className="user-admin-v2-search">
              <span>Search users</span>
              <input
                type="search"
                value={filters.search}
                placeholder="Name, email, role, team, department…"
                onChange={(event) => setFilters((current) => ({ ...current, search: event.target.value }))}
              />
            </label>
            <label>
              <span>Role</span>
              <select value={filters.role} onChange={(event) => setFilters((current) => ({ ...current, role: event.target.value }))}>
                <option value="all">All roles</option>
                {data.roles.map((role) => <option value={role.roleCode} key={role.roleCode}>{roleLabel(role)}</option>)}
              </select>
            </label>
            <label>
              <span>Team</span>
              <select value={filters.team} onChange={(event) => setFilters((current) => ({ ...current, team: event.target.value }))}>
                <option value="all">All teams</option>
                {teamOptions.map((team) => <option value={team} key={team}>{team}</option>)}
              </select>
            </label>
            <label>
              <span>Status</span>
              <select value={filters.status} onChange={(event) => setFilters((current) => ({ ...current, status: event.target.value }))}>
                <option value="all">All statuses</option>
                <option value="active">Active</option>
                <option value="login_disabled">Login disabled</option>
                <option value="inactive">Inactive</option>
              </select>
            </label>
            <label>
              <span>Account</span>
              <select value={filters.account} onChange={(event) => setFilters((current) => ({ ...current, account: event.target.value }))}>
                <option value="all">All accounts</option>
                <option value="local">Local</option>
                <option value="entra">Azure / Entra</option>
              </select>
            </label>
          </div>

          <div className="user-admin-v2-layout">
            <aside className="user-admin-v2-user-list">
              <div className="user-admin-v2-list-heading">
                <div>
                  <p className="eyebrow">Directory</p>
                  <h2>{filteredUsers.length} matching user{filteredUsers.length === 1 ? '' : 's'}</h2>
                </div>
              </div>
              <div className="user-admin-v2-user-scroll">
                {filteredUsers.map((user) => (
                  <button
                    type="button"
                    className={user.userId === selectedUserId ? 'user-admin-v2-user active' : 'user-admin-v2-user'}
                    key={user.userId}
                    onClick={() => selectUser(user.userId)}
                  >
                    <span className="user-admin-v2-avatar">{String(user.displayName || user.email || '?').trim().charAt(0).toUpperCase()}</span>
                    <span>
                      <strong>{user.displayName || user.email}</strong>
                      <small>{user.email}</small>
                      <em>{(user.roleNames ?? []).join(', ') || 'No active role'} · {user.teamName || 'No team'}</em>
                    </span>
                    <span className={`user-admin-v2-state ${user.isActive && user.loginEnabled ? 'active' : 'inactive'}`}>{userStatusLabel(user)}</span>
                  </button>
                ))}
                {!filteredUsers.length ? <div className="user-admin-v2-empty">No users match these filters.</div> : null}
              </div>
            </aside>

            {profileDraft && selectedUser ? (
              <div className="user-admin-v2-profile">
                <div className="user-admin-v2-profile-heading">
                  <div>
                    <p className="eyebrow">Individual user</p>
                    <h2>{profileDraft.displayName || profileDraft.email}</h2>
                    <span>{userIsLocal(selectedUser) ? 'Local ProjectPulse account' : 'Azure / Entra account'}</span>
                  </div>
                  <button type="button" className="primary-action" onClick={saveProfile}>Save user</button>
                </div>

                <div className="user-admin-v2-form-grid">
                  <label>Display name<input value={profileDraft.displayName ?? ''} onChange={(event) => setProfileDraft((current) => ({ ...current, displayName: event.target.value }))} /></label>
                  <label>Email<input value={profileDraft.email ?? ''} onChange={(event) => setProfileDraft((current) => ({ ...current, email: event.target.value }))} /></label>
                  <label>Job title<input value={profileDraft.jobTitle ?? ''} onChange={(event) => setProfileDraft((current) => ({ ...current, jobTitle: event.target.value }))} /></label>
                  <label>Office location<input value={profileDraft.officeLocation ?? ''} onChange={(event) => setProfileDraft((current) => ({ ...current, officeLocation: event.target.value }))} /></label>
                  <label>Department
                    <select value={profileDraft.departmentName ?? ''} onChange={(event) => setProfileDraft((current) => ({ ...current, departmentName: event.target.value }))}>
                      <option value="">Select department</option>
                      {departmentOptions.map((department) => <option value={department} key={department}>{department}</option>)}
                    </select>
                  </label>
                  <label>Team
                    <select value={profileDraft.teamName ?? ''} onChange={(event) => {
                      const teamName = event.target.value;
                      setProfileDraft((current) => ({
                        ...current,
                        teamName,
                        managerEmail: managerEmailForTeam(teamName) || current.managerEmail || ''
                      }));
                    }}>
                      <option value="">Select team</option>
                      {teamOptions.map((team) => <option value={team} key={team}>{team}</option>)}
                    </select>
                  </label>
                  <label className="user-admin-v2-wide">Manager email
                    <input value={profileDraft.managerEmail ?? ''} onChange={(event) => setProfileDraft((current) => ({ ...current, managerEmail: event.target.value }))} placeholder="manager@ussignal.com" />
                    <small>{managerEmailForTeam(profileDraft.teamName) ? 'Automatically controlled by the active manager team assignment.' : 'No active manager is assigned to this team; enter an email manually if required.'}</small>
                  </label>
                </div>

                <div className="user-admin-v2-access-card">
                  <div><p className="eyebrow">Workspace access</p><h3>Roles and account status</h3></div>
                  <div className="user-admin-v2-role-grid">
                    {data.roles.map((role) => (
                      <label className={selectedRoleCodes.includes(role.roleCode) ? 'active' : ''} key={role.roleCode}>
                        <input type="checkbox" checked={selectedRoleCodes.includes(role.roleCode)} onChange={() => toggleRole(role.roleCode, setSelectedRoleCodes)} />
                        <strong>{roleLabel(role)}</strong>
                        <small>{role.roleCode}</small>
                      </label>
                    ))}
                  </div>
                  <div className="user-admin-v2-switches">
                    <label><input type="checkbox" checked={Boolean(profileDraft.loginEnabled)} onChange={(event) => setProfileDraft((current) => ({ ...current, loginEnabled: event.target.checked }))} /> Login enabled</label>
                    <label><input type="checkbox" checked={Boolean(profileDraft.isActive)} onChange={(event) => setProfileDraft((current) => ({ ...current, isActive: event.target.checked }))} /> User active</label>
                  </div>
                </div>

                <div className="user-admin-v2-local-card">
                  <div><p className="eyebrow">Local account</p><h3>Password and account actions</h3></div>
                  {selectedUser.localUsername ? (
                    <>
                      <div className="user-admin-v2-local-facts">
                        <span>Username <strong>{selectedUser.localUsername}</strong></span>
                        <span>Password <strong>{selectedUser.hasLocalPassword ? 'Configured' : 'Not configured'}</strong></span>
                        <span>Failed logins <strong>{selectedUser.failedLoginCount ?? 0}</strong></span>
                      </div>
                      <div className="user-admin-v2-password-row">
                        <label>Temporary password<input type="password" value={temporaryPassword} onChange={(event) => setTemporaryPassword(event.target.value)} /></label>
                        <label className="user-admin-v2-checkbox"><input type="checkbox" checked={mustChangePassword} onChange={(event) => setMustChangePassword(event.target.checked)} /> Require change at next login</label>
                        <button type="button" className="secondary-action" onClick={updateLocalPassword}>Set password</button>
                      </div>
                    </>
                  ) : <p className="user-admin-v2-note">This Azure / Entra user authenticates through SSO and has no local password.</p>}
                  <div className="user-admin-v2-danger-row">
                    <button type="button" className="secondary-action" onClick={deactivateSelectedUser}>Deactivate user</button>
                    <button type="button" className="danger-action" onClick={deleteSelectedUser}>Delete user</button>
                  </div>
                </div>
              </div>
            ) : <div className="user-admin-v2-empty">Select a user to manage.</div>}
          </div>
        </section>
      ) : null}

      {activeTab === 'bulk' ? (
        <section className="user-admin-v2-tab-panel">
          <div className="user-admin-v2-panel-heading">
            <div><p className="eyebrow">Bulk update</p><h2>Apply one controlled change to several users</h2><p>Select users, choose only the fields to change, then apply once.</p></div>
            <button type="button" className="primary-action" onClick={applyBulkUpdate} disabled={!selectedUserIds.length}>Apply to {selectedUserIds.length} user{selectedUserIds.length === 1 ? '' : 's'}</button>
          </div>
          <div className="user-admin-v2-bulk-layout">
            <div className="user-admin-v2-bulk-users">
              <button type="button" className="secondary-action" onClick={selectAllFiltered}>Select / clear filtered users</button>
              {filteredUsers.map((user) => (
                <label key={user.userId}>
                  <input type="checkbox" checked={selectedUserIds.includes(user.userId)} onChange={() => toggleSelectedUser(user.userId)} />
                  <span><strong>{user.displayName}</strong><small>{user.email} · {user.teamName || 'No team'}</small></span>
                </label>
              ))}
            </div>
            <div className="user-admin-v2-bulk-form">
              <BulkField label="Department" enabled={bulkDraft.applyDepartmentName} onEnabled={(checked) => setBulkDraft((current) => ({ ...current, applyDepartmentName: checked }))}>
                <select value={bulkDraft.departmentName} onChange={(event) => setBulkDraft((current) => ({ ...current, departmentName: event.target.value }))}><option value="">Select department</option>{departmentOptions.map((department) => <option value={department} key={department}>{department}</option>)}</select>
              </BulkField>
              <BulkField label="Team" enabled={bulkDraft.applyTeamName} onEnabled={(checked) => setBulkDraft((current) => ({ ...current, applyTeamName: checked }))}>
                <select value={bulkDraft.teamName} onChange={(event) => setBulkDraft((current) => ({ ...current, teamName: event.target.value }))}><option value="">Select team</option>{teamOptions.map((team) => <option value={team} key={team}>{team}</option>)}</select>
              </BulkField>
              <BulkField label="Job title" enabled={bulkDraft.applyJobTitle} onEnabled={(checked) => setBulkDraft((current) => ({ ...current, applyJobTitle: checked }))}><input value={bulkDraft.jobTitle} onChange={(event) => setBulkDraft((current) => ({ ...current, jobTitle: event.target.value }))} /></BulkField>
              <BulkField label="Office location" enabled={bulkDraft.applyOfficeLocation} onEnabled={(checked) => setBulkDraft((current) => ({ ...current, applyOfficeLocation: checked }))}><input value={bulkDraft.officeLocation} onChange={(event) => setBulkDraft((current) => ({ ...current, officeLocation: event.target.value }))} /></BulkField>
              <BulkField label="Manager email" enabled={bulkDraft.applyManagerEmail} onEnabled={(checked) => setBulkDraft((current) => ({ ...current, applyManagerEmail: checked }))}><input value={bulkDraft.managerEmail} onChange={(event) => setBulkDraft((current) => ({ ...current, managerEmail: event.target.value }))} /></BulkField>
              <BulkField label="Login" enabled={bulkDraft.applyLoginEnabled} onEnabled={(checked) => setBulkDraft((current) => ({ ...current, applyLoginEnabled: checked }))}><select value={String(bulkDraft.loginEnabled)} onChange={(event) => setBulkDraft((current) => ({ ...current, loginEnabled: event.target.value === 'true' }))}><option value="true">Enable login</option><option value="false">Disable login</option></select></BulkField>
              <BulkField label="Active status" enabled={bulkDraft.applyIsActive} onEnabled={(checked) => setBulkDraft((current) => ({ ...current, applyIsActive: checked }))}><select value={String(bulkDraft.isActive)} onChange={(event) => setBulkDraft((current) => ({ ...current, isActive: event.target.value === 'true' }))}><option value="true">Active</option><option value="false">Inactive</option></select></BulkField>
              <label>Role action<select value={bulkDraft.roleUpdateMode} onChange={(event) => setBulkDraft((current) => ({ ...current, roleUpdateMode: event.target.value }))}><option value="none">Do not change roles</option><option value="add">Add selected roles</option><option value="remove">Remove selected roles</option><option value="replace">Replace all roles</option></select></label>
              <div className="user-admin-v2-role-grid compact">
                {data.roles.map((role) => <label className={bulkDraft.roleCodes.includes(role.roleCode) ? 'active' : ''} key={role.roleCode}><input type="checkbox" checked={bulkDraft.roleCodes.includes(role.roleCode)} onChange={() => setBulkDraft((current) => ({ ...current, roleCodes: current.roleCodes.includes(role.roleCode) ? current.roleCodes.filter((code) => code !== role.roleCode) : [...current.roleCodes, role.roleCode] }))} /><strong>{roleLabel(role)}</strong></label>)}
              </div>
            </div>
          </div>
        </section>
      ) : null}

      {activeTab === 'create' ? (
        <section className="user-admin-v2-tab-panel">
          <div className="user-admin-v2-panel-heading">
            <div><p className="eyebrow">Local account</p><h2>Create one local ProjectPulse user</h2><p>Local creation is restricted to @ussignal.local. Azure / Entra users remain owned by Module 010 import.</p></div>
            <button type="button" className="primary-action" onClick={createLocalUser}>Create local user</button>
          </div>
          <div className="user-admin-v2-create-grid">
            <label>Email<input value={localUserDraft.email} onChange={(event) => setLocalUserDraft((current) => ({ ...current, email: event.target.value }))} placeholder="firstname.lastname@ussignal.local" /></label>
            <label>Display name<input value={localUserDraft.displayName} onChange={(event) => setLocalUserDraft((current) => ({ ...current, displayName: event.target.value }))} /></label>
            <label>Temporary password<input type="password" value={localUserDraft.temporaryPassword} onChange={(event) => setLocalUserDraft((current) => ({ ...current, temporaryPassword: event.target.value }))} /></label>
            <label>Job title<input value={localUserDraft.jobTitle} onChange={(event) => setLocalUserDraft((current) => ({ ...current, jobTitle: event.target.value }))} /></label>
            <label>Department<select value={localUserDraft.departmentName} onChange={(event) => setLocalUserDraft((current) => ({ ...current, departmentName: event.target.value }))}><option value="">Select department</option>{departmentOptions.map((department) => <option value={department} key={department}>{department}</option>)}</select></label>
            <label>Team<select value={localUserDraft.teamName} onChange={(event) => { const teamName = event.target.value; setLocalUserDraft((current) => ({ ...current, teamName, managerEmail: managerEmailForTeam(teamName) || current.managerEmail || '' })); }}><option value="">Select team</option>{teamOptions.map((team) => <option value={team} key={team}>{team}</option>)}</select></label>
            <label>Office location<input value={localUserDraft.officeLocation} onChange={(event) => setLocalUserDraft((current) => ({ ...current, officeLocation: event.target.value }))} /></label>
            <label>Manager email<input value={localUserDraft.managerEmail} onChange={(event) => setLocalUserDraft((current) => ({ ...current, managerEmail: event.target.value }))} /></label>
            <label className="user-admin-v2-checkbox"><input type="checkbox" checked={localUserDraft.mustChangePassword} onChange={(event) => setLocalUserDraft((current) => ({ ...current, mustChangePassword: event.target.checked }))} /> Require password change at next login</label>
          </div>
          <div className="user-admin-v2-role-grid">
            {data.roles.map((role) => <label className={localUserDraft.roleCodes.includes(role.roleCode) ? 'active' : ''} key={role.roleCode}><input type="checkbox" checked={localUserDraft.roleCodes.includes(role.roleCode)} onChange={() => setLocalUserDraft((current) => ({ ...current, roleCodes: current.roleCodes.includes(role.roleCode) ? current.roleCodes.filter((code) => code !== role.roleCode) : [...current.roleCodes, role.roleCode] }))} /><strong>{roleLabel(role)}</strong><small>{role.roleCode}</small></label>)}
          </div>
        </section>
      ) : null}

      {activeTab === 'managers' ? (
        <section className="user-admin-v2-tab-panel">
          <div className="user-admin-v2-panel-heading">
            <div><p className="eyebrow">Manager scope</p><h2>Assign one manager to multiple teams</h2><p>Each team has one active manager. Saving automatically populates that manager’s email on every active team member so manager visibility remains team-scoped.</p></div>
            <button type="button" className="primary-action" onClick={saveManagerTeamScope} disabled={!teamScope.migrationReady || !selectedManagerId}>Save team scope</button>
          </div>
          {teamScope.error ? <div className="user-admin-v2-message error">{teamScope.error}</div> : null}
          {!teamScope.migrationReady ? <div className="user-admin-v2-message">Migration 048 must be applied before manager multi-team assignments can be saved.</div> : null}
          <div className="user-admin-v2-manager-controls">
            <label>Manager<select value={selectedManagerId} onChange={(event) => selectManager(event.target.value)}><option value="">Select manager</option>{teamScope.managers.map((manager) => <option value={manager.userId} key={manager.userId}>{manager.displayName} — {manager.email}</option>)}</select></label>
            <label>Audit reason<input value={managerScopeReason} onChange={(event) => setManagerScopeReason(event.target.value)} /></label>
          </div>
          <div className="user-admin-v2-team-grid">
            {teamScope.teams.map((team) => {
              const currentManager = team.activeManager;
              const selected = selectedManagerTeams.includes(team.teamName);
              return (
                <label className={selected ? 'active' : ''} key={team.teamName}>
                  <input type="checkbox" checked={selected} onChange={() => toggleManagerTeam(team.teamName)} />
                  <span><strong>{team.teamName}</strong><small>{team.activeMemberCount} active / {team.memberCount} total members</small><em>{currentManager ? `Current manager: ${currentManager.managerDisplayName}` : 'No active manager'}</em></span>
                </label>
              );
            })}
          </div>
        </section>
      ) : null}
    </div>
  );
}

function BulkField({ label, enabled, onEnabled, children }) {
  return (
    <div className="user-admin-v2-bulk-field">
      <label className="user-admin-v2-checkbox">
        <input type="checkbox" checked={enabled} onChange={(event) => onEnabled(event.target.checked)} />
        Change {label.toLowerCase()}
      </label>
      <div className={enabled ? '' : 'disabled'}>{children}</div>
    </div>
  );
}

/* 030_ROLE_CLEANUP_PHASE2_COMPATIBILITY */
