import { useEffect, useMemo, useState } from 'react';
import { createPortal } from 'react-dom';
import './ptc-runtime-task-catalog.css';

function ensureHost(portal) {
  if (!portal) return null;
  let host = portal.querySelector(':scope > #ptc-runtime-task-catalog-host');
  if (!host) {
    host = document.createElement('div');
    host.id = 'ptc-runtime-task-catalog-host';
    host.className = 'ptc-runtime-task-catalog-host';
    const entrySection = portal.querySelector('.ptc-entry-section');
    if (entrySection) portal.insertBefore(host, entrySection);
    else portal.appendChild(host);
  }
  return host;
}

function roleLabel(user) {
  const names = Array.isArray(user?.roleNames) ? user.roleNames : [];
  return names.length ? names.join(' / ') : 'Eligible delivery role';
}

function syncNativeSelectors(usersPayload, workspacePayload) {
  const portal = document.querySelector('.ptc-time-steward-portal');
  if (!portal) return;

  const users = Array.isArray(usersPayload?.users) ? usersPayload.users : [];
  const userById = new Map(users.map((user) => [String(user.userId), user]));
  const userSelect = portal.querySelector('.ptc-toolbar label:last-child select');
  userSelect?.querySelectorAll('option').forEach((option) => {
    const user = userById.get(option.value);
    if (!user) return;
    option.textContent = `${user.displayName} · ${roleLabel(user)} · ${user.email}`;
  });

  const assignments = Array.isArray(workspacePayload?.assignments) ? workspacePayload.assignments : [];
  const assignmentById = new Map(assignments.map((assignment) => [String(assignment.assignmentId), assignment]));
  portal.querySelectorAll('.ptc-entry-table select option').forEach((option) => {
    if (!option.value) return;
    const assignment = assignmentById.get(option.value);
    if (!assignment) return;
    option.textContent = `[${assignment.groupLabel}] ${assignment.selectionLabel || `${assignment.projectCode} · ${assignment.taskCode} · ${assignment.taskName}`}`;
  });
}

function TargetCard({ target }) {
  return <article className="ptc-runtime-target-card">
    <strong>{target.selectionLabel || target.categoryName || target.taskName}</strong>
    {target.serviceRequestNumber ? <span>Request: {target.serviceRequestNumber}</span> : null}
    {target.projectName ? <span>{target.projectCode} · {target.projectName}</span> : null}
    {target.taskCode ? <small>{target.taskCode} · {target.taskName}</small> : null}
    {target.categoryCode ? <small>{target.categoryCode}</small> : null}
  </article>;
}

export default function PtcRuntimeTaskCatalog() {
  const [host, setHost] = useState(null);
  const [usersPayload, setUsersPayload] = useState(() => window.__projectPulsePtcRuntimeUsers || null);
  const [workspacePayload, setWorkspacePayload] = useState(() => window.__projectPulsePtcRuntimeWorkspace || null);

  useEffect(() => {
    const sync = () => setHost(ensureHost(document.querySelector('.ptc-time-steward-portal')));
    sync();
    const observer = new MutationObserver(sync);
    observer.observe(document.body, { childList: true, subtree: true });
    window.addEventListener('hashchange', sync);
    return () => {
      observer.disconnect();
      window.removeEventListener('hashchange', sync);
    };
  }, []);

  useEffect(() => {
    const usersListener = (event) => setUsersPayload(event.detail || null);
    const workspaceListener = (event) => setWorkspacePayload(event.detail || null);
    window.addEventListener('projectpulse:ptc-runtime-users', usersListener);
    window.addEventListener('projectpulse:ptc-runtime-workspace', workspaceListener);
    return () => {
      window.removeEventListener('projectpulse:ptc-runtime-users', usersListener);
      window.removeEventListener('projectpulse:ptc-runtime-workspace', workspaceListener);
    };
  }, []);

  useEffect(() => {
    syncNativeSelectors(usersPayload, workspacePayload);
    const timer = window.setTimeout(() => syncNativeSelectors(usersPayload, workspacePayload), 100);
    return () => window.clearTimeout(timer);
  }, [usersPayload, workspacePayload]);

  const groups = useMemo(() => {
    const assignments = Array.isArray(workspacePayload?.assignments) ? workspacePayload.assignments : [];
    const categories = Array.isArray(workspacePayload?.nonProjectCategories) ? workspacePayload.nonProjectCategories : [];
    return [
      {
        code: 'requests',
        label: 'Requests / Service Requests',
        items: assignments.filter((item) => item.groupLabel === 'Requests / Service Requests')
      },
      {
        code: 'projects',
        label: 'Project Tasks',
        items: assignments.filter((item) => item.groupLabel !== 'Requests / Service Requests')
      },
      {
        code: 'non-project',
        label: 'Non-Project Time',
        items: categories
      }
    ];
  }, [workspacePayload]);

  if (!host) return null;
  const selectedUser = workspacePayload?.user || null;

  return createPortal(<section className="ptc-runtime-task-catalog" data-projectpulse-runtime-task-catalog="true">
    <header>
      <div>
        <p className="eyebrow">Available work for selected user</p>
        <h3>{selectedUser?.displayName || 'Select an eligible user'}</h3>
        <p>{selectedUser ? `${roleLabel(selectedUser)} · ${selectedUser.email}` : 'Choose an Engineer, Engineering Lead, Project Management, or Project Management Lead user.'}</p>
      </div>
      <div className="ptc-runtime-role-boundary">
        <strong>Eligible roles</strong>
        <span>Engineer · Engineering Lead · Project Management · Project Management Lead</span>
      </div>
    </header>

    <div className="ptc-runtime-groups">
      {groups.map((group) => <section key={group.code} className="ptc-runtime-group">
        <header><h4>{group.label}</h4><span>{group.items.length}</span></header>
        <div>
          {group.items.map((item) => <TargetCard key={item.assignmentId || item.nonProjectTimeCategoryId || item.categoryCode} target={item} />)}
          {group.items.length === 0 ? <p className="ptc-runtime-empty">No {group.label.toLowerCase()} are available for this user and week.</p> : null}
        </div>
      </section>)}
    </div>
  </section>, host);
}
