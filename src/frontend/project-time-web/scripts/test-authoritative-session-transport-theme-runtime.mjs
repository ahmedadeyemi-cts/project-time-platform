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
    const raw = typeof input === 'string' ? input : input?.url;
    const url = new URL(raw, 'https://phd-west-test.onenecklab.com');
    const requestPath = `${url.pathname}${url.search}`;
    const queue = nativeResponses.get(requestPath) || [];
    const responseDefinition = queue.shift() || { payload: { ok: true }, status: 200 };
    nativeResponses.set(requestPath, queue);
    fetchCalls.push({ input, init, requestPath, responseDefinition });
    return new Response(JSON.stringify(responseDefinition.payload), {
      status: responseDefinition.status,
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
  source += '\nglobalThis.__transportTest = { authoritativeApi, authoritativeApiDiagnostics, sessionContext };\n';

  vm.runInNewContext(source, sandbox, { filename: transportPath });

  return {
    sandbox,
    localStorage,
    sessionStorage,
    fetchCalls,
    xhrInstances,
    FakeXhr,
    queueNativeResponse,
    authoritativeApi: sandbox.__transportTest.authoritativeApi,
    diagnostics: sandbox.__transportTest.authoritativeApiDiagnostics
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

function assertFetchSessionHeaders(call, token) {
  const headers = new Headers(call.init.headers);
  assert.equal(headers.get('X-ProjectPulse-Session'), token);
  assert.equal(headers.get('X-Project-Pulse-Session'), token);
  assert.equal(headers.get('X-Session-Token'), token);
  assert.equal(headers.get('Authorization'), `Bearer ${token}`);
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
    queueNativeResponse,
    authoritativeApi,
    diagnostics
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
  assertFetchSessionHeaders(fetchCalls.at(-1), token);

  FakeXhr.nextStatus = 200;
  FakeXhr.nextPayload = { targets: [] };
  await authoritativeApi('/api/timesheet/timers/targets', { requiredCollections: ['targets'] });
  assertSingleSessionHeaders(xhrInstances.at(-1), token);

  // Reproduce the live browser defect: XHR reports HTTP 200 but exposes an empty
  // JSON object. The clean native fetch captured before wrappers must recover the
  // authoritative collections without logging a false failure.
  FakeXhr.nextStatus = 200;
  FakeXhr.nextPayload = {};
  queueNativeResponse('/api/runtime/v2/role-policy/summary', {
    roles: [{ roleCode: 'SUPER_ADMINISTRATOR' }],
    modules: [{ moduleCode: '008' }],
    status: 'authoritative_role_policy_summary_loaded'
  });
  const recoveredSummary = await authoritativeApi('/api/runtime/v2/role-policy/summary', {
    requiredCollections: ['roles', 'modules']
  });
  assert.equal(recoveredSummary.roles.length, 1);
  assert.equal(recoveredSummary.modules.length, 1);
  assert.equal(fetchCalls.at(-1).requestPath, '/api/runtime/v2/role-policy/summary');
  assertFetchSessionHeaders(fetchCalls.at(-1), token);
  assert.equal(
    diagnostics()['/api/runtime/v2/role-policy/summary']?.transport,
    'native-fetch-fallback'
  );

  // A direct-array response is valid for one required collection and must be
  // normalized rather than discarded as an empty object.
  FakeXhr.nextPayload = {};
  queueNativeResponse('/api/runtime/v2/role-policy/versions', [
    { versionNumber: 1, policyStatus: 'PUBLISHED' }
  ]);
  const recoveredVersions = await authoritativeApi('/api/runtime/v2/role-policy/versions', {
    requiredCollections: ['versions']
  });
  assert.equal(recoveredVersions.versions.length, 1);

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

  // The same direct header fallback must work for supported legacy sessionStorage
  // keys when the App bridge has no localStorage token to inject.
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
  const themeCss = fs.readFileSync(themeCssPath, 'utf8');

  assert.equal(stray.removed, true, 'literal \\n text node should be removed');
  assert.equal(parent.childNodes.includes(stray), false, 'stray theme text must leave the DOM');
  assert.equal(button.dataset.projectpulseThemeControl, 'true');
  assert.equal(button.dataset.projectpulseTheme, 'light');
  assert.equal(button.classList.contains('projectpulse-theme-control'), true);
  assert.equal(button.getAttribute('aria-label'), 'Switch to dark mode');
  assert.equal(button.getAttribute('aria-pressed'), 'false');

  assert.match(themeCss, /left:\s*0\s*!important/);
  assert.match(themeCss, /width:\s*44px\s*!important/);
  assert.match(themeCss, /border-radius:\s*0 14px 14px 0\s*!important/);
  assert.match(themeCss, /::after\s*\{[\s\S]*display:\s*none\s*!important/);
  assert.doesNotMatch(themeCss, /content:\s*'Dark mode'/);
  assert.doesNotMatch(themeCss, /content:\s*'Light mode'/);

  console.log('THEME_CONTROL_RUNTIME=PASSED');
}

await testTransportRuntime();
testThemeRuntime();
console.log('AUTHORITATIVE_SESSION_THEME_RUNTIME_CONTRACT=PASSED');
