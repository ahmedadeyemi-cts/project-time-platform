import { useSyncExternalStore } from 'react';
import {
  EnterpriseStatusCard,
  EnterpriseSummaryStrip,
  EnterpriseWarning
} from '../enterprise/EnterpriseModulePresentation.jsx';
import {
  getAiProviderReadinessSnapshot,
  refreshAiProviderReadiness,
  subscribeAiProviderReadiness
} from './ai-provider-readiness-store.js';
import './ai-provider-readiness.css';

function words(value) {
  return String(value || 'unknown')
    .replaceAll('_', ' ')
    .replace(/\b\w/g, (character) => character.toUpperCase());
}

function dateTime(value) {
  if (!value) return 'Not recorded';
  const parsed = new Date(value);
  return Number.isNaN(parsed.getTime()) ? 'Not recorded' : parsed.toLocaleString();
}

function tone(status) {
  if (status === 'available') return 'healthy';
  if (status === 'checking' || status === 'rate_limited') return 'warning';
  if (status === 'not_configured') return 'neutral';
  return 'critical';
}

export default function AiProviderReadinessPanel() {
  const readiness = useSyncExternalStore(
    subscribeAiProviderReadiness,
    getAiProviderReadinessSnapshot,
    getAiProviderReadinessSnapshot
  );

  const providers = readiness.providers ?? [];
  const configuredCount = providers.filter((provider) => provider.configured).length;
  const availableCount = providers.filter((provider) => provider.status === 'available').length;

  return (
    <section className="group7-provider-readiness" data-group7-provider-readiness="persistent-non-secret">
      <div className="group7-provider-readiness__heading">
        <div>
          <p>Provider reliability</p>
          <h2>Last verified AI provider readiness</h2>
          <span>
            Navigation does not clear the last verified non-secret status. Startup, focus, and background checks share one deduplicated request.
          </span>
        </div>
        <button
          type="button"
          onClick={() => refreshAiProviderReadiness({ force: true, reason: 'manual_retest' })}
          disabled={readiness.phase === 'checking'}
        >
          {readiness.phase === 'checking' ? 'Checking…' : 'Retest providers'}
        </button>
      </div>

      <EnterpriseSummaryStrip ariaLabel="AI provider readiness summary">
        <EnterpriseStatusCard
          label="Current state"
          value={readiness.phase === 'checking' ? 'Checking' : words(readiness.overallStatus)}
          detail={readiness.lastVerifiedAt ? `Last verified ${dateTime(readiness.lastVerifiedAt)}` : 'No verified status has been recorded.'}
          tone={tone(readiness.phase === 'checking' ? 'checking' : readiness.overallStatus)}
        />
        <EnterpriseStatusCard
          label="Configured providers"
          value={`${configuredCount}`}
          detail={`${availableCount} currently available`}
          tone={availableCount > 0 ? 'healthy' : configuredCount > 0 ? 'warning' : 'neutral'}
        />
        <EnterpriseStatusCard
          label="Freshness"
          value={readiness.stale ? 'Stale' : 'Current'}
          detail={`Last check ${dateTime(readiness.lastCheckedAt)}`}
          tone={readiness.stale ? 'warning' : 'informational'}
        />
      </EnterpriseSummaryStrip>

      {readiness.errorCode ? (
        <EnterpriseWarning
          title="Provider refresh did not complete"
          message={`${readiness.message} Diagnostic: ${readiness.errorCode}.`}
          tone={readiness.lastVerifiedAt ? 'warning' : 'critical'}
        />
      ) : null}

      <div className="group7-provider-readiness__providers">
        {providers.map((provider) => (
          <article key={provider.provider} className={`group7-provider-readiness__provider is-${provider.status}`}>
            <div>
              <strong>{provider.displayName}</strong>
              <span>{words(provider.status)}</span>
            </div>
            <dl>
              <div><dt>Configured</dt><dd>{provider.configured ? 'Yes' : 'No'}</dd></div>
              <div><dt>Enabled</dt><dd>{provider.enabled ? 'Yes' : 'No'}</dd></div>
              <div><dt>Last checked</dt><dd>{dateTime(provider.lastCheckedAt)}</dd></div>
              <div><dt>Last success</dt><dd>{dateTime(provider.lastSuccessAt)}</dd></div>
              <div><dt>Diagnostic</dt><dd>{provider.diagnosticCode || 'None'}</dd></div>
            </dl>
          </article>
        ))}
        {!providers.length ? (
          <div className="group7-provider-readiness__empty">
            Provider readiness has not been returned yet. The controller will retry in the background.
          </div>
        ) : null}
      </div>
    </section>
  );
}
