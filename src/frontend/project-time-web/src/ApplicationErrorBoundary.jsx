import React from 'react';

export default class ApplicationErrorBoundary extends React.Component {
  constructor(props) {
    super(props);
    this.state = { error: null };
  }

  static getDerivedStateFromError(error) {
    return { error };
  }

  componentDidCatch(error, info) {
    console.error('[Pulse UI boundary]', error, info);
  }

  render() {
    if (!this.state.error) return this.props.children;
    return (
      <main className="app-shell" role="main">
        <section className="panel" style={{ maxWidth: '52rem', margin: '4rem auto', padding: '2rem' }}>
          <p className="eyebrow">Pulse workspace recovery</p>
          <h1>This page could not finish rendering.</h1>
          <p>Your session is still available. Reload the current route; if the service remains unavailable, return to the dashboard.</p>
          <div style={{ display: 'flex', flexWrap: 'wrap', gap: '.75rem' }}>
            <button type="button" className="primary-action" onClick={() => window.location.reload()}>Reload page</button>
            <button type="button" className="secondary-action" onClick={() => { window.location.hash = '#dashboard'; window.location.reload(); }}>Return to dashboard</button>
          </div>
        </section>
      </main>
    );
  }
}
