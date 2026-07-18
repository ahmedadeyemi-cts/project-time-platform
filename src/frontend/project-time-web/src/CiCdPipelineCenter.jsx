import { useCallback, useEffect, useMemo, useState } from 'react';
import './cicd-pipeline-center.css';

const readJson = async (url, init) => {
  const response = await fetch(url, init);
  const raw = await response.text();
  let body = null;
  try {
    body = raw ? JSON.parse(raw) : null;
  } catch {
    body = { message: raw };
  }
  if (!response.ok) {
    throw new Error(body?.message || `${url} returned HTTP ${response.status}`);
  }
  return body;
};

export default function CiCdPipelineCenter() {
  const [configuration, setConfiguration] = useState(null);
  const [status, setStatus] = useState(null);
  const [error, setError] = useState('');
  const [loading, setLoading] = useState(true);
  const [action, setAction] = useState('');
  const [message, setMessage] = useState('');

  const load = useCallback(async () => {
    setLoading(true);
    setError('');
    try {
      const results = await Promise.allSettled([
        readJson('/api/cicd/configuration'),
        readJson('/api/cicd/status')
      ]);

      const configurationResult = results[0];
      const statusResult = results[1];

      if (configurationResult.status === 'fulfilled') {
        setConfiguration(configurationResult.value);
      }

      if (statusResult.status === 'fulfilled') {
        setStatus(statusResult.value);
        if (statusResult.value?.configuration) {
          setConfiguration((current) =>
            current || statusResult.value.configuration
          );
        }
      }

      const errors = results
        .filter((result) => result.status === 'rejected')
        .map((result) => result.reason?.message)
        .filter(Boolean);

      if (errors.length === results.length) {
        throw new Error(errors.join(' | '));
      }

      if (errors.length) {
        setError(errors.join(' | '));
      }
    } catch (requestError) {
      setError(requestError.message);
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    void load();
  }, [load]);

  const repoUrl = useMemo(() => {
    const repository = configuration?.sourceControl?.repository;
    return repository ? `https://github.com/${repository}` : '';
  }, [configuration]);

  const dispatch = async (workflow) => {
    setAction(workflow);
    setMessage('');
    try {
      const body = await readJson('/api/cicd/dispatch', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          workflow,
          ref: configuration?.sourceControl?.defaultBranch,
          inputs: { environment: 'test' }
        })
      });
      setMessage(body?.status || 'Workflow dispatch accepted.');
      await load();
    } catch (requestError) {
      setMessage(requestError.message);
    } finally {
      setAction('');
    }
  };

  return (
    <div className="cicd-pipeline-center">
      <section className="cicd-hero">
        <div>
          <p className="eyebrow">Module 058</p>
          <h1>CI/CD Pipeline Administration</h1>
          <p>
            Administrator-only source validation, OCI image publication,
            deployment health, rollback readiness, and provider portability.
          </p>
        </div>
        <div className="cicd-hero-status">
          <strong>{configuration?.deployment?.environment || 'test'}</strong>
          <span>{configuration?.deployment?.provider || 'Loading provider'}</span>
        </div>
      </section>

      {error ? <div className="cicd-banner error">{error}</div> : null}
      {message ? <div className="cicd-banner">{message}</div> : null}

      <section className="cicd-summary-grid">
        <article>
          <span>Source control</span>
          <strong>{configuration?.sourceControl?.provider || status?.repository?.provider || 'GitHub'}</strong>
          <small>{configuration?.sourceControl?.repository || status?.repository?.name || 'ahmedadeyemi-cts/project-time-platform'}</small>
        </article>
        <article>
          <span>Deployment provider</span>
          <strong>{configuration?.deployment?.provider || 'azure-container-apps'}</strong>
          <small>Future provider: {configuration?.deployment?.futureProvider || 'OpenCloud'}</small>
        </article>
        <article>
          <span>SCM dispatch</span>
          <strong>{configuration?.sourceControl?.tokenConfigured ? 'Enabled' : 'Not configured'}</strong>
          <small>Read-only status remains available without a token.</small>
        </article>
        <article>
          <span>Artifact standard</span>
          <strong>OCI containers</strong>
          <small>Portable API, web, release manifest, and SBOM artifacts.</small>
        </article>
      </section>

      <section className="cicd-panel">
        <div className="cicd-section-heading">
          <div>
            <p className="eyebrow">Current runtime</p>
            <h2>Test deployment</h2>
          </div>
          <button onClick={load} disabled={loading}>
            {loading ? 'Refreshing…' : 'Refresh'}
          </button>
        </div>

        <dl className="cicd-details">
          <div><dt>Repository</dt><dd>{status?.repository?.name || configuration?.sourceControl?.repository || 'ahmedadeyemi-cts/project-time-platform'}</dd></div>
          <div><dt>Branch</dt><dd>{status?.repository?.branch || configuration?.sourceControl?.defaultBranch || 'source/module-058-cicd-pipeline-20260716'}</dd></div>
          <div><dt>Source commit</dt><dd>{status?.repository?.sourceCommit || 'Not configured'}</dd></div>
          <div><dt>SCM runtime</dt><dd>{status?.repository?.runtimeConnection || 'runtime_token_not_configured'}</dd></div>
          <div><dt>API application</dt><dd>{status?.runtime?.apiApplication || 'Not configured'}</dd></div>
          <div><dt>API revision</dt><dd>{status?.runtime?.apiRevision || 'Not configured'}</dd></div>
          <div><dt>Web application</dt><dd>{status?.runtime?.webApplication || 'Not configured'}</dd></div>
          <div><dt>Registry</dt><dd>{configuration?.deployment?.registry || 'Not configured'}</dd></div>
        </dl>
      </section>

      <section className="cicd-panel">
        <div className="cicd-section-heading">
          <div>
            <p className="eyebrow">Administrative actions</p>
            <h2>Build and deploy</h2>
          </div>
          {repoUrl ? <a href={`${repoUrl}/actions`} target="_blank" rel="noreferrer">Open workflows</a> : null}
        </div>

        <div className="cicd-actions">
          <button
            className="primary-action"
            disabled={!configuration?.sourceControl?.tokenConfigured || Boolean(action)}
            onClick={() => dispatch('projectpulse-deploy-test.yml')}
          >
            {action === 'projectpulse-deploy-test.yml' ? 'Dispatching…' : 'Deploy test'}
          </button>
          <button
            disabled={!configuration?.sourceControl?.tokenConfigured || Boolean(action)}
            onClick={() => dispatch('projectpulse-ci.yml')}
          >
            {action === 'projectpulse-ci.yml' ? 'Dispatching…' : 'Run validation'}
          </button>
          <button disabled title="Production requires GitHub environment approval.">
            Deploy production
          </button>
        </div>

        {!configuration?.sourceControl?.tokenConfigured ? (
          <p className="muted">
            In-application dispatch is intentionally disabled until
            PROJECTPULSE_CICD_SCM_TOKEN is configured. GitHub Actions can still
            run through the repository interface and OIDC.
          </p>
        ) : null}
      </section>

      <section className="cicd-panel">
        <div className="cicd-section-heading">
          <div>
            <p className="eyebrow">Recent activity</p>
            <h2>Pipeline executions</h2>
          </div>
          <span>{status?.recentRuns?.length || 0} loaded</span>
        </div>

        <div className="cicd-run-list">
          {(status?.recentRuns || []).map((run) => (
            <a key={run.id} href={run.url} target="_blank" rel="noreferrer">
              <div>
                <strong>{run.name}</strong>
                <small>{run.branch} · {String(run.commit || '').slice(0, 12)}</small>
              </div>
              <div>
                <span>{run.status}</span>
                <small>{run.conclusion || 'Pending'}</small>
              </div>
            </a>
          ))}
          {!loading && !(status?.recentRuns || []).length ? (
            <div className="cicd-empty">
              Recent runs will appear after the SCM status token is configured.
            </div>
          ) : null}
        </div>
      </section>

      <section className="cicd-panel">
        <p className="eyebrow">Portability</p>
        <h2>Provider-neutral release model</h2>
        <div className="cicd-portability-grid">
          <article>
            <strong>Source control</strong>
            <span>GitHub now</span>
            <small>Repository/provider settings remain configuration-driven.</small>
          </article>
          <article>
            <strong>Runtime</strong>
            <span>Azure Container Apps now</span>
            <small>OpenCloud provider implementation is reserved.</small>
          </article>
          <article>
            <strong>Artifacts</strong>
            <span>OCI images</span>
            <small>Portable across compatible registries and runtimes.</small>
          </article>
          <article>
            <strong>Identity</strong>
            <span>OIDC workload identity</span>
            <small>No reusable Azure password is required in GitHub.</small>
          </article>
        </div>
      </section>
    </div>
  );
}
