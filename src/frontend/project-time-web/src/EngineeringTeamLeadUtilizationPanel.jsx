import { useEffect, useMemo, useState } from 'react';
import './engineering-team-lead-utilization.css';

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

const ENGINEERING_TEAM_SCOPE_ROLE_CODES = new Set([
  'TEAM_LEAD',
  'ENGINEERING_LEAD',
  'ENGINEERING_TEAM_LEAD',
  'MANAGER',
  'ENGINEERING_MANAGER',
  'DIRECTOR',
  'ENGINEERING_DIRECTOR'
]);

const ENGINEERING_ORGANIZATION_SCOPE_ROLE_CODES = new Set([
  'SUPER_ADMINISTRATOR',
  'SUPERADMINISTRATOR',
  'GLOBAL_ADMINISTRATOR',
  'GLOBALADMINISTRATOR',
  'ADMINISTRATOR',
  'PROJECT_TEAM_COORDINATOR',
  'EXECUTIVE',
  'EXECUTIVE_LEADERSHIP'
]);

function normalizeUtilizationRoleCode(value) {
  return String(value ?? '').trim().toUpperCase().replace(/[\s-]+/g, '_');
}

function canLoadEngineeringTeamSummary(securityContext) {
  const roleCodes = new Set((securityContext?.roles ?? [])
    .map((role) => normalizeUtilizationRoleCode(role?.roleCode ?? role?.roleName ?? role))
    .filter(Boolean));
  const permissions = new Set((securityContext?.permissions ?? [])
    .map((permission) => String(permission ?? '').trim().toUpperCase())
    .filter(Boolean));

  return [...roleCodes].some((roleCode) =>
    ENGINEERING_TEAM_SCOPE_ROLE_CODES.has(roleCode)
    || ENGINEERING_ORGANIZATION_SCOPE_ROLE_CODES.has(roleCode))
    || permissions.has('VIEW_TEAM_UTILIZATION')
    || permissions.has('VIEW_ORGANIZATION_UTILIZATION')
    || permissions.has('VIEW_ALL_UTILIZATION')
    || permissions.has('SYSTEM_ADMINISTRATION')
    || permissions.has('MANAGE_ALL');
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

  if (response.status === 403) {
    return { canViewEngineeringTeamUtilization: false };
  }

  if (!response.ok) throw new Error(await readApiErrorMessage(response, path));
  return response.json();
}

function formatNumber(value) {
  return Number(value ?? 0).toLocaleString(undefined, { maximumFractionDigits: 2 });
}

function formatPercent(value) {
  return `${Number(value ?? 0).toFixed(2)}%`;
}

function getScopeLabel(scope) {
  switch (scope) {
    case 'all_engineers':
      return 'All engineers';
    case 'engineering_team_scope':
      return 'All team members';
    case 'selected_team_engineer_scope':
      return 'Selected team engineer';
    case 'selected_engineer_scope':
      return 'Selected engineer';
    case 'own_engineer_scope':
      return 'My utilization';
    default:
      return String(scope ?? 'Utilization scope').replaceAll('_', ' ');
  }
}

function engineerInitials(name) {
  return String(name ?? '')
    .split(/\s+/)
    .filter(Boolean)
    .slice(0, 2)
    .map((part) => part[0]?.toUpperCase())
    .join('') || 'EN';
}

function quarterFor(member, quarterNumber) {
  return member?.quarters?.find((quarter) => Number(quarter.quarterNumber) === quarterNumber) ?? {
    quarterNumber,
    utilizationPercent: 0,
    billableHours: 0
  };
}

function utilizationState(value, targetPercent) {
  const percent = Number(value ?? 0);
  const target = Number(targetPercent ?? 0);
  if (percent <= 0) return 'no-activity';
  if (target > 0 && percent >= target) return 'on-target';
  return 'below-target';
}

export default function EngineeringTeamLeadUtilizationPanel() {
  const [selectedYear, setSelectedYear] = useState(new Date().getFullYear());
  const [selectedEngineerUserId, setSelectedEngineerUserId] = useState('');
  const [payload, setPayload] = useState({ loading: true, data: null, error: null });

  async function loadUtilization(year = selectedYear, engineerUserId = selectedEngineerUserId) {
    setPayload((current) => ({ ...current, loading: true, error: null }));

    const query = new URLSearchParams();
    query.set('year', String(year));
    if (engineerUserId) query.set('engineerUserId', engineerUserId);

    try {
      const result = await fetchJson(`/api/utilization/engineering-team-summary?${query.toString()}`);
      setPayload({ loading: false, data: result, error: null });
    } catch (error) {
      setPayload({
        loading: false,
        data: null,
        error: error instanceof Error ? error.message : 'Unable to load engineering team utilization.'
      });
    }
  }

  useEffect(() => {
    let cancelled = false;

    async function loadAuthorizedTeamUtilization() {
      try {
        const securityContext = await fetchJson('/api/security/context');
        if (cancelled) return;

        if (!canLoadEngineeringTeamSummary(securityContext)) {
          setPayload({
            loading: false,
            data: { canViewEngineeringTeamUtilization: false },
            error: null
          });
          return;
        }

        await loadUtilization(selectedYear, selectedEngineerUserId);
      } catch (error) {
        if (cancelled) return;
        setPayload({
          loading: false,
          data: null,
          error: error instanceof Error ? error.message : 'Unable to verify engineering utilization access.'
        });
      }
    }

    void loadAuthorizedTeamUtilization();
    return () => {
      cancelled = true;
    };
  }, []);

  const data = payload.data;
  const canView = Boolean(data?.canViewEngineeringTeamUtilization);
  const access = data?.access ?? {};
  const isEngineerOnlyScope = data?.scope === 'own_engineer_scope'
    || (access.canUseOwnScope && !access.canUseTeamScope && !access.canViewAll);
  const selectableEngineers = data?.selectableEngineers ?? [];
  const members = data?.members ?? [];
  const teamSummaries = data?.teamSummaries ?? [];
  const targetPercent = Number(data?.policy?.targetPercent ?? 0);
  const canSelectEngineer = Boolean(access.canSelectEngineer) && selectableEngineers.length > 1;

  const yearOptions = useMemo(() => {
    const currentYear = new Date().getFullYear();
    return [currentYear - 1, currentYear, currentYear + 1];
  }, []);

  if (payload.loading) {
    return null;
  }

  if (!payload.error && (!canView || isEngineerOnlyScope)) {
    return null;
  }

  function handleYearChange(value) {
    const nextYear = Number(value);
    setSelectedYear(nextYear);
    loadUtilization(nextYear, selectedEngineerUserId);
  }

  function handleEngineerChange(value) {
    setSelectedEngineerUserId(value);
    loadUtilization(selectedYear, value);
  }

  return (
    <section className="engineering-team-utilization-panel">
      <div className="section-heading">
        <div>
          <p className="eyebrow">019M-AO</p>
          <h2>Engineering Team Lead Utilization</h2>
          <p className="section-copy">
            Engineering Team Leads and Managers can review utilization for engineers in their team scope. Use the selector to switch between all team members and one engineer.
          </p>
        </div>
        <span className="badge">{getScopeLabel(data?.scope)}</span>
      </div>

      {payload.error ? <div className="error-text">{payload.error}</div> : null}

      <div className="engineering-utilization-toolbar">
        <label>
          Year
          <select value={selectedYear} onChange={(event) => handleYearChange(event.target.value)}>
            {yearOptions.map((year) => <option value={year} key={year}>{year}</option>)}
          </select>
        </label>

        {canSelectEngineer ? (
          <label>
            Engineer scope
            <select value={selectedEngineerUserId} onChange={(event) => handleEngineerChange(event.target.value)}>
              <option value="">{access.canViewAll ? 'All engineers' : 'All team members'}</option>
              {selectableEngineers.map((engineer) => (
                <option value={engineer.userId} key={engineer.userId}>
                  {engineer.displayName} · {engineer.teamName}
                </option>
              ))}
            </select>
          </label>
        ) : null}

        <button type="button" className="secondary-action" onClick={() => loadUtilization(selectedYear, selectedEngineerUserId)}>
          Refresh
        </button>
      </div>

      <div className="engineering-utilization-summary-grid">
        <article>
          <span>Visible engineers</span>
          <strong>{payload.loading ? '...' : data?.collectiveSummary?.memberCount ?? 0}</strong>
          <small>Backend-scoped team members</small>
        </article>
        <article>
          <span>Annual utilization</span>
          <strong>{payload.loading ? '...' : formatPercent(data?.collectiveSummary?.annualUtilizationPercent)}</strong>
          <small>{formatNumber(data?.collectiveSummary?.annualBillableHours)} billable hrs</small>
        </article>
        <article>
          <span>Annual capacity</span>
          <strong>{payload.loading ? '...' : formatNumber(data?.collectiveSummary?.annualCapacityHours)}</strong>
          <small>{targetPercent}% target</small>
        </article>
      </div>

      <div className="engineering-utilization-team-grid">
        {teamSummaries.map((team) => (
          <article className="engineering-utilization-team-card" key={team.teamName}>
            <div>
              <h3>{team.teamName}</h3>
              <p>{team.memberCount} engineer{team.memberCount === 1 ? '' : 's'} · {formatPercent(team.annualUtilizationPercent)} annual utilization</p>
            </div>
            <div className="engineering-quarter-grid">
              {team.quarters.map((quarter) => (
                <span key={`${team.teamName}-${quarter.quarterNumber}`}>
                  Q{quarter.quarterNumber}
                  <strong>{formatPercent(quarter.utilizationPercent)}</strong>
                  <small>{formatNumber(quarter.billableHours)} hrs</small>
                </span>
              ))}
            </div>
          </article>
        ))}
      </div>

      <section className="engineering-utilization-manager-table" aria-labelledby="engineering-utilization-manager-table-title">
        <div className="engineering-utilization-manager-table-heading">
          <div>
            <p className="eyebrow">Manager detail</p>
            <h3 id="engineering-utilization-manager-table-title">Engineer utilization by quarter</h3>
            <p>Each value is aligned beneath its header so managers can compare engineers, teams, annual totals, and quarterly performance without visual ambiguity.</p>
          </div>
          <span>{members.length} engineer{members.length === 1 ? '' : 's'}</span>
        </div>

        <div className="engineering-utilization-table-wrap">
          <table className="engineering-utilization-table">
            <colgroup>
              <col className="engineer-column" />
              <col className="team-column" />
              <col className="annual-utilization-column" />
              <col className="annual-billable-column" />
              <col className="quarter-column" />
              <col className="quarter-column" />
              <col className="quarter-column" />
              <col className="quarter-column" />
            </colgroup>
            <thead>
              <tr>
                <th scope="col">Engineer</th>
                <th scope="col">Team</th>
                <th scope="col">Annual utilization</th>
                <th scope="col">Billable hours</th>
                <th scope="col" className="quarter-heading">Q1</th>
                <th scope="col" className="quarter-heading">Q2</th>
                <th scope="col" className="quarter-heading">Q3</th>
                <th scope="col" className="quarter-heading">Q4</th>
              </tr>
            </thead>
            <tbody>
              {members.map((member) => {
                const annualState = utilizationState(member.annualUtilizationPercent, targetPercent);
                return (
                  <tr key={member.userId}>
                    <th scope="row" className="engineer-cell">
                      <div className="engineer-cell-content">
                        <span className="engineer-avatar" aria-hidden="true">{engineerInitials(member.displayName)}</span>
                        <span className="engineer-identity">
                          <strong>{member.displayName}</strong>
                          <small>{member.email}</small>
                        </span>
                      </div>
                    </th>
                    <td className="team-cell"><span>{member.teamName}</span></td>
                    <td className="annual-utilization-cell">
                      <div className="annual-utilization-content">
                        <div className="annual-utilization-value">
                          <strong>{formatPercent(member.annualUtilizationPercent)}</strong>
                          <span className={`utilization-state ${annualState}`}>
                            {annualState === 'on-target' ? 'On target' : annualState === 'below-target' ? 'Below target' : 'No recorded hours'}
                          </span>
                        </div>
                        <span className="utilization-progress" aria-label={`${formatPercent(member.annualUtilizationPercent)} annual utilization`}>
                          <span style={{ width: `${Math.min(100, Math.max(0, Number(member.annualUtilizationPercent ?? 0)))}%` }} />
                        </span>
                      </div>
                    </td>
                    <td className="numeric-cell">
                      <strong>{formatNumber(member.annualBillableHours)}</strong>
                      <small>hrs</small>
                    </td>
                    {[1, 2, 3, 4].map((quarterNumber) => {
                      const quarter = quarterFor(member, quarterNumber);
                      return (
                        <td className="quarter-cell" key={`${member.userId}-${quarterNumber}`}>
                          <strong>{formatPercent(quarter.utilizationPercent)}</strong>
                          <small>{formatNumber(quarter.billableHours)} hrs</small>
                        </td>
                      );
                    })}
                  </tr>
                );
              })}

              {!payload.loading && members.length === 0 ? (
                <tr>
                  <td className="engineering-utilization-empty" colSpan="8">No engineers are currently visible in this utilization scope.</td>
                </tr>
              ) : null}
            </tbody>
          </table>
        </div>
      </section>

      <p className="section-copy">{data?.calculationNote}</p>
    </section>
  );
}
