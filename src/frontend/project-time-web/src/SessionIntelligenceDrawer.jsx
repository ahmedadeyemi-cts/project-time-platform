import { useEffect, useMemo, useState } from 'react';
import './session-intelligence-drawer.css';

const safe = (v, f = 'Not available') =>
  v === undefined || v === null || v === '' ? f : String(v);

function getDevice() {
  const ua = navigator.userAgent || '';
  return {
    browser: ua.includes('Edg/') ? 'Microsoft Edge'
      : ua.includes('Firefox/') ? 'Mozilla Firefox'
      : ua.includes('Chrome/') ? 'Google Chrome'
      : ua.includes('Safari/') ? 'Apple Safari' : 'Unknown browser',
    os: /Windows/i.test(ua) ? 'Windows'
      : /Mac OS X/i.test(ua) ? 'macOS'
      : /Android/i.test(ua) ? 'Android'
      : /iPhone|iPad/i.test(ua) ? 'iOS'
      : /Linux/i.test(ua) ? 'Linux' : 'Unknown OS',
    type: /Mobi|Android|iPhone/i.test(ua) ? 'Mobile'
      : /iPad|Tablet/i.test(ua) ? 'Tablet' : 'Desktop / laptop'
  };
}

export default function SessionIntelligenceDrawer({ authSession }) {
  const [open, setOpen] = useState(false);
  const [server, setServer] = useState(null);
  const [error, setError] = useState('');
  const device = useMemo(getDevice, []);

  useEffect(() => {
    fetch('/api/security/session-intelligence')
      .then(async r => {
        const body = await r.json().catch(() => ({}));
        if (!r.ok) throw new Error(`HTTP ${r.status}`);
        setServer(body);
      })
      .catch(e => setError(e.message));
  }, []);

  useEffect(() => {
    const hideLegacy = () => {
      document.querySelectorAll('body *').forEach(el => {
        if (!(el instanceof HTMLElement)) return;
        const t = el.textContent?.trim();
        if (!['Effective session', 'Security Session', 'CI/CD Pipeline'].includes(t)) return;
        let node = el;
        while (node && node !== document.body) {
          if (getComputedStyle(node).position === 'fixed') {
            node.style.setProperty('display', 'none', 'important');
            break;
          }
          node = node.parentElement;
        }
      });
    };
    hideLegacy();
    const observer = new MutationObserver(hideLegacy);
    observer.observe(document.body, { childList: true, subtree: true });
    return () => observer.disconnect();
  }, []);

  const signedInUser =
    authSession?.username || authSession?.email || 'Authenticated user';
  const role =
    authSession?.role || authSession?.roleName || 'Backend resolved';
  const permissionCount =
    authSession?.permissions?.length ?? authSession?.permissionCount ?? 'Backend resolved';

  const diagnostics = {
    signedInUser, role, permissionCount,
    clientIp: server?.network?.publicIp,
    deviceType: device.type,
    operatingSystem: device.os,
    browser: device.browser,
    timeZone: Intl.DateTimeFormat().resolvedOptions().timeZone,
    language: navigator.language,
    screen: `${screen.width} × ${screen.height}`,
    viewport: `${innerWidth} × ${innerHeight}`,
    apiRevision: server?.runtime?.apiRevision,
    sourceCommit: server?.runtime?.sourceCommit
  };

  const sections = [
    ['Effective session', [
      ['Signed-in user', signedInUser],
      ['Role', role],
      ['Permissions', permissionCount],
      ['View-As guard', authSession?.isViewAs ? 'Active' : 'Inactive'],
      ['Session token', 'Present — value hidden']]],
    ['Network', [
      ['Client IP', server?.network?.publicIp || 'Loading'],
      ['Forwarded address', server?.network?.forwardedForPresent ? 'Present' : 'Not present'],
      ['Protocol', server?.network?.protocol],
      ['Host', server?.network?.host]]],
    ['Device', [
      ['Device type', device.type],
      ['Operating system', device.os],
      ['Browser', device.browser],
      ['Touch support', navigator.maxTouchPoints > 0 ? 'Yes' : 'No'],
      ['CPU cores', navigator.hardwareConcurrency],
      ['Device memory', navigator.deviceMemory ? `${navigator.deviceMemory} GB` : 'Not exposed']]],
    ['Client environment', [
      ['Time zone', Intl.DateTimeFormat().resolvedOptions().timeZone],
      ['Language', navigator.language],
      ['Screen', `${screen.width} × ${screen.height}`],
      ['Viewport', `${innerWidth} × ${innerHeight}`],
      ['Theme', matchMedia('(prefers-color-scheme: dark)').matches ? 'Dark' : 'Light']]],
    ['Deployment traceability', [
      ['Environment', server?.runtime?.environment],
      ['API revision', server?.runtime?.apiRevision],
      ['API replica', server?.runtime?.apiReplica],
      ['Source commit', server?.runtime?.sourceCommit],
      ['SCM', 'GitHub'],
      ['Future runtime', 'OpenCloud-ready OCI model']]],
    ['Privacy boundaries', [
      ['Browser fingerprinting', 'No'],
      ['Token values returned', 'No'],
      ['Secrets returned', 'No'],
      ['Precise location', 'Not collected'],
      ['Write operations', 'None']]]
  ];

  return <>
    <button className="session-drawer-handle" onClick={() => setOpen(v => !v)}>
      {open ? 'Close session' : 'Session intelligence'}
    </button>

    <aside className={`session-drawer ${open ? 'open' : ''}`}>
      <header>
        <div><small>SECURITY SESSION</small><h2>Session Intelligence</h2></div>
        <button onClick={() => setOpen(false)}>Close</button>
      </header>

      {error ? <div className="session-drawer-warning">Server context: {error}</div> : null}

      <div className="session-drawer-body">
        {sections.map(([title, rows]) => (
          <details key={title} open={title === 'Effective session'}>
            <summary>{title}</summary>
            <dl>
              {rows.map(([label, entry]) => (
                <div key={label}><dt>{label}</dt><dd>{safe(entry)}</dd></div>
              ))}
            </dl>
          </details>
        ))}
      </div>

      <footer>
        <button onClick={() =>
          navigator.clipboard.writeText(JSON.stringify(diagnostics, null, 2))
        }>
          Copy safe diagnostics
        </button>
        <small>No secrets or token values are copied.</small>
      </footer>
    </aside>

    {open ? <button className="session-drawer-backdrop" onClick={() => setOpen(false)} /> : null}
  </>;
}
