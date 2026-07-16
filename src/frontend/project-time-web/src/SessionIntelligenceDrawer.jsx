import { useEffect, useMemo, useState } from 'react';
import './session-intelligence-drawer.css';

const safe = (value, fallback = 'Not available') =>
  value === undefined || value === null || value === '' ? fallback : String(value);

function readDevice() {
  const ua = navigator.userAgent || '';

  return {
    browser: ua.includes('Edg/') ? 'Microsoft Edge'
      : ua.includes('Firefox/') ? 'Mozilla Firefox'
      : ua.includes('Chrome/') ? 'Google Chrome'
      : ua.includes('Safari/') ? 'Apple Safari'
      : 'Unknown browser',
    os: /Windows/i.test(ua) ? 'Windows'
      : /Mac OS X/i.test(ua) ? 'macOS'
      : /Android/i.test(ua) ? 'Android'
      : /iPhone|iPad/i.test(ua) ? 'iOS'
      : /Linux/i.test(ua) ? 'Linux'
      : 'Unknown OS',
    type: /Mobi|Android|iPhone/i.test(ua) ? 'Mobile'
      : /iPad|Tablet/i.test(ua) ? 'Tablet'
      : 'Desktop / laptop'
  };
}

const SectionIcon = ({ name }) => {
  const icons = {
    Identity: '◯',
    Authorization: '◇',
    Session: '◷',
    Device: '▱',
    Network: '⌁',
    'Client Environment': '⊕',
    Deployment: '‹/›',
    'Privacy & Security': '▣',
    Diagnostics: '⌕'
  };

  return <span className="uss-si-section-icon">{icons[name] || '•'}</span>;
};

export default function SessionIntelligenceDrawer({ authSession }) {
  const [open, setOpen] = useState(false);
  const [server, setServer] = useState(null);
  const [error, setError] = useState('');
  const device = useMemo(readDevice, []);

  useEffect(() => {
    let active = true;

    fetch('/api/security/session-intelligence')
      .then(async (response) => {
        const body = await response.json().catch(() => ({}));
        if (!response.ok) throw new Error(`HTTP ${response.status}`);
        if (active) setServer(body);
      })
      .catch((requestError) => {
        if (active) setError(requestError.message);
      });

    return () => {
      active = false;
    };
  }, []);

  const signedInUser =
    authSession?.username ||
    authSession?.email ||
    'Authenticated user';

  const displayName =
    authSession?.displayName ||
    authSession?.name ||
    signedInUser.split('@')[0]
      .split(/[._-]/)
      .map((part) => part.charAt(0).toUpperCase() + part.slice(1))
      .join(' ');

  const role =
    authSession?.role ||
    authSession?.roleName ||
    'Backend resolved';

  const permissionCount =
    authSession?.permissions?.length ??
    authSession?.permissionCount ??
    'Backend resolved';

  const sections = [
    {
      title: 'Identity',
      rows: [
        ['Display name', displayName],
        ['Signed-in user', signedInUser],
        ['Identity provider', 'Microsoft Entra ID'],
        ['Tenant', 'OneNeck / US Signal test tenant']
      ]
    },
    {
      title: 'Authorization',
      rows: [
        ['Role', role],
        ['Permissions', permissionCount],
        ['View-As guard', authSession?.isViewAs ? 'Active' : 'Inactive'],
        ['Administrative context', role]
      ]
    },
    {
      title: 'Session',
      rows: [
        ['Session token', 'Present — value hidden'],
        ['Backend validation', error ? 'Unavailable' : 'Confirmed'],
        ['Trace identifier', server?.request?.traceIdentifier],
        ['Write operations', 'None']
      ]
    },
    {
      title: 'Device',
      rows: [
        ['Device type', device.type],
        ['Operating system', device.os],
        ['Browser', device.browser],
        ['Touch support', navigator.maxTouchPoints > 0 ? 'Yes' : 'No'],
        ['CPU cores', navigator.hardwareConcurrency],
        ['Device memory', navigator.deviceMemory ? `${navigator.deviceMemory} GB` : 'Not exposed']
      ]
    },
    {
      title: 'Network',
      rows: [
        ['Client IP', server?.network?.publicIp || 'Loading'],
        ['Forwarded address', server?.network?.forwardedForPresent ? 'Present' : 'Not present'],
        ['Protocol', server?.network?.protocol],
        ['Host', server?.network?.host],
        ['VPN / proxy', 'Not asserted without trusted detection']
      ]
    },
    {
      title: 'Client Environment',
      rows: [
        ['Time zone', Intl.DateTimeFormat().resolvedOptions().timeZone],
        ['Language', navigator.language],
        ['Screen', `${window.screen.width} × ${window.screen.height}`],
        ['Viewport', `${window.innerWidth} × ${window.innerHeight}`],
        ['Theme', matchMedia('(prefers-color-scheme: dark)').matches ? 'Dark' : 'Light'],
        ['Online state', navigator.onLine ? 'Online' : 'Offline']
      ]
    },
    {
      title: 'Deployment',
      rows: [
        ['Environment', server?.runtime?.environment],
        ['API revision', server?.runtime?.apiRevision],
        ['API replica', server?.runtime?.apiReplica],
        ['Source commit', server?.runtime?.sourceCommit],
        ['SCM', 'GitHub'],
        ['Future runtime', 'OpenCloud-ready OCI model']
      ]
    },
    {
      title: 'Privacy & Security',
      rows: [
        ['Browser fingerprinting', 'No'],
        ['Token values returned', 'No'],
        ['Secrets returned', 'No'],
        ['Precise location', 'Not collected'],
        ['Diagnostics', 'Sanitized only']
      ]
    },
    {
      title: 'Diagnostics',
      rows: [
        ['Server context', error ? `Unavailable: ${error}` : 'Available'],
        ['Session intelligence API', error ? 'Warning' : 'Healthy'],
        ['Clipboard export', 'Sanitized JSON'],
        ['Support package', 'Ready']
      ]
    }
  ];

  const diagnostics = {
    displayName,
    signedInUser,
    role,
    permissionCount,
    clientIp: server?.network?.publicIp,
    deviceType: device.type,
    operatingSystem: device.os,
    browser: device.browser,
    timeZone: Intl.DateTimeFormat().resolvedOptions().timeZone,
    language: navigator.language,
    screen: `${window.screen.width} × ${window.screen.height}`,
    viewport: `${window.innerWidth} × ${window.innerHeight}`,
    apiRevision: server?.runtime?.apiRevision,
    sourceCommit: server?.runtime?.sourceCommit
  };

  return (
    <>
      <button
        type="button"
        className="uss-session-intelligence-handle"
        onClick={() => setOpen((current) => !current)}
        aria-expanded={open}
      >
        {open ? 'Close' : 'US Signal Session Intelligence'}
      </button>

      <aside className={`uss-session-intelligence-panel ${open ? 'is-open' : ''}`}>
        <header className="uss-si-header">
          <div>
            <p><span>◉</span> US Signal</p>
            <h2>Session Intelligence</h2>
            <small>Real-time session context</small>
          </div>
          <button type="button" onClick={() => setOpen(false)} aria-label="Close">×</button>
        </header>

        <section className="uss-si-user">
          <div className="uss-si-avatar">
            {displayName.split(' ').slice(0, 2).map((part) => part[0]).join('')}
          </div>
          <div>
            <strong>{displayName}</strong>
            <span>{signedInUser}</span>
            <em>{safe(role)}</em>
          </div>
        </section>

        <div className="uss-si-content">
          {sections.map((section) => (
            <details key={section.title}>
              <summary>
                <span className="uss-si-summary-label">
                  <SectionIcon name={section.title} />
                  {section.title}
                </span>
                <span className="uss-si-chevron">⌄</span>
              </summary>

              <dl>
                {section.rows.map(([label, entry]) => (
                  <div key={label}>
                    <dt>{label}</dt>
                    <dd>{safe(entry)}</dd>
                  </div>
                ))}
              </dl>
            </details>
          ))}
        </div>

        <div className="uss-si-actions">
          <button
            type="button"
            onClick={() =>
              navigator.clipboard.writeText(JSON.stringify(diagnostics, null, 2))
            }
          >
            Copy Safe Diagnostics
          </button>

          <button
            type="button"
            onClick={() => {
              const blob = new Blob(
                [JSON.stringify(diagnostics, null, 2)],
                { type: 'application/json' }
              );
              const url = URL.createObjectURL(blob);
              const anchor = document.createElement('a');
              anchor.href = url;
              anchor.download = 'ussignal-session-diagnostics.json';
              anchor.click();
              URL.revokeObjectURL(url);
            }}
          >
            Export Support Package
          </button>

          <small>No secrets returned • No tokens • No write operations performed</small>
        </div>

        <footer className="uss-si-footer">
          <span>US Signal • ProjectPulse</span>
          <span>Module 059</span>
        </footer>
      </aside>

      {open ? (
        <button
          type="button"
          className="uss-session-intelligence-backdrop"
          onClick={() => setOpen(false)}
          aria-label="Close session intelligence"
        />
      ) : null}
    </>
  );
}
