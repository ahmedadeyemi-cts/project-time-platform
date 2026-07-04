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

export default function YearlyUtilizationPanel() {
  const currentYear = new Date().getFullYear();
  const [selectedYear, setSelectedYear] = useState(currentYear >= 2026 && currentYear <= 2036 ? currentYear : 2026);
  const [data, setData] = useState({ loading: true, error: null, payload: null });

  const availableYears = useMemo(() => Array.from({ length: 11 }, (_, index) => 2026 + index), []);

  async function loadYearlyUtilization(year) {
    setData({ loading: true, error: null, payload: null });

    try {
      const payload = await fetchJson(`/api/utilization/yearly-status?year=${year}`);
      setData({ loading: false, error: null, payload });
    } catch (error) {
      setData({
        loading: false,
        error: error instanceof Error ? error.message : 'Unable to load yearly utilization.',
        payload: null
      });
    }
  }

  useEffect(() => {
    loadYearlyUtilization(selectedYear);
  }, [selectedYear]);

  return (
    <section className="yearly-utilization-panel">
      <div className="section-heading compact-heading">
        <div>
          <p className="eyebrow">Engineer utilization</p>
          <h2>Quarterly progress by year</h2>
          <p className="section-copy">
            Track Q1, Q2, Q3, and Q4 utilization, billable hours, and hours remaining to each target threshold.
          </p>
        </div>

        <select
          className="year-select"
          value={selectedYear}
          onChange={(event) => setSelectedYear(Number(event.target.value))}
        >
          {availableYears.map((year) => (
            <option value={year} key={year}>{year}</option>
          ))}
        </select>
      </div>

      {data.loading && <div className="manager-empty-state">Loading yearly utilization...</div>}
      {data.error && <div className="error-text">{data.error}</div>}

      {data.payload && (
        <>
          <div className="manager-status-row">
            <span>Year: <strong>{data.payload.year}</strong></span>
            <span>Quarter standard hours: <strong>{Number(data.payload.standardQuarterHours).toFixed(1)}</strong></span>
          </div>

          <div className="quarter-utilization-grid">
            {data.payload.quarters.map((quarter) => (
              <article className="quarter-utilization-card" key={quarter.quarterNumber}>
                <div className="quarter-card-header">
                  <div>
                    <p className="eyebrow">{quarter.quarterName}</p>
                    <h3>{Number(quarter.utilizationPercent).toFixed(2)}%</h3>
                  </div>
                  <span className="badge">{Number(quarter.billableHours).toFixed(2)} hrs</span>
                </div>

                <div className="quarter-summary-grid">
                  <span>
                    Current billable
                    <strong>{Number(quarter.billableHours).toFixed(2)} hrs</strong>
                  </span>
                  <span>
                    Next target
                    <strong>{quarter.nextTargetPercent ? `${quarter.nextTargetPercent}%` : 'Complete'}</strong>
                  </span>
                  <span>
                    Hours left
                    <strong>{Number(quarter.hoursToNextTarget).toFixed(2)} hrs</strong>
                  </span>
                </div>

                <div className="threshold-list">
                  {quarter.thresholds.map((threshold) => (
                    <div className={threshold.reached ? 'threshold-row reached' : 'threshold-row'} key={threshold.targetPercent}>
                      <span>{threshold.targetPercent}%</span>
                      <span>{Number(threshold.targetHours).toFixed(1)} hrs</span>
                      <strong>{threshold.reached ? 'Reached' : `${Number(threshold.hoursRemaining).toFixed(2)} hrs left`}</strong>
                    </div>
                  ))}
                </div>
              </article>
            ))}
          </div>
        </>
      )}
    </section>
  );
}
