import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import vm from 'node:vm';
import { fileURLToPath } from 'node:url';

const scriptDirectory = path.dirname(fileURLToPath(import.meta.url));
const frontendRoot = path.resolve(scriptDirectory, '..');
const transportPath = path.join(frontendRoot, 'src', 'projectpulse-authoritative-api.js');
const themePath = path.join(frontendRoot, 'src', 'admin-experience-theme.js');

class MemoryStorage {
  constructor() {
    this.values = new Map();
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
  const fetchCalls = [];
  const xhrInstances = [];

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

      // This models App.jsx's exact 050B global XHR bridge contract. It does not
      // read legacy storage keys, sessionStorage, or the session_token field.
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
    fetchCalls.push({ input, init });
    return new Response(JSON.stringify({ ok: true }), {
      status: 200,
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
  source += '\nglobalThis.__transportTest = { authoritativeApi, sessionContext };\n';

  vm.runInNewContext(source, sandbox, { filename: transportPath });

  return {
    sandbox,
    localStorage,
    sessionStorage,
    fetchCalls,
    xhrInstances,
    FakeXhr,
    authoritativeApi: sandbox.__transportTest.authoritativeApi
  };
}

function assertSingleSessionHeaders(xhr, token) {
  const expected = new Map([
    ['x-projectpulse-session', token],
    ['x-project-pulse-session', token],
    ['x-session-token', token],
    ['authorization', `Bearer ${token}`]
  ]);

  for (const [header, value] of expected) {
    assert.equal(xhr.headers.get(header)?.length, 1, `${header} must be sent exactly once`);
    assert.equal(xhr.headers.get(header)[0], value, `${header} should contain the expected token`);
  }
}

async function testTransportRuntime() {
  const harness = buildTransportHarness();
  const {
    sandbox,
    localStorage,
    sessionStorage,
    fetchCalls,
    xhrInstances,
    FakeXhr,
    authoritativeApi
  } = harness;

  const protectedResponse = await sandbox.fetch('/api/timesheet/timers/targets');
  assert.equal(protectedResponse.status, 425, 'pre-login protected fetch should stop locally');
  assert.equal(fetchCalls.length, 0, 'pre-login protected fetch must not reach the network');

  const loginResponse = await sandbox.fetch('/api/auth/login/route?username=admin%40ussignal.local');
  assert.equal(loginResponse.status, 200, 'public login route must remain available');
  assert.equal(fetchCalls.length, 1, 'public login route should reach the network once');

  const token = 'runtime-test-session-token';
  localStorage.setItem('projectPulseAuthSession', JSON.stringify({
    sessionToken: token,
    expiresAt: new Date(Date.now() + 60_000).toISOString()
  }));
  sandbox.dispatchEvent(new sandbox.CustomEvent('projectpulse:auth-session-ready'));

  await sandbox.fetch('/api/module-availability/overrides');
  assert.equal(fetchCalls.length, 2, 'authenticated protected fetch should reach the network');
  const protectedHeaders = new Headers(fetchCalls.at(-1).init.headers);
  assert.equal(protectedHeaders.get('X-ProjectPulse-Session'), token);
  assert.equal(protectedHeaders.get('X-Project-Pulse-Session'), token);
  assert.equal(protectedHeaders.get('X-Session-Token'), token);
  assert.equal(protectedHeaders.get('Authorization'), `Bearer ${token}`);

  FakeXhr.nextStatus = 200;
  FakeXhr.nextPayload = { targets: [] };
  await authoritativeApi('/api/timesheet/timers/targets', { requiredCollections: ['targets'] });
  assertSingleSessionHeaders(xhrInstances.at(-1), token);

  FakeXhr.nextStatus = 401;
  FakeXhr.nextPayload = { status: 'session_required', message: 'Session expired or invalid.' };
  await assert.rejects(
    authoritativeApi('/api/runtime/v2/role-policy/summary'),
    /Session expired or invalid\./
  );
  assert.equal(
    JSON.parse(localStorage.getItem('projectPulseAuthSession')).sessionToken,
    token,
    'a rejected request must not delete the working browser session'
  );

  // The App bridge cannot read session_token. Authoritative XHR must therefore
  // supply the headers directly even while the bridge is installed.
  const sessionTokenShape = 'session-token-shape-value';
  localStorage.setItem('projectPulseAuthSession', JSON.stringify({
    session_token: sessionTokenShape,
    expiresAt: new Date(Date.now() + 60_000).toISOString()
  }));
  FakeXhr.nextStatus = 200;
  FakeXhr.nextPayload = { targets: [] };
  await authoritativeApi('/api/timesheet/timers/targets', { requiredCollections: ['targets'] });
  assertSingleSessionHeaders(xhrInstances.at(-1), sessionTokenShape);

  // The same fallback must work for supported legacy sessionStorage keys when
  // the App bridge has no localStorage token to inject.
  localStorage.removeItem('projectPulseAuthSession');
  const legacyToken = 'legacy-session-storage-token';
  sessionStorage.setItem('projectPulseSession', JSON.stringify({
    session_token: legacyToken,
    expiresAt: new Date(Date.now() + 60_000).toISOString()
  }));
  await authoritativeApi('/api/timesheet/timers/targets', { requiredCollections: ['targets'] });
  assertSingleSessionHeaders(xhrInstances.at(-1), legacyToken);

  // If the App bridge would append an old local token while the authoritative
  // context selected a different valid fallback, stop locally instead of sending
  // a combined invalid header value.
  localStorage.setItem('projectPulseAuthSession', JSON.stringify({
    sessionToken: 'expired-bridge-token',
    expiresAt: new Date(Date.now() - 60_000).toISOString()
  }));
  sessionStorage.setItem('projectPulseSession', JSON.stringify({
    sessionToken: 'fresh-fallback-token',
    expiresAt: new Date(Date.now() + 60_000).toISOString()
  }));
  const xhrCountBeforeConflict = xhrInstances.length;
  await assert.rejects(
    authoritativeApi('/api/runtime/v2/role-policy/catalog'),
    (error) => error?.code === 'session_transport_conflict' && error?.silent === true
  );
  assert.equal(
    xhrInstances.length,
    xhrCountBeforeConflict,
    'a bridge-token conflict must stop before creating or sending an XHR'
  );

  console.log('AUTHORITATIVE_SESSION_RUNTIME=PASSED');
}

function buildThemeHarness() {
  class FakeNode {}
  FakeNode.TEXT_NODE = 3;

  class FakeTextNode extends FakeNode {
    constructor(text) {
      super();
      this.nodeType = FakeNode.TEXT_NODE;
      this.textContent = text;
      this.removed = false;
    }

    remove() {
      this.removed = true;
      this.parentNode?.removeChild(this);
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
      this.textContent = textContent;
      this.attributes = new Map();
      this.dataset = {};
      this.classList = new FakeClassList();
      this.parentNode = null;
      this.parentElement = null;
      this.previousSibling = null;
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
  }

  class FakeContainer extends FakeNode {
    constructor(childNodes = []) {
      super();
      this.childNodes = childNodes;
      this.parentNode = null;
      this.parentElement = null;
      for (const child of childNodes) {
        child.parentNode = this;
        if (child instanceof FakeButton) child.parentElement = this;
      }
    }

    removeChild(child) {
      this.childNodes = this.childNodes.filter((candidate) => candidate !== child);
    }
  }

  class FakeMutationObserver {
    constructor(callback) {
      this.callback = callback;
    }

    observe() {}
  }

  const stray = new FakeTextNode('\\n');
  const button = new FakeButton('🌙 Dark mode');
  button.classList.add('theme-toggle');
  const parent = new FakeContainer([stray, button]);
  button.previousSibling = stray;
  const body = new FakeContainer([parent]);
  parent.parentNode = body;
  parent.parentElement = body;

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
    querySelectorAll(selector) {
      return selector === 'button' ? [button] : [];
    },
    addEventListener() {}
  };

  const localStorage = new MemoryStorage();
  const sandbox = {
    console,
    Node: FakeNode,
    HTMLButtonElement: FakeButton,
    MutationObserver: FakeMutationObserver,
    document,
    localStorage,
    requestAnimationFrame(callback) {
      callback();
      return 1;
    },
    addEventListener() {},
    __projectPulseThemeControlPolishInstalled: false
  };
  sandbox.window = sandbox;
  sandbox.globalThis = sandbox;

  const source = fs.readFileSync(themePath, 'utf8');
  vm.runInNewContext(source, sandbox, { filename: themePath });

  return { stray, button, parent };
}

function testThemeRuntime() {
  const { stray, button, parent } = buildThemeHarness();

  assert.equal(stray.removed, true, 'literal \\n text node should be removed');
  assert.equal(parent.childNodes.includes(stray), false, 'stray theme text must leave the DOM');
  assert.equal(button.dataset.projectpulseThemeControl, 'true');
  assert.equal(button.dataset.projectpulseTheme, 'light');
  assert.equal(button.classList.contains('projectpulse-theme-control'), true);
  assert.equal(button.getAttribute('aria-label'), 'Switch to dark mode');
  assert.equal(button.getAttribute('aria-pressed'), 'false');

  console.log('THEME_CONTROL_RUNTIME=PASSED');
}

await testTransportRuntime();
testThemeRuntime();
console.log('AUTHORITATIVE_SESSION_THEME_RUNTIME_CONTRACT=PASSED');
