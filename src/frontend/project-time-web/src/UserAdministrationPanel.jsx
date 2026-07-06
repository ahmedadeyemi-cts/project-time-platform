import { useEffect, useMemo, useState } from 'react';

function getProjectPulseAuthHeaders() {
  try {
    const rawSession = window.localStorage.getItem('projectPulseAuthSession');
    if (!rawSession) return {};
    const session = JSON.parse(rawSession);
    return session?.sessionToken ? { 'X-ProjectPulse-Session': session.sessionToken } : {};
  } catch {
    return {};
  }
}

async function readApiErrorMessage(response, path) {
  const raw = await response.text();
  if (!raw) return `${path} returned HTTP ${response.status}`;

  try {
    const parsed = JSON.parse(raw);
    return `${path} returned HTTP ${response.status}: ${parsed.message || parsed.detail || parsed.status || raw}`;
  } catch {
    return `${path} returned HTTP ${response.status}: ${raw}`;
  }
}

async function fetchJson(path) {
  const response = await fetch(path, { headers: getProjectPulseAuthHeaders() });
  if (!response.ok) throw new Error(await readApiErrorMessage(response, path));
  return response.json();
}


/* 041C_USER_EMAIL_EDIT_TEAM_CATALOG_START */
const TEAM_OPTIONS = [
  { teamName: 'Collaboration Engineering', departmentName: 'Collaboration Engineering', managerName: '', managerEmail: '', coordinatorAssigned: false },
  { teamName: 'Systems Engineering', departmentName: 'Systems Engineering', managerName: '', managerEmail: '', coordinatorAssigned: false },
  { teamName: 'Enterprise Network Engineering', departmentName: 'Enterprise Network Engineering', managerName: '', managerEmail: '', coordinatorAssigned: false },
  { teamName: 'Sales', departmentName: 'Sales', managerName: '', managerEmail: '', coordinatorAssigned: false },
  { teamName: 'Project Management', departmentName: 'Project Management Office', managerName: '', managerEmail: '', coordinatorAssigned: false },
  { teamName: 'Account Executive/Sales', departmentName: 'Account Executive/Sales', managerName: '', managerEmail: '', coordinatorAssigned: false },
  { teamName: 'Solution Architecture', departmentName: 'Solution Architecture', managerName: '', managerEmail: '', coordinatorAssigned: false },
  { teamName: 'Accounting', departmentName: 'Accounting', managerName: '', managerEmail: '', coordinatorAssigned: false },
  { teamName: 'Executive', departmentName: 'Executive', managerName: '', managerEmail: '', coordinatorAssigned: false },
  { teamName: 'Resale', departmentName: 'Resale', managerName: '', managerEmail: '', coordinatorAssigned: false },
  { teamName: 'Human Resources', departmentName: 'Human Resources', managerName: '', managerEmail: '', coordinatorAssigned: false }
];
/* 041C_USER_EMAIL_EDIT_TEAM_CATALOG_END */


/* 041D_DEPARTMENT_DROPDOWN_EXEC_MANAGER_START */
/* 041E_EMAIL_ROUTE_MANAGER_MAP_START */
const APPROVED_DEPARTMENT_OPTIONS = [
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

const TEAM_MANAGER_NAME_MAP = {
  'Collaboration Engineering': 'Ahmed Adeyemi',
  'Systems Engineering': 'Ahmed Adeyemi',
  'Enterprise Network Engineering': 'Matthew Lenoble',
  'Project Management': 'Matthew Lenoble'
};

const MANAGER_ROLE_MANAGER_NAME = 'Darren Olson';

function getDepartmentOptions() {
  return APPROVED_DEPARTMENT_OPTIONS;
}

function findUserByDisplayName(users = [], displayName = '') {
  const target = String(displayName ?? '').trim().toLowerCase();
  if (!target) return null;

  return (users ?? []).find((user) => String(user?.displayName ?? '').trim().toLowerCase() === target)
    ?? (users ?? []).find((user) => String(user?.displayName ?? '').toLowerCase().includes(target))
    ?? null;
}

function selectedRoleIsManager(roleCodes = [], roles = []) {
  const normalizedCodes = (roleCodes ?? []).map((code) => String(code ?? '').toLowerCase());
  const selectedRoles = (roles ?? []).filter((role) => normalizedCodes.includes(String(role?.roleCode ?? '').toLowerCase()));
  const roleText = [
    ...normalizedCodes,
    ...selectedRoles.map((role) => String(role?.roleName ?? '').toLowerCase())
  ].join(' ');

  return /\bmanager\b/.test(roleText);
}

function resolveManagerNameForSelection(teamName, roleCodes = [], users = [], roles = []) {
  if (selectedRoleIsManager(roleCodes, roles)) return MANAGER_ROLE_MANAGER_NAME;
  return TEAM_MANAGER_NAME_MAP[teamName] ?? '';
}

function resolveManagerEmailForSelection(teamName, roleCodes = [], users = [], roles = [], currentManagerEmail = '') {
  const managerName = resolveManagerNameForSelection(teamName, roleCodes, users, roles);
  if (!managerName) return currentManagerEmail ?? '';

  const managerUser = findUserByDisplayName(users, managerName);
  return managerUser?.email ?? currentManagerEmail ?? '';
}

function getManagerRuleLabel(teamName, roleCodes = [], users = [], roles = []) {
  const managerName = resolveManagerNameForSelection(teamName, roleCodes, users, roles);

  if (!teamName && !selectedRoleIsManager(roleCodes, roles)) {
    return 'Select a team and PHD role to populate department and manager information.';
  }

  if (!managerName) {
    return 'No automatic manager rule is assigned for this team/role combination. Enter manager email manually if needed.';
  }

  const managerUser = findUserByDisplayName(users, managerName);

  if (managerUser?.email) return `Manager rule: ${managerName} <${managerUser.email}>`;

  return `Manager rule: ${managerName}. Add or update this user email in User Administration to auto-populate the Manager Email field.`;
}

function applyTeamSelectionWithManager(current, teamName, roleCodes = [], users = [], roles = []) {
  const team = TEAM_OPTIONS.find((item) => item.teamName === teamName);

  if (!team) return { ...current, teamName };

  return {
    ...current,
    teamName: team.teamName,
    departmentName: team.departmentName,
    managerEmail: resolveManagerEmailForSelection(team.teamName, roleCodes, users, roles, current.managerEmail)
  };
}

function applyRoleSelectionWithManager(current, roleCodes = [], users = [], roles = []) {
  if (!current) return current;

  return {
    ...current,
    managerEmail: resolveManagerEmailForSelection(current.teamName, roleCodes, users, roles, current.managerEmail)
  };
}
/* 041E_EMAIL_ROUTE_MANAGER_MAP_END */
/* 041D_DEPARTMENT_DROPDOWN_EXEC_MANAGER_END */

function applyTeamSelectionToDraft(current, teamName, roleCodes = [], users = [], roles = []) {
  return applyTeamSelectionWithManager(current, teamName, roleCodes, users, roles);
}

function getTeamManagerLabel(teamName, roleCodes = [], users = [], roles = []) {
  return getManagerRuleLabel(teamName, roleCodes, users, roles);
}


async function postJson(path, payload) {
  const response = await fetch(path, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', ...getProjectPulseAuthHeaders() },
    body: JSON.stringify(payload)
  });

  if (!response.ok) throw new Error(await readApiErrorMessage(response, path));
  return response.json();
}

export default function UserAdministrationPanel() {
  const [data, setData] = useState({ loading: true, users: [], roles: [], departments: [], teams: [], error: null });
  const [selectedUserId, setSelectedUserId] = useState('');
  const [selectedUserIds, setSelectedUserIds] = useState([]);
  const [profileDraft, setProfileDraft] = useState(null);
  const [selectedRoleCodes, setSelectedRoleCodes] = useState([]);
  const [temporaryPassword, setTemporaryPassword] = useState('');
  const [mustChangePassword, setMustChangePassword] = useState(true);
  const [status, setStatus] = useState('Ready');
  const [isBulkUpdateOpen, setIsBulkUpdateOpen] = useState(false);
  const [localUserDraft, setLocalUserDraft] = useState({
    email: '',
    displayName: '',
    temporaryPassword: '',
    mustChangePassword: true,
    jobTitle: '',
    departmentName: '',
    teamName: '',
    officeLocation: '',
    managerEmail: '',
    roleCodes: ['ENGINEERING']
  });

  const [bulkDraft, setBulkDraft] = useState({
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
  });

  async function loadUserAdministration() {
    setData((current) => ({ ...current, loading: true, error: null }));

    try {
      const [usersResult, referenceResult] = await Promise.all([
        fetchJson('/api/admin/user-admin/users'),
        fetchJson('/api/admin/user-admin/reference')
      ]);

      const users = usersResult.users ?? [];
      const selected = users.find((user) => user.userId === selectedUserId) ?? users[0] ?? null;

      setData({
        loading: false,
        users,
        roles: referenceResult.roles ?? [],
        departments: referenceResult.departments ?? [],
        teams: referenceResult.teams ?? [],
        error: null
      });

      setSelectedUserIds((current) => current.filter((userId) => users.some((user) => user.userId === userId)));

      if (selected) {
        setSelectedUserId(selected.userId);
        setProfileDraft({ ...selected });
        setSelectedRoleCodes(selected.roleCodes ?? []);
      }
    } catch (error) {
      setData((current) => ({
        ...current,
        loading: false,
        error: error instanceof Error ? error.message : 'Unable to load User Administration.'
      }));
    }
  }

  useEffect(() => {
    loadUserAdministration();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const selectedUser = useMemo(
    () => data.users.find((user) => user.userId === selectedUserId) ?? null,
    [data.users, selectedUserId]
  );

  const departmentOptions = useMemo(
    () => getDepartmentOptions(),
    []
  );

  const allVisibleSelected = data.users.length > 0 && selectedUserIds.length === data.users.length;

  function selectUser(userId) {
    const user = data.users.find((item) => item.userId === userId);
    setSelectedUserId(userId);
    setProfileDraft(user ? { ...user } : null);
    setSelectedRoleCodes(user?.roleCodes ?? []);
    setTemporaryPassword('');
    setMustChangePassword(true);
    setStatus('Ready');
  }

  function toggleSelectedUser(userId) {
    setSelectedUserIds((current) => (
      current.includes(userId)
        ? current.filter((id) => id !== userId)
        : [...current, userId]
    ));
  }

  function toggleAllVisibleUsers() {
    setSelectedUserIds(allVisibleSelected ? [] : data.users.map((user) => user.userId));
  }

  function toggleRole(roleCode) {
    setSelectedRoleCodes((current) => (
      current.includes(roleCode)
        ? current.filter((code) => code !== roleCode)
        : [...current, roleCode]
    ));
  }

  function getSelectedPrimaryRoleCode() {
    return selectedRoleCodes?.[0] ?? '';
  }

  function updateSelectedPrimaryRole(roleCode) {
    const nextRoleCodes = roleCode ? [roleCode] : [];
    setSelectedRoleCodes(nextRoleCodes);
    setProfileDraft((current) => applyRoleSelectionWithManager(current, nextRoleCodes, data.users, data.roles));
  }

  function toggleBulkRole(roleCode) {
    setBulkDraft((current) => ({
      ...current,
      roleCodes: current.roleCodes.includes(roleCode)
        ? current.roleCodes.filter((code) => code !== roleCode)
        : [...current.roleCodes, roleCode]
    }));
  }

  function toggleLocalCreateRole(roleCode) {
    setLocalUserDraft((current) => ({
      ...current,
      roleCodes: current.roleCodes.includes(roleCode)
        ? current.roleCodes.filter((code) => code !== roleCode)
        : [...current.roleCodes, roleCode]
    }));
  }

  async function createLocalUser() {
    const email = localUserDraft.email.trim().toLowerCase();

    if (!email.endsWith('@ussignal.local')) {
      setStatus('Manual users must use @ussignal.local. Use Entra import for @ussignal.com and @onenecklab.com users.');
      return;
    }

    if (!localUserDraft.displayName.trim()) {
      setStatus('Display name is required before creating a local user.');
      return;
    }

    if (!localUserDraft.temporaryPassword.trim()) {
      setStatus('Enter a temporary password before creating a local user.');
      return;
    }

    setStatus(`Creating local user ${email}...`);

    try {
      const result = await postJson('/api/admin/user-admin/users/local', {
        ...localUserDraft,
        email
      });

      setStatus(result.message ?? 'Local user created.');
      setLocalUserDraft({
        email: '',
        displayName: '',
        temporaryPassword: '',
        mustChangePassword: true,
        jobTitle: '',
        departmentName: '',
        teamName: '',
        officeLocation: '',
        managerEmail: '',
        roleCodes: ['ENGINEERING']
      });

      await loadUserAdministration();
    } catch (error) {
      setStatus(error instanceof Error ? error.message : 'Unable to create local user.');
    }
  }

  async function deactivateSelectedUser() {
    if (!selectedUser) return;

    if (!window.confirm(`Deactivate ${selectedUser.email}? Login will be disabled and active roles will be removed.`)) {
      return;
    }

    setStatus(`Deactivating ${selectedUser.email}...`);

    try {
      const result = await postJson('/api/admin/user-admin/users/deactivate', {
        userId: selectedUser.userId,
        reason: 'Deactivated from User Administration page.'
      });

      setStatus(result.message ?? 'User deactivated.');
      await loadUserAdministration();
    } catch (error) {
      setStatus(error instanceof Error ? error.message : 'Unable to deactivate user.');
    }
  }

  async function deleteSelectedUser() {
    if (!selectedUser) return;

    if (!window.confirm(`Delete ${selectedUser.email}? If this user has history, Project Health Dashboard will safely deactivate the account instead of hard deleting it.`)) {
      return;
    }

    setStatus(`Deleting ${selectedUser.email}...`);

    try {
      const result = await postJson('/api/admin/user-admin/users/delete', {
        userId: selectedUser.userId,
        reason: 'Deleted from User Administration page.'
      });

      setStatus(result.message ?? 'User delete workflow completed.');
      await loadUserAdministration();
    } catch (error) {
      setStatus(error instanceof Error ? error.message : 'Unable to delete user.');
    }
  }

  async function saveProfile() {
    if (!profileDraft) return;

    const cleanEmail = String(profileDraft.email ?? '').trim().toLowerCase();

    if (!/^[^@\s]+@[^@\s]+\.[^@\s]+$/.test(cleanEmail)) {
      setStatus('Enter a valid email address before saving the profile.');
      return;
    }

    setStatus('Saving user email, profile, and role...');

    try {
      /* 041G_PROFILE_ENDPOINT_EMAIL_SAVE_START */
      const profileResult = await postJson('/api/admin/user-admin/users/profile', {
        userId: profileDraft.userId,
        email: cleanEmail,
        displayName: profileDraft.displayName,
        jobTitle: profileDraft.jobTitle ?? '',
        departmentName: profileDraft.departmentName ?? '',
        teamName: profileDraft.teamName ?? '',
        officeLocation: profileDraft.officeLocation ?? '',
        managerEmail: profileDraft.managerEmail ?? '',
        loginEnabled: Boolean(profileDraft.loginEnabled),
        isActive: Boolean(profileDraft.isActive)
      });
      /* 041G_PROFILE_ENDPOINT_EMAIL_SAVE_END */

      const roleResult = await postJson('/api/admin/user-admin/users/roles', {
        userId: profileDraft.userId,
        roleCodes: selectedRoleCodes,
        reason: 'Updated from User Administration profile section.'
      });

      setStatus(profileResult.message ?? roleResult.message ?? 'User email, profile, and role saved.');
      await loadUserAdministration();
    } catch (error) {
      setStatus(error instanceof Error ? error.message : 'Unable to save user email, profile, and role.');
    }
  }

  async function saveRoles() {
    if (!selectedUser) return;
    setStatus('Saving user roles...');

    try {
      const result = await postJson('/api/admin/user-admin/users/roles', {
        userId: selectedUser.userId,
        roleCodes: selectedRoleCodes,
        reason: 'Updated from User Administration page.'
      });

      setStatus(result.message ?? 'User roles updated.');
      await loadUserAdministration();
    } catch (error) {
      setStatus(error instanceof Error ? error.message : 'Unable to save user roles.');
    }
  }

  async function updateLocalPassword() {
    if (!selectedUser) return;

    if (!temporaryPassword.trim()) {
      setStatus('Enter a temporary password before updating the local password.');
      return;
    }

    setStatus('Updating local temporary password...');

    try {
      const result = await postJson('/api/admin/user-admin/local-password', {
        userId: selectedUser.userId,
        temporaryPassword,
        mustChangePassword,
        notes: 'Updated from User Administration page.'
      });

      setTemporaryPassword('');
      setStatus(result.message ?? 'Local temporary password updated.');
      await loadUserAdministration();
    } catch (error) {
      setStatus(error instanceof Error ? error.message : 'Unable to update local password.');
    }
  }

  async function applyBulkUpdate() {
    if (selectedUserIds.length === 0) {
      setStatus('Select at least one user before applying a bulk update.');
      return;
    }

    setStatus(`Applying bulk update to ${selectedUserIds.length} user(s)...`);

    try {
      const result = await postJson('/api/admin/user-admin/users/bulk-update', {
        userIds: selectedUserIds,
        ...bulkDraft,
        reason: 'Bulk update from User Administration page.'
      });

      setStatus(result.message ?? 'Bulk update completed.');
      await loadUserAdministration();
    } catch (error) {
      setStatus(error instanceof Error ? error.message : 'Unable to apply bulk update.');
    }
  }

  return (
    <div className="user-admin-shell">
      <div className="section-heading">
        <div>
          <p className="eyebrow">User Administration</p>
          <h1>Users, roles, teams, and departments</h1>
          <p className="section-copy">
            Manage users individually or select multiple users for bulk department, team, manager, login, and role updates.
          </p>
        </div>
        <button type="button" className="secondary-action" onClick={loadUserAdministration}>
          Refresh
        </button>
      </div>

      {data.error && <div className="error-text">{data.error}</div>}

      <div className="manager-status-row">
        <span>Users: <strong>{data.loading ? 'Loading...' : data.users.length}</strong></span>
        <span>Bulk selected: <strong>{selectedUserIds.length}</strong></span>
        <span>Action: <strong>{status}</strong></span>
      </div>


      <div className="user-admin-create-card">
        <div className="user-admin-create-hero">
          <div>
            <p className="eyebrow">Local user</p>
            <h2>Create local PHD user</h2>
            <p className="section-copy">
              Manual creation is restricted to @ussignal.local accounts. Use Azure Admin import for @ussignal.com and @onenecklab.com users.
            </p>
          </div>
          <button type="button" className="primary-action" onClick={createLocalUser}>
            + Create local user
          </button>
        </div>

        <div className="user-admin-local-layout">
          <div className="user-admin-context-card">
            <div className="context-card-heading">
              <span className="context-card-icon">👤</span>
              <div>
                <p className="eyebrow">1. Identity</p>
                <h3>Account identity</h3>
              </div>
            </div>

            <div className="user-admin-create-grid compact">
              <label>Email</label>
              <input
                value={localUserDraft.email}
                onChange={(event) => setLocalUserDraft((current) => ({ ...current, email: event.target.value }))}
                placeholder="firstname.lastname@ussignal.local"
              />

              <label>Display name</label>
              <input
                value={localUserDraft.displayName}
                onChange={(event) => setLocalUserDraft((current) => ({ ...current, displayName: event.target.value }))}
                placeholder="Full name"
              />

              <label>Temporary password</label>
              <input
                type="password"
                value={localUserDraft.temporaryPassword}
                onChange={(event) => setLocalUserDraft((current) => ({ ...current, temporaryPassword: event.target.value }))}
                placeholder="At least 12 chars with upper, lower, number, special"
              />
            </div>
          </div>

          <div className="user-admin-context-card">
            <div className="context-card-heading">
              <span className="context-card-icon">🏢</span>
              <div>
                <p className="eyebrow">2. Organization</p>
                <h3>Team and reporting</h3>
              </div>
            </div>

            <div className="user-admin-create-grid compact">
              <label>Team</label>
              <select
                value={localUserDraft.teamName}
                onChange={(event) => {
                  setLocalUserDraft((current) => applyTeamSelectionWithManager(current, event.target.value, current.roleCodes, data.users, data.roles));
                }}
              >
                <option value="">Select team</option>
                {TEAM_OPTIONS.map((team) => (
                  <option value={team.teamName} key={team.teamName}>{team.teamName}</option>
                ))}
              </select>

              <label>Department</label>
              <select
                value={localUserDraft.departmentName}
                onChange={(event) => setLocalUserDraft((current) => ({ ...current, departmentName: event.target.value }))}
              >
                <option value="">Select department</option>
                {departmentOptions.map((department) => (
                  <option value={department} key={department}>{department}</option>
                ))}
              </select>

              <label>Job title</label>
              <input
                value={localUserDraft.jobTitle}
                onChange={(event) => setLocalUserDraft((current) => ({ ...current, jobTitle: event.target.value }))}
                placeholder="Engineering, Coordinator, Manager, etc."
              />

              <label>Office location</label>
              <input
                value={localUserDraft.officeLocation}
                onChange={(event) => setLocalUserDraft((current) => ({ ...current, officeLocation: event.target.value }))}
                placeholder="Office / Remote"
              />

              <label>Manager email</label>
              <input
                value={localUserDraft.managerEmail}
                onChange={(event) => setLocalUserDraft((current) => ({ ...current, managerEmail: event.target.value }))}
                placeholder="manager@ussignal.com"
              />
            </div>
          </div>

          <div className="user-admin-context-card compact-card">
            <div className="context-card-heading">
              <span className="context-card-icon">🛡️</span>
              <div>
                <p className="eyebrow">3. Security / Access</p>
                <h3>Password policy</h3>
              </div>
            </div>

            <label className="checkbox-row">
              <input
                type="checkbox"
                checked={localUserDraft.mustChangePassword}
                onChange={(event) => setLocalUserDraft((current) => ({ ...current, mustChangePassword: event.target.checked }))}
              />
              Require password change at next login
            </label>
          </div>

          <div className="user-admin-context-card roles-card-wide">
            <div className="context-card-heading">
              <span className="context-card-icon">👥</span>
              <div>
                <p className="eyebrow">4. Roles</p>
                <h3>Workspace access</h3>
              </div>
            </div>

            <div className="user-admin-role-grid compact-role-grid local-create-roles">
              {data.roles.map((role) => (
                <label key={role.roleCode} className={localUserDraft.roleCodes.includes(role.roleCode) ? 'role-option active' : 'role-option'}>
                  <input
                    type="checkbox"
                    checked={localUserDraft.roleCodes.includes(role.roleCode)}
                    onChange={() => toggleLocalCreateRole(role.roleCode)}
                  />
                  <strong>{role.roleName}</strong>
                  <span>{role.roleCode}</span>
                </label>
              ))}
            </div>
          </div>
        </div>


      </div>

      <div className={isBulkUpdateOpen ? 'user-admin-bulk-card compact-bulk-card open' : 'user-admin-bulk-card compact-bulk-card'}>
        <div className="bulk-collapsible-header">
          <div>
            <p className="eyebrow">Bulk update</p>
            <h2>Update selected users</h2>
            <p className="section-copy">
              Select users from the list below, then expand this panel only when you need to apply the same update to multiple people.
            </p>
          </div>

          <div className="bulk-header-actions">
            <span className={selectedUserIds.length > 0 ? 'bulk-selection-pill active' : 'bulk-selection-pill'}>
              {selectedUserIds.length} selected
            </span>
            <button
              type="button"
              className="secondary-action"
              onClick={() => setIsBulkUpdateOpen((current) => !current)}
            >
              {isBulkUpdateOpen ? 'Collapse' : 'Expand bulk update'}
            </button>
            <button
              type="button"
              className="primary-action"
              onClick={applyBulkUpdate}
              disabled={selectedUserIds.length === 0}
            >
              Apply update
            </button>
          </div>
        </div>

        {isBulkUpdateOpen && (
          <div className="bulk-update-body">
            <div className="bulk-selection-context">
              <strong>{selectedUserIds.length}</strong>
              <span>
                user{selectedUserIds.length === 1 ? '' : 's'} selected from the user list below. Check or uncheck names on the left before applying changes.
              </span>
            </div>

            <div className="user-admin-bulk-grid compact-bulk-grid">
              <label className="checkbox-row">
                <input
                  type="checkbox"
                  checked={bulkDraft.applyDepartmentName}
                  onChange={(event) => setBulkDraft((current) => ({ ...current, applyDepartmentName: event.target.checked }))}
                />
                Apply department
              </label>
              <select
                value={bulkDraft.departmentName}
                onChange={(event) => setBulkDraft((current) => ({ ...current, departmentName: event.target.value }))}
              >
                <option value="">Select department</option>
                {departmentOptions.map((department) => (
                  <option value={department} key={department}>{department}</option>
                ))}
              </select>

              <label className="checkbox-row">
                <input
                  type="checkbox"
                  checked={bulkDraft.applyTeamName}
                  onChange={(event) => setBulkDraft((current) => ({ ...current, applyTeamName: event.target.checked }))}
                />
                Apply team
              </label>
              <select
                value={bulkDraft.teamName}
                onChange={(event) => {
                  const nextTeam = TEAM_OPTIONS.find((team) => team.teamName === event.target.value);
                  setBulkDraft((current) => ({
                    ...applyTeamSelectionWithManager(current, event.target.value, current.roleCodes, data.users, data.roles),
                    applyDepartmentName: Boolean(nextTeam) ? true : current.applyDepartmentName,
                    applyManagerEmail: Boolean(nextTeam) ? true : current.applyManagerEmail
                  }));
                }}
              >
                <option value="">Select team</option>
                {TEAM_OPTIONS.map((team) => (
                  <option value={team.teamName} key={team.teamName}>{team.teamName}</option>
                ))}
              </select>

              <label className="checkbox-row">
                <input
                  type="checkbox"
                  checked={bulkDraft.applyJobTitle}
                  onChange={(event) => setBulkDraft((current) => ({ ...current, applyJobTitle: event.target.checked }))}
                />
                Apply job title
              </label>
              <input
                value={bulkDraft.jobTitle}
                onChange={(event) => setBulkDraft((current) => ({ ...current, jobTitle: event.target.value }))}
                placeholder="Job title"
              />

              <label className="checkbox-row">
                <input
                  type="checkbox"
                  checked={bulkDraft.applyManagerEmail}
                  onChange={(event) => setBulkDraft((current) => ({ ...current, applyManagerEmail: event.target.checked }))}
                />
                Apply manager email
              </label>
              <input
                value={bulkDraft.managerEmail}
                onChange={(event) => setBulkDraft((current) => ({ ...current, managerEmail: event.target.value }))}
                placeholder="executive.manager@example.com"
              />

              <label className="checkbox-row">
                <input
                  type="checkbox"
                  checked={bulkDraft.applyOfficeLocation}
                  onChange={(event) => setBulkDraft((current) => ({ ...current, applyOfficeLocation: event.target.checked }))}
                />
                Apply office location
              </label>
              <input
                value={bulkDraft.officeLocation}
                onChange={(event) => setBulkDraft((current) => ({ ...current, officeLocation: event.target.value }))}
                placeholder="Office / Remote"
              />

              <label className="checkbox-row">
                <input
                  type="checkbox"
                  checked={bulkDraft.applyLoginEnabled}
                  onChange={(event) => setBulkDraft((current) => ({ ...current, applyLoginEnabled: event.target.checked }))}
                />
                Apply login enabled
              </label>
              <select
                value={bulkDraft.loginEnabled ? 'true' : 'false'}
                onChange={(event) => setBulkDraft((current) => ({ ...current, loginEnabled: event.target.value === 'true' }))}
              >
                <option value="true">Enable login</option>
                <option value="false">Disable login</option>
              </select>

              <label className="checkbox-row">
                <input
                  type="checkbox"
                  checked={bulkDraft.applyIsActive}
                  onChange={(event) => setBulkDraft((current) => ({ ...current, applyIsActive: event.target.checked }))}
                />
                Apply active status
              </label>
              <select
                value={bulkDraft.isActive ? 'true' : 'false'}
                onChange={(event) => setBulkDraft((current) => ({ ...current, isActive: event.target.value === 'true' }))}
              >
                <option value="true">Active</option>
                <option value="false">Inactive</option>
              </select>
            </div>

            <div className="user-admin-bulk-roles compact-bulk-roles">
              <div>
                <label>Bulk role action</label>
                <select
                  value={bulkDraft.roleUpdateMode}
                  onChange={(event) => setBulkDraft((current) => ({ ...current, roleUpdateMode: event.target.value }))}
                >
                  <option value="none">Do not change roles</option>
                  <option value="add">Add selected roles</option>
                  <option value="remove">Remove selected roles</option>
                  <option value="replace">Replace all roles with selected roles</option>
                </select>
              </div>

              <div className="user-admin-role-grid compact-role-grid">
                {data.roles.map((role) => (
                  <label key={role.roleCode} className={bulkDraft.roleCodes.includes(role.roleCode) ? 'role-option active' : 'role-option'}>
                    <input
                      type="checkbox"
                      checked={bulkDraft.roleCodes.includes(role.roleCode)}
                      onChange={() => toggleBulkRole(role.roleCode)}
                    />
                    <strong>{role.roleName}</strong>
                    <span>{role.roleCode}</span>
                  </label>
                ))}
              </div>
            </div>


          </div>
        )}
      </div>

      <div className="user-admin-layout">
        <div className="user-admin-list-card">
          <div className="user-admin-list-actions">
            <h2>Users</h2>
            <button type="button" className="secondary-action" onClick={toggleAllVisibleUsers}>
              {allVisibleSelected ? 'Clear all' : 'Select all'}
            </button>
          </div>

          <div className="user-admin-list">
            {data.users.map((user) => (
              <div key={user.userId} className={user.userId === selectedUserId ? 'user-admin-list-row active' : 'user-admin-list-row'}>
                <input
                  type="checkbox"
                  checked={selectedUserIds.includes(user.userId)}
                  onChange={() => toggleSelectedUser(user.userId)}
                  aria-label={`Select ${user.displayName}`}
                />
                <button type="button" onClick={() => selectUser(user.userId)}>
                  <strong>{user.displayName}</strong>
                  <span>{user.email}</span>
                  <small>{user.roleNames?.length ? user.roleNames.join(', ') : 'No active roles'}</small>
                </button>
              </div>
            ))}
          </div>
        </div>

        {profileDraft && selectedUser ? (
          <div className="user-admin-detail-grid">
            <div className="user-admin-card">
              <p className="eyebrow">Profile</p>
              <h2>Identity and organization</h2>

              <label>Display name</label>
              <input value={profileDraft.displayName ?? ''} onChange={(event) => setProfileDraft((current) => ({ ...current, displayName: event.target.value }))} />

              <label>Email</label>
              <input
                value={profileDraft.email ?? ''}
                onChange={(event) => setProfileDraft((current) => ({ ...current, email: event.target.value }))}
                placeholder="name@example.com"
              />
              <div className="user-admin-helper-text">
                Used for notifications, closeout emails, PM assignment alerts, and test/demo recipient validation.
              </div>

              <label>Job title</label>
              <input value={profileDraft.jobTitle ?? ''} onChange={(event) => setProfileDraft((current) => ({ ...current, jobTitle: event.target.value }))} />

              <label>Department</label>
              <select value={profileDraft.departmentName ?? ''} onChange={(event) => setProfileDraft((current) => ({ ...current, departmentName: event.target.value }))}>
                <option value="">Select department</option>
                {departmentOptions.map((department) => (
                  <option value={department} key={department}>{department}</option>
                ))}
              </select>

              <label>Team</label>
              <select
                value={profileDraft.teamName ?? ''}
                onChange={(event) => setProfileDraft((current) => applyTeamSelectionToDraft(current, event.target.value, selectedRoleCodes, data.users, data.roles))}
              >
                <option value="">Select team</option>
                {TEAM_OPTIONS.map((team) => (
                  <option value={team.teamName} key={team.teamName}>{team.teamName}</option>
                ))}
              </select>
              <div className="user-admin-helper-text">
                {getManagerRuleLabel(profileDraft.teamName, selectedRoleCodes, data.users, data.roles)}
              </div>

              <label>Office location</label>
              <input value={profileDraft.officeLocation ?? ''} onChange={(event) => setProfileDraft((current) => ({ ...current, officeLocation: event.target.value }))} />

              <label>Manager email</label>
              <input
                value={profileDraft.managerEmail ?? ''}
                onChange={(event) => setProfileDraft((current) => ({ ...current, managerEmail: event.target.value }))}
                placeholder="executive.manager@example.com"
              />


              <label>PHD role</label>
              <select
                value={getSelectedPrimaryRoleCode()}
                onChange={(event) => updateSelectedPrimaryRole(event.target.value)}
              >
                <option value="">No active role - login blocked</option>
                {data.roles.map((role) => (
                  <option value={role.roleCode} key={role.roleCode}>
                    {role.roleName}
                  </option>
                ))}
              </select>
              <div className="user-admin-helper-text">
                Select the workspace role that controls this user's available views and actions.
              </div>

<label className="checkbox-row">
                <input type="checkbox" checked={Boolean(profileDraft.loginEnabled)} onChange={(event) => setProfileDraft((current) => ({ ...current, loginEnabled: event.target.checked }))} />
                Login enabled
              </label>

              <label className="checkbox-row">
                <input type="checkbox" checked={Boolean(profileDraft.isActive)} onChange={(event) => setProfileDraft((current) => ({ ...current, isActive: event.target.checked }))} />
                User active
              </label>

              <button type="button" className="primary-action" onClick={saveProfile}>
                Save profile
              </button>

              <div className="user-admin-danger-row">
                <button type="button" className="secondary-action" onClick={deactivateSelectedUser}>
                  Deactivate user
                </button>
                <button type="button" className="danger-action" onClick={deleteSelectedUser}>
                  Delete user
                </button>
              </div>
            </div>

            <div className="user-admin-card">
              <p className="eyebrow">Local account</p>
              <h2>Local password management</h2>

              {selectedUser.localUsername ? (
                <>
                  <div className="user-admin-facts">
                    <span>Username: <strong>{selectedUser.localUsername}</strong></span>
                    <span>Password configured: <strong>{selectedUser.hasLocalPassword ? 'Yes' : 'No'}</strong></span>
                    <span>Must change password: <strong>{selectedUser.mustChangePassword ? 'Yes' : 'No'}</strong></span>
                    <span>Failed logins: <strong>{selectedUser.failedLoginCount ?? 0}</strong></span>
                  </div>

                  <label>Temporary password</label>
                  <input
                    type="password"
                    value={temporaryPassword}
                    onChange={(event) => setTemporaryPassword(event.target.value)}
                    placeholder="At least 12 chars with upper, lower, number, special"
                  />

                  <label className="checkbox-row">
                    <input type="checkbox" checked={mustChangePassword} onChange={(event) => setMustChangePassword(event.target.checked)} />
                    Require password change at next login
                  </label>

                  <button type="button" className="primary-action" onClick={updateLocalPassword}>
                    Set local temporary password
                  </button>
                </>
              ) : (
                <div className="manager-empty-state">
                  This user does not have a local account. Azure/Entra users should authenticate through SSO.
                </div>
              )}
            </div>
          </div>
        ) : (
          <div className="manager-empty-state">Select a user to manage.</div>
        )}
      </div>
    </div>
  );
}


/* 030_ROLE_CLEANUP_PHASE2_COMPATIBILITY */
