import React from 'react';
import {
  EXTERNAL_DOM_RECOVERY_WINDOW_MS,
  claimExternalDomMutationRecovery,
  isRecoverableExternalDomMutationError,
  protectReactOwnedRoot,
  publishExternalDomMutationRecovery
} from './external-dom-mutation-resilience.js';

export default class ApplicationErrorBoundary extends React.Component {
  constructor(props) {
    super(props);
    this.state = {
      error: null,
      recoveryEpoch: 0,
      automaticRecoveryPending: false
    };
    this.automaticRecoveryAttempted = false;
    this.recoveryTimer = null;
    this.recoveryResetTimer = null;
    this.retryWorkspace = this.retryWorkspace.bind(this);
  }

  static getDerivedStateFromError(error) {
    return {
      error,
      automaticRecoveryPending: isRecoverableExternalDomMutationError(error)
    };
  }

  componentDidCatch(error, info) {
    console.error('[Pulse UI boundary]', error, info);

    if (!isRecoverableExternalDomMutationError(error)) return;

    const automaticRecoveryAllowed = !this.automaticRecoveryAttempted
      && claimExternalDomMutationRecovery();
    this.automaticRecoveryAttempted = true;

    if (!automaticRecoveryAllowed) {
      this.setState({ automaticRecoveryPending: false });
      return;
    }

    publishExternalDomMutationRecovery(error, info);
    this.recoveryTimer = window.setTimeout(() => {
      this.retryWorkspace({ automatic: true });
    }, 50);
  }

  componentDidUpdate(_previousProps, previousState) {
    if (previousState.error && !this.state.error) {
      window.clearTimeout(this.recoveryResetTimer);
      this.recoveryResetTimer = window.setTimeout(() => {
        this.automaticRecoveryAttempted = false;
      }, EXTERNAL_DOM_RECOVERY_WINDOW_MS);
    }
  }

  componentWillUnmount() {
    window.clearTimeout(this.recoveryTimer);
    window.clearTimeout(this.recoveryResetTimer);
  }

  retryWorkspace(options = {}) {
    window.clearTimeout(this.recoveryTimer);
    this.recoveryTimer = null;
    protectReactOwnedRoot(document.getElementById('root'));

    if (options?.automatic !== true) {
      this.automaticRecoveryAttempted = false;
    }

    this.setState((current) => ({
      error: null,
      recoveryEpoch: current.recoveryEpoch + 1,
      automaticRecoveryPending: false
    }));
  }

  render() {
    if (!this.state.error) {
      return (
        <React.Fragment key={`pulse-workspace-${this.state.recoveryEpoch}`}>
          {this.props.children}
        </React.Fragment>
      );
    }

    if (this.state.automaticRecoveryPending) {
      return (
        <main className="app-shell" role="main" aria-live="polite">
          <section className="panel" style={{ maxWidth: '52rem', margin: '4rem auto', padding: '2rem' }}>
            <p className="eyebrow">Pulse workspace recovery</p>
            <h1>Restoring your workspace...</h1>
            <p>Pulse detected that another browser component changed the page while it was rendering. The current route is being rebuilt without ending your session.</p>
          </section>
        </main>
      );
    }

    return (
      <main className="app-shell" role="main">
        <section className="panel" style={{ maxWidth: '52rem', margin: '4rem auto', padding: '2rem' }}>
          <p className="eyebrow">Pulse workspace recovery</p>
          <h1>This page could not finish rendering.</h1>
          <p>Your session is still available. Try the workspace again; if the browser conflict continues, reload the current route or return to the dashboard.</p>
          <div style={{ display: 'flex', flexWrap: 'wrap', gap: '.75rem' }}>
            <button type="button" className="primary-action" onClick={this.retryWorkspace}>Try workspace again</button>
            <button type="button" className="secondary-action" onClick={() => window.location.reload()}>Reload page</button>
            <button type="button" className="secondary-action" onClick={() => { window.location.hash = '#dashboard'; window.location.reload(); }}>Return to dashboard</button>
          </div>
        </section>
      </main>
    );
  }
}
