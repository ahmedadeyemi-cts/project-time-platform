import { useEffect, useMemo, useState } from 'react';

function getProjectPulseAuthHeaders() {
  try {
    const rawSession = window.localStorage.getItem('projectPulseAuthSession');
    if (!rawSession) return {};
    const session = JSON.parse(rawSession);
    return session?.sessionToken ? { 'X-Project Health Dashboard-Session': session.sessionToken } : {};
  } catch {
    return {};
  }
}

async function fetchJson(path) {
  const response = await fetch(path, {
    headers: getProjectPulseAuthHeaders()
  });

  if (!response.ok) {
    const raw = await response.text();
    throw new Error(raw || `${path} returned HTTP ${response.status}`);
  }

  return response.json();
}

export default function ManagerTeamUtilizationPanel() {
  const [selectedYear, setSelectedYear] = useState(2026);
  const [data, setData] = useState({ loading: true, error: null, payload: null });

  const availableYears = useMemo(() => Array.from({ length: 11 }, (_, index) => 2026 + index), []);

  async function loadManagerUtilization(year) {
    setData({ loading: true, error: null, payload: null });

    try {
      const payload = await fetchJson(`/api/utilization/manager-team-summary?year=${year}`);
      setData({ loading: false, error: null, payload });
    } catch (error) {
      setData({
        loading: false,
        error: error instanceof Error ? error.message : 'Unable to load manager utilization.',
        payload: null
      });
    }
  }

  useEffect(() => {
    loadManagerUtilization(selectedYear);
  }, [selectedYear]);

  if (data.loading) {
    return <section className="manager-team-utilization-panel"><div className="manager-empty-state">Loading manager utilization...</div></section>;
  }

  if (data.error) {
    return <section className="manager-team-utilization-panel"><div className="error-text">{data.error}</div></section>;
  }

  if (!data.payload?.canViewManagerUtilization) {
    return null;
  }

  return (
    <section className="manager-team-utilization-panel">
      <div className="section-heading compact-heading">
        <div>
          <p className="eyebrow">Manager utilization</p>
          <h2>Team utilization summary</h2>
          <p className="section-copy">
            Review utilization by managed team, individual team member, and collective portfolio.
          </p>
        </div>

        <select className="year-select" value={selectedYear} onChange={(event) => setSelectedYear(Number(event.target.value))}>
          {availableYears.map((year) => <option value={year} key={year}>{year}</option>)}
        </select>
      </div>

      <div className="manager-status-row">
        <span>Managed teams: <strong>{data.payload.managedTeams?.join(', ') || 'None'}</strong></span>
        <span>Team members: <strong>{data.payload.collectiveSummary?.memberCount ?? 0}</strong></span>
        <span>Collective utilization: <strong>{Number(data.payload.collectiveSummary?.annualUtilizationPercent ?? 0).toFixed(2)}%</strong></span>
      </div>

      <div className="team-utilization-summary-grid">
        {data.payload.teamSummaries?.map((team) => (
          <article className="team-utilization-card" key={team.teamName}>
            <div className="quarter-card-header">
              <div>
                <p className="eyebrow">{team.teamName}</p>
                <h3>{Number(team.annualUtilizationPercent).toFixed(2)}%</h3>
              </div>
              <span className="badge">{team.memberCount} members</span>
            </div>

            <div className="project-hours-summary">
              <span>Annual billable <strong>{Number(team.annualBillableHours).toFixed(2)} hrs</strong></span>
              {team.quarters?.map((quarter) => (
                <span key={quarter.quarterName}>
                  {quarter.quarterName}
                  <strong>{Number(quarter.utilizationPercent).toFixed(2)}%</strong>
                  <small>{Number(quarter.billableHours).toFixed(2)} hrs</small>
                </span>
              ))}
            </div>
          </article>
        ))}
      </div>

      <div className="manager-team-member-table-wrap">
        <table className="manager-table">
          <thead>
            <tr>
              <th>Team member</th>
              <th>Team</th>
              <th>Annual utilization</th>
              <th>Annual billable</th>
              <th>Q1</th>
              <th>Q2</th>
              <th>Q3</th>
              <th>Q4</th>
            </tr>
          </thead>
          <tbody>
            {data.payload.teamMembers?.map((member) => (
              <tr key={member.userId}>
                <td>
                  <strong>{member.displayName}</strong>
                  <span>{member.email}</span>
                </td>
                <td>{member.teamName ?? member.departmentName ?? 'Unassigned'}</td>
                <td>{Number(member.annualUtilizationPercent).toFixed(2)}%</td>
                <td>{Number(member.annualBillableHours).toFixed(2)} hrs</td>
                {member.quarters?.map((quarter) => (
                  <td key={`${member.userId}-${quarter.quarterName}`}>
                    <strong>{Number(quarter.utilizationPercent).toFixed(2)}%</strong>
                    <span>{Number(quarter.billableHours).toFixed(2)} hrs</span>
                  </td>
                ))}
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      <p className="section-copy">{data.payload.calculationNote}</p>
    </section>
  );
}
