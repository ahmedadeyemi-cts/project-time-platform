import { useCallback, useEffect, useMemo, useState } from 'react';
import { usSignalLogoDataUrl } from './assets/usSignalLogoData.js';
import './platform-resilience-planning-panel.css';
import './projectpulse-module-standard.css';

const consolidatedReportEndpoint = '/api/system/backup-dr/resilience-report';
const resilienceExportEndpoint = '/api/system/backup-dr/resilience-report/export';

const moduleConfiguration = {
  '014': {
    title: 'Environment & Production Readiness Planning',
    eyebrow: 'Module 014 · Production Planning',
    endpoint: '/api/system/backup-dr/production-planning',
    summary: 'Compare the active runtime with the intended production design, expose single-instance constraints, and keep readiness decisions provider-neutral.'
  },
  '015': {
    title: 'Backup, Recovery & Continuity',
    eyebrow: 'Module 015 · Recovery Continuity',
    endpoint: '/api/system/restore-validation/recovery-continuity',
    summary: 'Bring backup evidence, recovery objectives, restoration validation, continuity ownership, and approval history into one governed view.'
  },
  '017': {
    title: 'Availability, Regions & Failover',
    eyebrow: 'Module 017 · Redundancy & Failover',
    endpoint: '/api/system/replication-sync/redundancy-failover',
    summary: 'Review replicas, regional coverage, database and storage redundancy, failover prerequisites, accountable owners, and test evidence.'
  }
};

function sessionToken(authSession) {
  return authSession?.sessionToken
    ?? authSession?.token
    ?? authSession?.accessToken
    ?? window.localStorage.getItem('projectPulseSessionToken')
    ?? window.sessionStorage.getItem('projectPulseSessionToken')
    ?? '';
}

function requestHeaders(authSession) {
  const token = sessionToken(authSession);
  return token ? {
    Authorization: `Bearer ${token}`,
    'X-ProjectPulse-Session': token,
    'X-Project-Pulse-Session': token,
    'X-Session-Token': token
  } : {};
}

async function readJson(path, authSession) {
  const response = await fetch(path, {
    method: 'GET',
    credentials: 'include',
    cache: 'no-store',
    headers: {
      Accept: 'application/json',
      ...requestHeaders(authSession)
    }
  });
  const payload = await response.json().catch(() => null);
  if (!response.ok) {
    throw new Error(payload?.message ?? `Production resilience request returned HTTP ${response.status}.`);
  }
  return payload;
}

function words(value) {
  return String(value ?? 'not_recorded')
    .replaceAll('_', ' ')
    .replace(/\b\w/g, (letter) => letter.toUpperCase());
}

function display(value, fallback = 'Not recorded') {
  if (value === null || value === undefined || value === '') return fallback;
  if (value === 'not_recorded' || value === 'not_reported' || value === 'not_assigned') return fallback;
  return value;
}

function dateTime(value) {
  if (!value) return 'Not recorded';
  const parsed = new Date(value);
  return Number.isNaN(parsed.getTime()) ? 'Not recorded' : parsed.toLocaleString();
}

function objective(value) {
  if (value === null || value === undefined || value === '') return 'Not recorded';
  const parsed = Number(value);
  return Number.isFinite(parsed) ? `${parsed} minutes` : 'Not recorded';
}

function statusTone(value) {
  const normalized = String(value ?? '').toLowerCase();
  if (['failed', 'critical', 'unavailable', 'required', 'not_recorded', 'not_reported', 'not_configured', 'not_assigned', 'single_instance'].some((item) => normalized.includes(item))) return 'critical';
  if (['warning', 'planned', 'partial', 'configuration_observed'].some((item) => normalized.includes(item))) return 'warning';
  if (['ready', 'healthy', 'active', 'recorded', 'observed', 'configured', 'enabled', 'available'].some((item) => normalized.includes(item))) return 'healthy';
  return 'neutral';
}

function StatusPill({ value }) {
  return <span className={`group2b-resilience-status ${statusTone(value)}`}>{words(value)}</span>;
}

function MetricCard({ label, value, detail, status }) {
  return (
    <article className="group2b-resilience-metric">
      <div className="group2b-resilience-metric-heading">
        <span>{label}</span>
        {status ? <StatusPill value={status} /> : null}
      </div>
      <strong>{display(value)}</strong>
      {detail ? <p>{detail}</p> : null}
    </article>
  );
}

function DetailItem({ label, value, status }) {
  return (
    <div className="group2b-resilience-detail-item">
      <dt>{label}</dt>
      <dd>{display(value)}</dd>
      {status ? <StatusPill value={status} /> : null}
    </div>
  );
}

function EnvironmentComparison({ rows = [] }) {
  return (
    <div className="group2b-resilience-table-wrap">
      <table className="group2b-resilience-table">
        <thead>
          <tr>
            <th>Environment</th>
            <th>Purpose</th>
            <th>Provider</th>
            <th>Region</th>
            <th>Workload</th>
            <th>Replicas</th>
            <th>Status</th>
          </tr>
        </thead>
        <tbody>
          {rows.map((row, index) => (
            <tr key={`${row.environment}-${row.purpose}-${index}`}>
              <td>{display(row.environment)}</td>
              <td>{words(row.purpose)}</td>
              <td>{display(row.provider)}</td>
              <td>{display(row.region)}</td>
              <td>{display(row.workloadKind)}</td>
              <td>{row.replicaCount ?? 'Not recorded'}</td>
              <td><StatusPill value={row.status} /></td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

function ProductionPlanningDetail({ data }) {
  const detail = data?.productionPlanning;
  if (!detail) return null;

  return (
    <section className="group2b-resilience-card">
      <div className="group2b-resilience-section-heading">
        <div>
          <p className="group2b-resilience-eyebrow">Planned production design</p>
          <h3>Target-state decisions</h3>
          <p>Values remain “Not recorded” until an accountable owner supplies evidence. The screen never invents a provider or topology.</p>
        </div>
        <StatusPill value={detail.designStatus} />
      </div>
      <dl className="group2b-resilience-detail-grid">
        <DetailItem label="Target environment" value={detail.targetEnvironment} />
        <DetailItem label="Target provider" value={detail.targetProvider} />
        <DetailItem label="Target region" value={detail.targetRegion} />
        <DetailItem label="Target workload" value={detail.targetWorkloadKind} />
        <DetailItem label="Target replicas" value={detail.targetReplicaCount} />
        <DetailItem label="Database topology" value={detail.databaseTopology} />
        <DetailItem label="Storage topology" value={detail.storageTopology} />
        <DetailItem label="Network boundary" value={detail.networkBoundary} />
        <DetailItem label="Observability design" value={detail.observabilityDesign} />
        <DetailItem label="Release approval model" value={detail.releaseApprovalModel} />
        <DetailItem label="Rollback design" value={detail.rollbackDesign} />
        <DetailItem label="Planning notes" value={detail.notes} />
      </dl>
    </section>
  );
}

function RecoveryContinuityDetail({ data }) {
  const detail = data?.recoveryContinuity;
  if (!detail) return null;

  return (
    <>
      <section className="group2b-resilience-metric-grid compact">
        <MetricCard label="Recovery point objective" value={objective(detail.recoveryPointObjectiveMinutes)} detail="Maximum acceptable data-loss window." status={detail.recoveryPointObjectiveMinutes ? 'recorded' : 'not_recorded'} />
        <MetricCard label="Recovery time objective" value={objective(detail.recoveryTimeObjectiveMinutes)} detail="Maximum acceptable restoration window." status={detail.recoveryTimeObjectiveMinutes ? 'recorded' : 'not_recorded'} />
        <MetricCard label="Last successful backup" value={dateTime(detail.lastSuccessfulBackupAt)} detail={display(detail.policyName, 'Backup policy is not recorded.')} status={detail.backupEvidenceStatus} />
        <MetricCard label="Last recovery test" value={dateTime(detail.lastSuccessfulRecoveryTestAt)} detail="A status-page read is not treated as a successful recovery test." status={detail.recoveryTestEvidenceStatus} />
      </section>

      <section className="group2b-resilience-card">
        <div className="group2b-resilience-section-heading">
          <div>
            <p className="group2b-resilience-eyebrow">Recovery operating model</p>
            <h3>Backup, restore, and continuity evidence</h3>
          </div>
        </div>
        <dl className="group2b-resilience-detail-grid">
          <DetailItem label="Backup frequency" value={detail.backupFrequency} />
          <DetailItem label="Retention policy" value={detail.retentionPolicy} />
          <DetailItem label="Backup location" value={detail.backupLocation} />
          <DetailItem label="Storage status" value={detail.storageStatus} status={detail.storageStatus} />
          <DetailItem label="Storage evidence" value={detail.storageEvidence} />
          <DetailItem label="Recovery runbook" value={detail.recoveryRunbook} />
          <DetailItem label="Continuity communications" value={detail.continuityCommunicationPlan} />
        </dl>
      </section>
    </>
  );
}

function RedundancyFailoverDetail({ data }) {
  const detail = data?.redundancyFailover;
  if (!detail) return null;

  return (
    <>
      <section className="group2b-resilience-metric-grid compact">
        <MetricCard label="Observed replicas" value={detail.observedReplicaCount} detail="Reported by the active Group 2A platform adapter." status={detail.observedReplicaCount > 1 ? 'observed' : 'single_instance_observed'} />
        <MetricCard label="Regional coverage" value={(detail.observedRegions ?? []).join(', ') || 'Not recorded'} detail={`Target: ${display(detail.targetRegion)}`} status={(detail.observedRegions ?? []).length > 1 ? 'observed' : 'not_recorded'} />
        <MetricCard label="Database replica" value={words(detail.databaseReplicaStatus)} detail="Managed high availability and replica evidence are reported separately from database reachability." status={detail.databaseReplicaStatus} />
        <MetricCard label="Storage replication" value={words(detail.storageReplicationStatus)} detail={detail.storageAvailabilityEvidence} status={detail.storageReplicationStatus} />
      </section>

      <section className="group2b-resilience-card">
        <div className="group2b-resilience-section-heading">
          <div>
            <p className="group2b-resilience-eyebrow">Failover prerequisites</p>
            <h3>What must be true before failover is approved</h3>
            <p>Storage availability is not represented as storage replication, and one active process is not represented as redundant.</p>
          </div>
          <StatusPill value={detail.missingPrerequisiteCount === 0 ? 'ready_for_review' : 'evidence_required'} />
        </div>
        <div className="group2b-resilience-prerequisite-list">
          {(detail.failoverPrerequisites ?? []).map((item) => (
            <article key={item.code}>
              <div>
                <strong>{item.name}</strong>
                <p>{item.evidenceSource}</p>
              </div>
              <StatusPill value={item.status} />
            </article>
          ))}
        </div>
        <dl className="group2b-resilience-detail-grid top-space">
          <DetailItem label="Failover mode" value={detail.failoverMode} />
          <DetailItem label="Failover runbook" value={detail.failoverRunbook} />
          <DetailItem label="Last failover test" value={dateTime(detail.lastFailoverTestAt)} />
          <DetailItem label="Storage availability" value={detail.storageAvailabilityStatus} status={detail.storageAvailabilityStatus} />
        </dl>
      </section>
    </>
  );
}

function ReadinessGovernance({ readiness, evidence }) {
  const blockers = readiness?.blockers ?? [];
  const owners = readiness?.responsibleOwners ?? [];
  const observations = evidence?.latestObservations ?? [];
  const approvals = evidence?.approvalHistory ?? [];

  return (
    <section className="group2b-resilience-governance-grid">
      <article className="group2b-resilience-card">
        <div className="group2b-resilience-section-heading">
          <div>
            <p className="group2b-resilience-eyebrow">Readiness blockers</p>
            <h3>{blockers.length === 0 ? 'No contract blockers reported' : `${blockers.length} blocker${blockers.length === 1 ? '' : 's'} require ownership`}</h3>
          </div>
        </div>
        {blockers.length === 0 ? (
          <div className="group2b-resilience-empty">The contract is complete enough for human review. Approval is still a separate decision.</div>
        ) : (
          <div className="group2b-resilience-blocker-list">
            {blockers.map((blocker) => (
              <div key={blocker.code} className={`group2b-resilience-blocker ${blocker.severity}`}>
                <div>
                  <strong>{words(blocker.area)}</strong>
                  <p>{blocker.message}</p>
                  <small>{blocker.code}</small>
                </div>
                <span>{display(blocker.owner, 'Owner not assigned')}</span>
              </div>
            ))}
          </div>
        )}
      </article>

      <article className="group2b-resilience-card">
        <div className="group2b-resilience-section-heading">
          <div>
            <p className="group2b-resilience-eyebrow">Responsible owners</p>
            <h3>Accountability by operating area</h3>
          </div>
        </div>
        <div className="group2b-resilience-owner-list">
          {owners.map((owner) => (
            <div key={owner.area}>
              <span>{owner.responsibility}</span>
              <strong>{display(owner.owner, 'Owner not assigned')}</strong>
              <StatusPill value={owner.status} />
            </div>
          ))}
        </div>
      </article>

      <article className="group2b-resilience-card">
        <div className="group2b-resilience-section-heading">
          <div>
            <p className="group2b-resilience-eyebrow">Evidence history</p>
            <h3>Recent bounded observations</h3>
            <p>Request telemetry is evidence of observation, not proof that a backup, restore, or failover exercise succeeded.</p>
          </div>
        </div>
        {observations.length === 0 ? (
          <div className="group2b-resilience-empty">No bounded Module observations have been recorded in this process.</div>
        ) : (
          <div className="group2b-resilience-evidence-list">
            {observations.slice(0, 8).map((item) => (
              <div key={item.evidenceId}>
                <div>
                  <strong>{words(item.eventType)}</strong>
                  <span>{item.method} {item.path}</span>
                </div>
                <div>
                  <StatusPill value={item.status} />
                  <small>{dateTime(item.observedAt)}</small>
                </div>
              </div>
            ))}
          </div>
        )}
      </article>

      <article className="group2b-resilience-card">
        <div className="group2b-resilience-section-heading">
          <div>
            <p className="group2b-resilience-eyebrow">Approval history</p>
            <h3>Governed readiness approval</h3>
          </div>
          <StatusPill value={evidence?.approvalStatus} />
        </div>
        {approvals.length === 0 ? (
          <div className="group2b-resilience-empty">No production resilience approval has been recorded by the current contract.</div>
        ) : (
          <div className="group2b-resilience-approval-list">
            {approvals.map((approval, index) => (
              <div key={`${approval.reference}-${index}`}>
                <strong>{display(approval.approvedBy)}</strong>
                <span>{dateTime(approval.approvedAt)}</span>
                <small>{display(approval.reference, 'No approval reference')}</small>
              </div>
            ))}
          </div>
        )}
      </article>
    </section>
  );
}

function ReportingContract({ reporting }) {
  if (!reporting) return null;
  return (
    <section className="group2b-resilience-card group2b-resilience-reporting">
      <div className="group2b-resilience-section-heading">
        <div>
          <p className="group2b-resilience-eyebrow">Reporting API contract</p>
          <h3>One provider-neutral reporting model</h3>
          <p>{reporting.futureAdapterRule}</p>
        </div>
      </div>
      <div className="group2b-resilience-api-grid">
        {[...(reporting.moduleApis ?? []), ...(reporting.sharedSourceApis ?? []), ...(reporting.operationalSourceApis ?? [])].map((path) => (
          <code key={path}>GET {path}</code>
        ))}
        <code>GET {reporting.consolidatedReportApi ?? consolidatedReportEndpoint}</code>
        <code>GET {reporting.exportApi ?? resilienceExportEndpoint}</code>
      </div>
      <p className="group2b-resilience-contract-note">{reporting.missingEvidenceRule}</p>
    </section>
  );
}

export default function PlatformResiliencePlanningPanel({ moduleCode, authSession }) {
  const configuration = moduleConfiguration[moduleCode] ?? moduleConfiguration['014'];
  const [state, setState] = useState({ loading: true, data: null, error: '' });
  const [exportState, setExportState] = useState({ loading: false, error: '' });

  const load = useCallback(async () => {
    setState((current) => ({ ...current, loading: true, error: '' }));
    try {
      const data = await readJson(configuration.endpoint, authSession);
      setState({ loading: false, data, error: '' });
    } catch (error) {
      setState({
        loading: false,
        data: null,
        error: error instanceof Error ? error.message : 'Unable to load provider-neutral production resilience data.'
      });
    }
  }, [configuration.endpoint, authSession]);

  useEffect(() => {
    load();
  }, [load]);

  const exportReport = useCallback(async () => {
    setExportState({ loading: true, error: '' });
    try {
      const response = await fetch(resilienceExportEndpoint, {
        method: 'GET',
        credentials: 'include',
        cache: 'no-store',
        headers: requestHeaders(authSession)
      });
      if (!response.ok) {
        const payload = await response.json().catch(() => null);
        throw new Error(payload?.message ?? `Report export returned HTTP ${response.status}.`);
      }
      const blob = await response.blob();
      const disposition = response.headers.get('content-disposition') ?? '';
      const match = disposition.match(/filename\*?=(?:UTF-8''|\")?([^\";]+)/i);
      const fileName = match?.[1] ? decodeURIComponent(match[1].replaceAll('"', '')) : 'projectpulse-production-resilience.json';
      const url = URL.createObjectURL(blob);
      const link = document.createElement('a');
      link.href = url;
      link.download = fileName;
      document.body.appendChild(link);
      link.click();
      link.remove();
      URL.revokeObjectURL(url);
      setExportState({ loading: false, error: '' });
    } catch (error) {
      setExportState({
        loading: false,
        error: error instanceof Error ? error.message : 'Unable to export production resilience report.'
      });
    }
  }, [authSession]);

  const platform = state.data?.platform;
  const current = platform?.current;
  const target = platform?.target;
  const readiness = state.data?.readiness;
  const singleInstance = state.data?.singleInstance;
  const moduleDetail = state.data?.recoveryContinuity ?? state.data?.redundancyFailover ?? state.data?.productionPlanning;
  const objectiveMetrics = useMemo(() => {
    const recovery = state.data?.recoveryContinuity;
    return recovery ? `${objective(recovery.recoveryPointObjectiveMinutes)} RPO · ${objective(recovery.recoveryTimeObjectiveMinutes)} RTO` : null;
  }, [state.data]);

  return (
    <section className="group2b-resilience-shell projectpulse-module-standard" data-projectpulse-group2b="provider-neutral" data-module-code={moduleCode}>
      <header className="group2b-resilience-hero">
        <div className="group2b-resilience-brand-block">
          <img src={usSignalLogoDataUrl} alt="US Signal" />
          <div>
            <p className="group2b-resilience-eyebrow">{configuration.eyebrow}</p>
            <h2>{configuration.title}</h2>
            <p>{configuration.summary}</p>
          </div>
        </div>
        <div className="group2b-resilience-actions">
          {state.data?.status ? <StatusPill value={state.data.status} /> : null}
          <button type="button" className="group2b-resilience-secondary" onClick={load} disabled={state.loading}>
            {state.loading ? 'Refreshing…' : 'Refresh planning data'}
          </button>
          <button type="button" className="group2b-resilience-primary" onClick={exportReport} disabled={exportState.loading}>
            {exportState.loading ? 'Preparing report…' : 'Export resilience report'}
          </button>
        </div>
      </header>

      <div className="group2b-resilience-provider-banner">
        <strong>Provider-neutral by design</strong>
        <span>Current data comes from the Group 2A platform adapter. Azure-specific details stay behind that adapter, and a future provider can populate the same contract without rebuilding these modules.</span>
      </div>

      {state.error ? (
        <div className="group2b-resilience-alert">
          <strong>Provider-neutral planning data is unavailable.</strong>
          <p>{state.error}</p>
          <small>The existing operational controls below remain available; no readiness value is guessed.</small>
        </div>
      ) : null}
      {exportState.error ? <div className="group2b-resilience-alert compact"><strong>Report export failed.</strong><p>{exportState.error}</p></div> : null}

      {state.loading && !state.data ? <div className="group2b-resilience-loading">Loading current platform, target design, recovery, and failover evidence…</div> : null}

      {state.data ? (
        <>
          <section className="group2b-resilience-metric-grid">
            <MetricCard label="Current platform" value={current?.displayName ?? current?.provider} detail={`${display(current?.environment)} · ${display(current?.region)} · ${display(current?.workloadKind)}`} status={current?.adapterStatus} />
            <MetricCard label="Target platform" value={target?.displayName ?? target?.provider} detail={`${display(target?.environment)} · ${display(target?.region)} · ${target?.replicaCount ?? 'Replica target not recorded'}`} status={target?.provider === 'not_recorded' ? 'not_recorded' : 'planned'} />
            <MetricCard label="Single-instance constraint" value={singleInstance?.singleInstance ? 'Observed' : 'Multiple instances observed'} detail={(singleInstance?.limitations ?? [])[0]} status={singleInstance?.status} />
            <MetricCard label="Readiness blockers" value={readiness?.blockerCount ?? 0} detail={objectiveMetrics ?? 'Missing values remain explicit blockers.'} status={(readiness?.blockerCount ?? 0) === 0 ? 'ready_for_review' : 'evidence_required'} />
          </section>

          <section className="group2b-resilience-card">
            <div className="group2b-resilience-section-heading">
              <div>
                <p className="group2b-resilience-eyebrow">Current versus target</p>
                <h3>Environment comparison</h3>
                <p>The current row is observed. Test and production rows are operator-recorded planning contracts and remain incomplete until evidence exists.</p>
              </div>
            </div>
            <EnvironmentComparison rows={platform?.environmentComparison ?? []} />
          </section>

          <ProductionPlanningDetail data={state.data} />
          <RecoveryContinuityDetail data={state.data} />
          <RedundancyFailoverDetail data={state.data} />
          <ReadinessGovernance readiness={readiness} evidence={state.data.evidence} />
          <ReportingContract reporting={state.data.reporting} />

          <footer className="group2b-resilience-footer">
            <span>Contract {state.data.contractVersion}</span>
            <span>Generated {dateTime(state.data.generatedAt)}</span>
            <span>{words(state.data.responsibility)}</span>
            {moduleDetail ? <span>Source: Group 2A platform abstraction</span> : null}
          </footer>
        </>
      ) : null}
    </section>
  );
}
