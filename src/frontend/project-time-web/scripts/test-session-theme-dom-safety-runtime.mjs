import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import vm from 'node:vm';
import { fileURLToPath } from 'node:url';

const scriptDirectory = path.dirname(fileURLToPath(import.meta.url));
const frontendRoot = path.resolve(scriptDirectory, '..');
const transportPath = path.join(frontendRoot, 'src', 'projectpulse-authoritative-api.js');
const themePath = path.join(frontendRoot, 'src', 'admin-experience-theme.js');
const themeCssPath = path.join(frontendRoot, 'src', 'admin-experience-theme.css');
const ownershipPreludePath = path.join(frontendRoot, 'src', 'react-dom-ownership-prelude.js');

class MemoryStorage {
  constructor(initial = {}) {
    this.values = new Map(Object.entries(initial));
  }

  getItem(key) {
    return this.values.has(String(key)) ? this.values.get(String(key)) : null;
  }

  setItem(key, value) {
    this.values.set(String(key), String(value));
  }

  removeItem(key) {
    this.values.delete(String(key));
  }
}

function buildTransportHarness() {
  const listeners = new Map();
  const localStorage = new MemoryStorage();
  const sessionStorage = new MemoryStorage();
  const networkCalls = [];
  const xhrInstances = [];
  const nativeResponses = new Map();

  const queueNativeResponse = (requestPath, payload, status = 200) => {
    const queue = nativeResponses.get(requestPath) || [];
    queue.push({ payload, status });
    nativeResponses.set(requestPath, queue);
  };

  class FakeCustomEvent {
    constructor(type, init = {}) {
      this.type = type;
      this.detail = init.detail;
    }
  }

  class FakeXhr {
    static nextStatus = 200;
    static nextPayload = { targets: [] };

    constructor() {
      this.headers = new Map();
      this.status = 0;
      this.responseText = '';
      this.timeout = 0;
      xhrInstances.push(this);
    }

    open(method, url) {
      this.method = method;
      this.url = url;
    }

    setRequestHeader(name, value) {
      const key = String(name).toLowerCase();
      const existing = this.headers.get(key) || [];
      existing.push(String(value));
      this.headers.set(key, existing);
    }

    send() {
      const session = JSON.parse(localStorage.getItem('projectPulseAuthSession') || 'null');
      const token = session?.sessionToken || session?.token || session?.accessToken || '';
      if (token) {
        this.setRequestHeader('X-ProjectPulse-Session', token);
        this.setRequestHeader('X-Project-Pulse-Session', token);
        this.setRequestHeader('X-Session-Token', token);
        this.setRequestHeader('Authorization', `Bearer ${token}`);
      }
      this.status = FakeXhr.nextStatus;
      this.responseText = JSON.stringify(FakeXhr.nextPayload);
      queueMicrotask(() => this.onload?.());
    }
  }
  FakeXhr.prototype.__projectPulse050BFinalWrapped = true;

  const nativeFetch = async (input, init = {}) => {
    const raw = typeof input === 'string' ? input : input?.url;
    const url = new URL(raw, 'https://phd-west-test.onenecklab.com');
    const requestPath = `${url.pathname}${url.search}`;
    const queue = nativeResponses.get(requestPath) || [];
    const definition = queue.shift() || { payload: { ok: true }, status: 200 };
    nativeResponses.set(requestPath, queue);
    networkCalls.push({ input, init, requestPath, definition });
    return new Response(JSON.stringify(definition.payload), {
      status: definition.status,
      headers: { 'Content-Type': 'application/json' }
    });
  };

  const sandbox = {
    console,
    URL,
    Headers,
    Request,
    Response,
    XMLHttpRequest: FakeXhr,
    CustomEvent: FakeCustomEvent,
    setTimeout,
    clearTimeout,
    queueMicrotask,
    Date,
    JSON,
    Map,
    Set,
    WeakSet,
    Promise,
    Error,
    Number,
    String,
    Object,
    Array,
    Boolean,
    RegExp,
    Math,
    currentProjectPulseRoute: () => 'audit-history',
    moduleForRoute: () => ({ moduleNumber: '008' }),
    localStorage,
    sessionStorage,
    fetch: nativeFetch,
    location: { origin: 'https://phd-west-test.onenecklab.com', hash: '#audit-history' },
    addEventListener(type, handler) {
      const handlers = listeners.get(type) || new Set();
      handlers.add(handler);
      listeners.set(type, handlers);
    },
    removeEventListener(type, handler) {
      listeners.get(type)?.delete(handler);
    },
    dispatchEvent(event) {
      for (const handler of listeners.get(event.type) || []) handler(event);
      return true;
    }
  };
  sandbox.window = sandbox;
  sandbox.globalThis = sandbox;

  let source = fs.readFileSync(transportPath, 'utf8');
  source = source.replace(/^import .*?;\s*/s, '');
  source = source.replace('export function authoritativeApiDiagnostics', 'function authoritativeApiDiagnostics');
  source = source.replace('export async function authoritativeApi', 'async function authoritativeApi');
  source += '\nglobalThis.__transportTest = { authoritativeApi, authoritativeApiDiagnostics };\n';
  vm.runInNewContext(source, sandbox, { filename: transportPath });

  return {
    sandbox,
    localStorage,
    sessionStorage,
    networkCalls,
    xhrInstances,
    FakeXhr,
    queueNativeResponse,
    authoritativeApi: sandbox.__transportTest.authoritativeApi,
    diagnostics: sandbox.__transportTest.authoritativeApiDiagnostics
  };
}

function assertSingleHeaders(xhr, token) {
  const expected = new Map([
    ['x-projectpulse-session', token],
    ['x-project-pulse-session', token],
    ['x-session-token', token],
    ['authorization', `Bearer ${token}`]
  ]);
  for (const [name, value] of expected) {
    assert.equal(xhr.headers.get(name)?.length, 1, `${name} must be sent exactly once`);
    assert.equal(xhr.headers.get(name)?.[0], value, `${name} must contain the selected session`);
  }
}

function assertFetchHeaders(call, token) {
  const headers = new Headers(call.init.headers);
  assert.equal(headers.get('X-ProjectPulse-Session'), token);
  assert.equal(headers.get('X-Project-Pulse-Session'), token);
  assert.equal(headers.get('X-Session-Token'), token);
  assert.equal(headers.get('Authorization'), `Bearer ${token}`);
}

async function testSessionTransport() {
  const harness = buildTransportHarness();
  const {
    sandbox,
    localStorage,
    sessionStorage,
    networkCalls,
    xhrInstances,
    FakeXhr,
    queueNativeResponse,
    authoritativeApi,
    diagnostics
  } = harness;

  const preLogin = await sandbox.fetch('/api/runtime/v2/role-policy/summary');
  assert.equal(preLogin.status, 425);
  assert.equal(networkCalls.length, 0, 'protected pre-login request must not reach the network');

  const publicLogin = await sandbox.fetch('/api/auth/login/route?username=admin%40ussignal.local');
  assert.equal(publicLogin.status, 200);
  assert.equal(networkCalls.length, 1, 'public authentication request remains reachable');

  const token = 'runtime-test-session-token';
  localStorage.setItem('projectPulseAuthSession', JSON.stringify({
    sessionToken: token,
    expiresAt: new Date(Date.now() + 60_000).toISOString()
  }));
  sandbox.dispatchEvent(new sandbox.CustomEvent('projectpulse:auth-session-ready'));

  await sandbox.fetch('/api/module-availability/overrides');
  assert.equal(networkCalls.length, 2);
  assertFetchHeaders(networkCalls.at(-1), token);

  FakeXhr.nextStatus = 200;
  FakeXhr.nextPayload = { targets: [] };
  await authoritativeApi('/api/timesheet/timers/targets', { requiredCollections: ['targets'] });
  assertSingleHeaders(xhrInstances.at(-1), token);

  FakeXhr.nextStatus = 200;
  FakeXhr.nextPayload = {};
  queueNativeResponse('/api/runtime/v2/role-policy/summary', {
    roles: [{ roleCode: 'SUPER_ADMINISTRATOR' }],
    modules: [{ moduleCode: '008' }]
  });
  const recovered = await authoritativeApi('/api/runtime/v2/role-policy/summary', {
    requiredCollections: ['roles', 'modules']
  });
  assert.equal(recovered.roles.length, 1);
  assert.equal(recovered.modules.length, 1);
  assertFetchHeaders(networkCalls.at(-1), token);
  assert.equal(diagnostics()['/api/runtime/v2/role-policy/summary']?.transport, 'native-fetch-fallback');

  FakeXhr.nextStatus = 401;
  FakeXhr.nextPayload = { status: 'session_required', message: 'Session expired or invalid.' };
  await assert.rejects(
    authoritativeApi('/api/runtime/v2/role-policy/summary'),
    /Session expired or invalid\./
  );
  assert.equal(JSON.parse(localStorage.getItem('projectPulseAuthSession')).sessionToken, token);

  const sessionTokenShape = 'session-token-shape-value';
  localStorage.setItem('projectPulseAuthSession', JSON.stringify({
    session_token: sessionTokenShape,
    expiresAt: new Date(Date.now() + 60_000).toISOString()
  }));
  FakeXhr.nextStatus = 200;
  FakeXhr.nextPayload = { targets: [] };
  await authoritativeApi('/api/timesheet/timers/targets', { requiredCollections: ['targets'] });
  assertSingleHeaders(xhrInstances.at(-1), sessionTokenShape);

  localStorage.removeItem('projectPulseAuthSession');
  const legacyToken = 'legacy-session-storage-token';
  sessionStorage.setItem('projectPulseSession', JSON.stringify({
    session_token: legacyToken,
    expiresAt: new Date(Date.now() + 60_000).toISOString()
  }));
  await authoritativeApi('/api/timesheet/timers/targets', { requiredCollections: ['targets'] });
  assertSingleHeaders(xhrInstances.at(-1), legacyToken);

  localStorage.setItem('projectPulseAuthSession', JSON.stringify({
    sessionToken: 'expired-bridge-token',
    expiresAt: new Date(Date.now() - 60_000).toISOString()
  }));
  sessionStorage.setItem('projectPulseSession', JSON.stringify({
    sessionToken: 'fresh-fallback-token',
    expiresAt: new Date(Date.now() + 60_000).toISOString()
  }));
  const beforeConflict = xhrInstances.length;
  await assert.rejects(
    authoritativeApi('/api/runtime/v2/role-policy/catalog'),
    (error) => error?.code === 'session_transport_conflict' && error?.silent === true
  );
  assert.equal(xhrInstances.length, beforeConflict, 'conflicting bridge state must fail before XHR creation');

  console.log('SAFE_SESSION_TRANSPORT_RUNTIME=PASSED');
}

function buildThemeHarness() {
  const documentListeners = new Map();
  const windowListeners = new Map();

  class FakeNode {}
  FakeNode.TEXT_NODE = 3;

  class FakeTextNode extends FakeNode {
    constructor(text) {
      super();
      this.nodeType = FakeNode.TEXT_NODE;
      this.textContent = text;
      this.parentNode = null;
    }
  }

  class FakeClassList {
    constructor() {
      this.values = new Set();
    }

    add(...values) {
      values.forEach((value) => this.values.add(value));
    }

    contains(value) {
      return this.values.has(value);
    }
  }

  class FakeButton extends FakeNode {
    constructor(textContent) {
      super();
      this.id = 'projectpulse-floating-theme-toggle';
      this.textContent = textContent;
      this.attributes = new Map();
      this.dataset = {};
      this.classList = new FakeClassList();
      this.parentNode = null;
      this.parentElement = null;
      this.type = '';
    }

    getAttribute(name) {
      return this.attributes.get(name) || null;
    }

    setAttribute(name, value) {
      this.attributes.set(name, String(value));
    }

    matches(selector) {
      return selector.includes('.theme-toggle') && this.classList.contains('theme-toggle');
    }

    closest(selector) {
      return selector === 'button' ? this : null;
    }
  }

  class FakeBody extends FakeNode {
    constructor(childNodes = []) {
      super();
      this.childNodes = childNodes;
      this.dataset = { theme: 'light' };
      for (const child of childNodes) {
        child.parentNode = this;
        if (child instanceof FakeButton) child.parentElement = this;
      }
    }
  }

  const stray = new FakeTextNode('\\n');
  const button = new FakeButton('🌙 Dark mode');
  button.classList.add('theme-toggle');
  const body = new FakeBody([stray, button]);
  const documentElement = { dataset: { theme: 'light' } };

  const document = {
    readyState: 'complete',
    body,
    documentElement,
    querySelector(selector) {
      if (selector === '[data-projectpulse-theme-control="true"]') {
        return button.dataset.projectpulseThemeControl === 'true' ? button : null;
      }
      return null;
    },
    getElementById(id) {
      return id === button.id ? button : null;
    },
    querySelectorAll(selector) {
      return selector === 'button' ? [button] : [];
    },
    addEventListener(type, handler) {
      const handlers = documentListeners.get(type) || [];
      handlers.push(handler);
      documentListeners.set(type, handlers);
    }
  };

  const localStorage = new MemoryStorage({ 'ptp-theme': 'light' });
  class FakeCustomEvent {
    constructor(type, init = {}) {
      this.type = type;
      this.detail = init.detail;
    }
  }

  const sandbox = {
    console,
    Node: FakeNode,
    HTMLButtonElement: FakeButton,
    document,
    localStorage,
    CustomEvent: FakeCustomEvent,
    location: { reload: () => { throw new Error('Theme must not reload the application.'); } },
    requestAnimationFrame(callback) {
      callback();
      return 1;
    },
    setTimeout(callback) {
      callback();
      return 1;
    },
    clearTimeout() {},
    addEventListener(type, handler) {
      const handlers = windowListeners.get(type) || [];
      handlers.push(handler);
      windowListeners.set(type, handlers);
    },
    dispatchEvent(event) {
      for (const handler of windowListeners.get(event.type) || []) handler(event);
      return true;
    }
  };
  sandbox.window = sandbox;
  sandbox.globalThis = sandbox;

  const source = fs.readFileSync(themePath, 'utf8');
  vm.runInNewContext(source, sandbox, { filename: themePath });

  return { stray, button, body, documentElement, localStorage, documentListeners };
}

function testThemeAndOwnershipRuntime() {
  const { stray, button, body, documentElement, localStorage, documentListeners } = buildThemeHarness();
  const themeCss = fs.readFileSync(themeCssPath, 'utf8');
  const ownershipPrelude = fs.readFileSync(ownershipPreludePath, 'utf8');

  assert.equal(stray.textContent, '', 'literal newline text must be neutralized');
  assert.equal(body.childNodes.includes(stray), true, 'safe theme cleanup must not remove body nodes');
  assert.equal(button.textContent, '', 'the visible theme control must be icon-only');
  assert.equal(button.dataset.projectpulseThemeControl, 'true');
  assert.equal(button.dataset.projectpulseTheme, 'light');
  assert.equal(button.classList.contains('projectpulse-theme-control'), true);
  assert.equal(button.getAttribute('aria-label'), 'Switch to dark mode');
  assert.equal(button.getAttribute('aria-pressed'), 'false');

  const clickHandler = documentListeners.get('click')?.[0];
  assert.equal(typeof clickHandler, 'function', 'theme click boundary must be installed');
  clickHandler({
    target: button,
    preventDefault() {},
    stopImmediatePropagation() {}
  });
  assert.equal(localStorage.getItem('ptp-theme'), 'dark');
  assert.equal(documentElement.dataset.theme, 'dark');
  assert.equal(body.dataset.theme, 'dark');
  assert.equal(button.dataset.projectpulseTheme, 'dark');
  assert.equal(button.getAttribute('aria-label'), 'Switch to light mode');
  assert.equal(button.getAttribute('aria-pressed'), 'true');

  assert.match(themeCss, /left:\s*0\s*!important/);
  assert.match(themeCss, /width:\s*44px\s*!important/);
  assert.match(themeCss, /border-radius:\s*0 14px 14px 0\s*!important/);
  assert.match(themeCss, /content:\s*'☾'/);
  assert.match(themeCss, /content:\s*'☀'/);
  assert.doesNotMatch(themeCss, /content:\s*'Dark mode'/);
  assert.doesNotMatch(themeCss, /content:\s*'Light mode'/);

  assert.match(ownershipPrelude, /__projectPulseGlobalViewAsTopbarMountInstalled\s*=\s*true/);
  assert.match(ownershipPrelude, /view-as-body-owned/);
  assert.doesNotMatch(ownershipPrelude, /insertBefore|appendChild|removeChild|MutationObserver/);

  console.log('SAFE_THEME_AND_DOM_OWNERSHIP_RUNTIME=PASSED');
}

await testSessionTransport();
testThemeAndOwnershipRuntime();
console.log('SESSION_THEME_DOM_SAFETY_RUNTIME_CONTRACT=PASSED');
