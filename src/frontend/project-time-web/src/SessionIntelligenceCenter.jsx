import { useEffect, useMemo, useState } from 'react';
import './session-intelligence-center.css';

const value = (v, fallback = 'Not available') =>
  v === undefined || v === null || v === '' ? fallback : String(v);

function deviceInfo() {
  const ua = navigator.userAgent || '';
  const browser = ua.includes('Edg/') ? 'Microsoft Edge'
    : ua.includes('Firefox/') ? 'Mozilla Firefox'
    : ua.includes('Chrome/') ? 'Google Chrome'
    : ua.includes('Safari/') ? 'Apple Safari' : 'Unknown browser';
  const os = /Windows/i.test(ua) ? 'Windows'
    : /Mac OS X/i.test(ua) ? 'macOS'
    : /Android/i.test(ua) ? 'Android'
    : /iPhone|iPad/i.test(ua) ? 'iOS'
    : /Linux/i.test(ua) ? 'Linux' : 'Unknown OS';
  const type = /Mobi|Android|iPhone/i.test(ua) ? 'Mobile'
    : /iPad|Tablet/i.test(ua) ? 'Tablet' : 'Desktop / laptop';
  return { browser, os, type };
}

export default function SessionIntelligenceCenter({ authSession }) {
  const [server, setServer] = useState(null);
  const [error, setError] = useState('');
  const device = useMemo(deviceInfo, []);

  useEffect(() => {
    fetch('/api/security/session-intelligence')
      .then(async r => {
        const body = await r.json().catch(() => ({}));
        if (!r.ok) throw new Error(`HTTP ${r.status}`);
        setServer(body);
      })
      .catch(e => setError(e.message));
  }, []);

  const diagnostics = {
    signedInUser: authSession?.username || authSession?.email || 'Authenticated user',
    role: authSession?.role || authSession?.roleName || 'Backend resolved',
    publicIp: server?.network?.publicIp || 'Loading',
    deviceType: device.type,
    operatingSystem: device.os,
    browser: device.browser,
    timezone: Intl.DateTimeFormat().resolvedOptions().timeZone,
    language: navigator.language,
    screen: `${screen.width} × ${screen.height}`,
    viewport: `${innerWidth} × ${innerHeight}`,
    apiRevision: server?.runtime?.apiRevision || 'Loading',
    sourceCommit: server?.runtime?.sourceCommit || 'Loading'
  };

  const copy = () =>
    navigator.clipboard.writeText(JSON.stringify(diagnostics, null, 2));

  const sections = [
    ['Identity', [
      ['Signed-in user', diagnostics.signedInUser],
      ['Role', diagnostics.role],
      ['Session token', 'Present — value hidden'],
      ['View-As', authSession?.isViewAs ? 'Active' : 'Inactive']]],
    ['Device', [
      ['Device type', device.type],
      ['Operating system', device.os],
      ['Browser', device.browser],
      ['Touch support', navigator.maxTouchPoints > 0 ? 'Yes' : 'No'],
      ['CPU cores', value(navigator.hardwareConcurrency)],
      ['Device memory', navigator.deviceMemory ? `${navigator.deviceMemory} GB` : 'Not exposed']]],
    ['Network', [
      ['Client IP', value(server?.network?.publicIp, 'Loading')],
      ['Forwarded address', server?.network?.forwardedForPresent ? 'Present' : 'Not present'],
      ['Protocol', value(server?.network?.protocol)],
      ['Host', value(server?.network?.host)],
      ['VPN / proxy', 'Not asserted without trusted detection'],
      ['Location', 'Not collected']]],
    ['Client environment', [
      ['Time zone', diagnostics.timezone],
      ['Language', diagnostics.language],
      ['Screen', diagnostics.screen],
      ['Viewport', diagnostics.viewport],
      ['Color scheme', matchMedia('(prefers-color-scheme: dark)').matches ? 'Dark' : 'Light'],
      ['Online state', navigator.onLine ? 'Online' : 'Offline']]],
    ['Deployment traceability', [
      ['Environment', value(server?.runtime?.environment)],
      ['API revision', value(server?.runtime?.apiRevision, 'Loading')],
      ['API replica', value(server?.runtime?.apiReplica, 'Loading')],
      ['Source commit', value(server?.runtime?.sourceCommit, 'Loading')],
      ['SCM', 'GitHub'],
      ['Future runtime', 'OpenCloud-ready OCI model']]],
    ['Privacy boundaries', [
      ['Browser fingerprinting', 'No'],
      ['Token values returned', 'No'],
      ['Secrets returned', 'No'],
      ['Precise location', 'Not collected'],
      ['Write operations', 'None'],
      ['Diagnostics', 'Sanitized only']]]
  ];

  return (
    <div className="session-intelligence-center">
      <section className="session-intelligence-hero">
        <div>
          <p className="eyebrow">Module 059</p>
          <h1>Security Session Intelligence</h1>
          <p>Read-only identity, device, network, session, and deployed-runtime context.</p>
        </div>
        <div className="security-state"><strong>READ ONLY</strong><span>No secrets exposed</span></div>
      </section>

      {error ? <div className="session-warning">Server context: {error}</div> : null}

      <section className="session-grid">
        {sections.map(([title, rows]) => (
          <article key={title}>
            <p className="eyebrow">{title}</p>
            <dl>
              {rows.map(([label, entry]) => (
                <div key={label}><dt>{label}</dt><dd>{value(entry)}</dd></div>
              ))}
            </dl>
          </article>
        ))}
      </section>

      <section className="session-actions">
        <div><p className="eyebrow">Safe diagnostics</p><h2>Support-ready context</h2></div>
        <button onClick={copy}>Copy safe diagnostics</button>
      </section>
    </div>
  );
}
