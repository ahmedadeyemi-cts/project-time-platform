const MARKER = '__projectPulseMailRuntimeActivationInstalled';
const DOCUMENT_PATH = '/api/native-administration/065/document';
const RUNTIME_PATH = '/api/microsoft-integration/mail-runtime';
const CONFIG_PREFIX = 'PROJECTPULSE_MICROSOFT_INTEGRATION_JSON:';

function extractConfiguration(body) {
  try {
    const request = JSON.parse(typeof body === 'string' ? body : '{}');
    const notes = request?.document?.configuration?.notes;
    if (typeof notes !== 'string' || !notes.startsWith(CONFIG_PREFIX)) return null;
    const stored = JSON.parse(notes.slice(CONFIG_PREFIX.length));
    const tenants = Array.isArray(stored?.tenants) ? stored.tenants : [];
    const tenant = tenants.find((item) => item?.key === stored?.activeTenantKey)
      || tenants.find((item) => item?.environmentMode === stored?.activeEnvironmentMode);
    if (!tenant) return null;
    const services = tenant.services || {};
    const mail = stored.mail || {};
    return {
      environmentMode: tenant.environmentMode,
      providerTarget: mail.providerTarget || 'microsoft_graph',
      tenantId: tenant.tenantId || '',
      clientId: services.clientId || tenant.clientId || '',
      smtpHost: mail.smtpHost || 'smtp.office365.com',
      smtpPort: Number(mail.smtpPort || 587),
      senderName: mail.senderName || '',
      senderAddress: mail.senderAddress || '',
      replyToAddress: mail.replyToAddress || '',
      recipientBoundary: mail.recipientBoundary || 'test_only'
    };
  } catch {
    return null;
  }
}

function sameOriginPath(input) {
  try {
    const url = new URL(typeof input === 'string' ? input : input?.url, window.location.origin);
    return url.origin === window.location.origin ? url.pathname : '';
  } catch {
    return '';
  }
}

function headersFor(input, init) {
  const headers = new Headers(input instanceof Request ? input.headers : undefined);
  new Headers(init?.headers || {}).forEach((value, name) => headers.set(name, value));
  headers.set('Content-Type', 'application/json');
  return headers;
}

if (typeof window !== 'undefined' && !window[MARKER]) {
  const previousFetch = window.fetch.bind(window);
  window.fetch = async (input, init = {}) => {
    const method = String(init?.method || (input instanceof Request ? input.method : 'GET')).toUpperCase();
    const configuration = method === 'PUT' && sameOriginPath(input) === DOCUMENT_PATH
      ? extractConfiguration(init?.body)
      : null;
    const response = await previousFetch(input, init);
    if (!configuration || !response.ok) return response;

    const applied = await previousFetch(RUNTIME_PATH, {
      method: 'PUT',
      cache: 'no-store',
      headers: headersFor(input, init),
      body: JSON.stringify(configuration)
    });
    if (applied.ok) return response;

    let payload = {};
    try { payload = await applied.json(); } catch { /* no-op */ }
    return new Response(JSON.stringify({
      status: 'mail_runtime_activation_failed',
      message: payload?.message || 'Mail settings were saved, but runtime activation failed.',
      persistedConfiguration: true,
      runtimeActivated: false
    }), {
      status: applied.status >= 400 ? applied.status : 502,
      headers: { 'Content-Type': 'application/json' }
    });
  };
  window[MARKER] = true;
}
