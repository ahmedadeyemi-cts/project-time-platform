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
    loadUtilization(selectedYear, selectedEngineerUserId);
  }, []);

  const data = payload.data;
  const canView = Boolean(data?.canViewEngineeringTeamUtilization);
  const access = data?.access ?? {};
  const selectableEngineers = data?.selectableEngineers ?? [];
  const members = data?.members ?? [];
  const teamSummaries = data?.teamSummaries ?? [];
  const canSelectEngineer = Boolean(access.canSelectEngineer) && selectableEngineers.length > 1;

  const yearOptions = useMemo(() => {
    const currentYear = new Date().getFullYear();
    return [currentYear - 1, currentYear, currentYear + 1];
  }, []);

  if (!payload.loading && !payload.error && !canView) {
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
            Engineering Team Leads can review utilization for engineers on their team only. Use the selector to switch between all team members and one engineer.
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
          <small>{data?.policy?.targetPercent ?? 0}% target</small>
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

      <div className="engineering-utilization-table-wrap">
        <table className="team-member-table">
          <thead>
            <tr>
              <th>Engineer</th>
              <th>Team</th>
              <th>Annual utilization</th>
              <th>Annual billable</th>
              <th>Quarter detail</th>
            </tr>
          </thead>
          <tbody>
            {members.map((member) => (
              <tr key={member.userId}>
                <td><strong>{member.displayName}</strong><span>{member.email}</span></td>
                <td>{member.teamName}</td>
                <td>{formatPercent(member.annualUtilizationPercent)}</td>
                <td>{formatNumber(member.annualBillableHours)} hrs</td>
                <td>
                  <div className="member-quarter-list">
                    {member.quarters.map((quarter) => (
                      <span key={`${member.userId}-${quarter.quarterNumber}`}>
                        Q{quarter.quarterNumber}: {formatPercent(quarter.utilizationPercent)}
                      </span>
                    ))}
                  </div>
                </td>
              </tr>
            ))}
          </tbody>
        </table>

        {!payload.loading && members.length === 0 ? (
          <div className="manager-empty-state">No engineers are currently visible in this utilization scope.</div>
        ) : null}
      </div>

      <p className="section-copy">{data?.calculationNote}</p>
    </section>
  );
}
