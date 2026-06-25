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


const TEAM_OPTIONS = [
  {
    teamName: 'Collaboration',
    departmentName: 'Collaboration Engineering',
    managerName: 'Ahmed Adeyemi',
    managerEmail: 'ahmed.adeyemi@ussignal.com',
    coordinatorAssigned: false
  },
  {
    teamName: 'Systems',
    departmentName: 'Systems Engineering',
    managerName: 'Ahmed Adeyemi',
    managerEmail: 'ahmed.adeyemi@ussignal.com',
    coordinatorAssigned: false
  },
  {
    teamName: 'Enterprise Networking',
    departmentName: 'Enterprise Networking Engineering',
    managerName: 'Matthew Lenoble',
    managerEmail: 'matthew.lenoble@ussignal.com',
    coordinatorAssigned: false
  },
  {
    teamName: 'Back Office',
    departmentName: 'Back Office',
    managerName: 'Project and Team Coordinators',
    managerEmail: '',
    coordinatorAssigned: true
  }
];

function applyTeamSelectionToDraft(current, teamName) {
  const team = TEAM_OPTIONS.find((item) => item.teamName === teamName);

  if (!team) {
    return { ...current, teamName };
  }

  return {
    ...current,
    teamName: team.teamName,
    departmentName: team.departmentName,
    managerEmail: team.managerEmail
  };
}

function getTeamManagerLabel(teamName) {
  const team = TEAM_OPTIONS.find((item) => item.teamName === teamName);

  if (!team) return 'Select a team to populate manager information.';

  if (team.coordinatorAssigned) {
    return 'Back Office is assigned to Project and Team Coordinators and does not have a direct manager.';
  }

  return `${team.managerName} is the manager for ${team.departmentName}.`;
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

  function toggleBulkRole(roleCode) {
    setBulkDraft((current) => ({
      ...current,
      roleCodes: current.roleCodes.includes(roleCode)
        ? current.roleCodes.filter((code) => code !== roleCode)
        : [...current.roleCodes, roleCode]
    }));
  }

  async function saveProfile() {
    if (!profileDraft) return;
    setStatus('Saving user profile...');

    try {
      const result = await postJson('/api/admin/user-admin/users/profile', {
        userId: profileDraft.userId,
        displayName: profileDraft.displayName,
        jobTitle: profileDraft.jobTitle ?? '',
        departmentName: profileDraft.departmentName ?? '',
        teamName: profileDraft.teamName ?? '',
        officeLocation: profileDraft.officeLocation ?? '',
        managerEmail: profileDraft.managerEmail ?? '',
        loginEnabled: Boolean(profileDraft.loginEnabled),
        isActive: Boolean(profileDraft.isActive)
      });

      setStatus(result.message ?? 'User profile saved.');
      await loadUserAdministration();
    } catch (error) {
      setStatus(error instanceof Error ? error.message : 'Unable to save user profile.');
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

      <div className="user-admin-bulk-card">
        <div className="section-heading compact-heading">
          <div>
            <p className="eyebrow">Bulk update</p>
            <h2>Update selected users</h2>
            <p className="section-copy">
              Check users on the left, choose the fields to apply, then run one update for everyone selected.
            </p>
          </div>
          <button type="button" className="primary-action" onClick={applyBulkUpdate}>
            Apply bulk update
          </button>
        </div>

        <div className="user-admin-bulk-grid">
          <label className="checkbox-row">
            <input
              type="checkbox"
              checked={bulkDraft.applyDepartmentName}
              onChange={(event) => setBulkDraft((current) => ({ ...current, applyDepartmentName: event.target.checked }))}
            />
            Apply department
          </label>
          <input
            list="bulk-user-admin-departments"
            value={bulkDraft.departmentName}
            onChange={(event) => setBulkDraft((current) => ({ ...current, departmentName: event.target.value }))}
            placeholder="Department"
          />

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
                ...current,
                teamName: event.target.value,
                applyDepartmentName: Boolean(nextTeam) ? true : current.applyDepartmentName,
                departmentName: nextTeam?.departmentName ?? current.departmentName,
                applyManagerEmail: Boolean(nextTeam) ? true : current.applyManagerEmail,
                managerEmail: nextTeam?.managerEmail ?? current.managerEmail
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
            placeholder={bulkDraft.teamName === 'Back Office' ? 'Coordinator assigned' : 'manager@ussignal.com'}
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

        <div className="user-admin-bulk-roles">
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

        <datalist id="bulk-user-admin-departments">
          {data.departments.map((item) => <option value={item} key={item} />)}
        </datalist>

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
              <input value={profileDraft.email ?? ''} disabled />

              <label>Job title</label>
              <input value={profileDraft.jobTitle ?? ''} onChange={(event) => setProfileDraft((current) => ({ ...current, jobTitle: event.target.value }))} />

              <label>Department</label>
              <input list="user-admin-departments" value={profileDraft.departmentName ?? ''} onChange={(event) => setProfileDraft((current) => ({ ...current, departmentName: event.target.value }))} />
              <datalist id="user-admin-departments">
                {data.departments.map((item) => <option value={item} key={item} />)}
              </datalist>

              <label>Team</label>
              <select
                value={profileDraft.teamName ?? ''}
                onChange={(event) => setProfileDraft((current) => applyTeamSelectionToDraft(current, event.target.value))}
              >
                <option value="">Select team</option>
                {TEAM_OPTIONS.map((team) => (
                  <option value={team.teamName} key={team.teamName}>{team.teamName}</option>
                ))}
              </select>
              <div className="user-admin-helper-text">
                {getTeamManagerLabel(profileDraft.teamName)}
              </div>

              <label>Office location</label>
              <input value={profileDraft.officeLocation ?? ''} onChange={(event) => setProfileDraft((current) => ({ ...current, officeLocation: event.target.value }))} />

              <label>Manager email</label>
              <input
                value={profileDraft.managerEmail ?? ''}
                onChange={(event) => setProfileDraft((current) => ({ ...current, managerEmail: event.target.value }))}
                placeholder={profileDraft.teamName === 'Back Office' ? 'Coordinator assigned' : 'manager@ussignal.com'}
              />

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
            </div>

            <div className="user-admin-card">
              <p className="eyebrow">Roles</p>
              <h2>Project Pulse roles</h2>
              <p className="section-copy">Users with no active roles are blocked from login.</p>

              <div className="user-admin-role-grid">
                {data.roles.map((role) => (
                  <label key={role.roleCode} className={selectedRoleCodes.includes(role.roleCode) ? 'role-option active' : 'role-option'}>
                    <input
                      type="checkbox"
                      checked={selectedRoleCodes.includes(role.roleCode)}
                      onChange={() => toggleRole(role.roleCode)}
                    />
                    <strong>{role.roleName}</strong>
                    <span>{role.description}</span>
                  </label>
                ))}
              </div>

              <button type="button" className="primary-action" onClick={saveRoles}>
                Save roles
              </button>
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
