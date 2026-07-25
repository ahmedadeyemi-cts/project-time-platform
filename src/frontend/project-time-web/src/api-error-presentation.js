const RAW_HTTP_ERROR_PATTERN = /(?:(\/api\/[\w\-./?=&%]+)\s+)?returned\s+HTTP\s+(\d{3})(?:\s*:\s*([\s\S]+))?/i;
const RAW_PERMISSION_PATTERN = /\b(?:explicit\s+denial|access\s+denied|forbidden|not\s+authorized|permission\s+denied)\b/i;
const DIAGNOSTIC_ENDPOINT = '/api/client-diagnostics';
const MAX_AUDIT_EVENTS_PER_SESSION = 20;

const referenceByFingerprint = new Map();
const loggedFingerprints = new Set();
const auditedFingerprints = new Set();
let auditEventCount = 0;

function activeRoute() {
  return String(window.location.hash || '#dashboard').replace(/^#/, '').split('?')[0] || 'dashboard';
}

function currentSessionToken() {
  try {
    const session = JSON.parse(window.localStorage.getItem('projectPulseAuthSession') || 'null');
    return session?.sessionToken || session?.token || session?.accessToken || '';
  } catch {
    return '';
  }
}

function isViewAsActive() {
  try {
    return Boolean(JSON.parse(window.localStorage.getItem('projectPulseViewAsUser') || 'null')?.userId);
  } catch {
    return false;
  }
}

function cleanTechnicalDetail(value) {
  return String(value || '')
    .replace(/[\r\n\t]+/g, ' ')
    .replace(/\s+/g, ' ')
    .trim();
}

function parseStatusFromText(text) {
  const match = String(text || '').match(/\bHTTP\s+(\d{3})\b/i);
  return match ? Number(match[1]) : null;
}

function extractTechnicalCode(detail) {
  const source = cleanTechnicalDetail(detail);
  const statusMatch = source.match(/\b(?:status|code)\s*[=:]\s*["']?([a-z0-9_\-]{3,80})/i);
  if (statusMatch) return statusMatch[1].toUpperCase();

  const permissionMatch = source.match(/\b([A-Z][A-Z0-9_]{3,80})\b/g) || [];
  const ignored = new Set(['HTTP', 'API', 'JSON', 'HTML', 'SQL', 'UTC']);
  return permissionMatch.find((value) => !ignored.has(value)) || '';
}

function makeReferenceId(fingerprint) {
  if (referenceByFingerprint.has(fingerprint)) return referenceByFingerprint.get(fingerprint);

  let token = '';
  try {
    token = crypto.randomUUID().replaceAll('-', '').slice(0, 8).toUpperCase();
  } catch {
    token = Math.random().toString(36).slice(2, 10).toUpperCase();
  }

  const referenceId = `PP-${token}`;
  referenceByFingerprint.set(fingerprint, referenceId);
  return referenceId;
}

function diagnosticFrom(value, context = {}) {
  const rawMessage = cleanTechnicalDetail(value instanceof Error ? value.message : value);
  const httpMatch = rawMessage.match(RAW_HTTP_ERROR_PATTERN);
  const path = context.path || httpMatch?.[1] || rawMessage.match(/\/api\/[\w\-./?=&%]+/)?.[0] || '';
  const status = Number(context.status || httpMatch?.[2] || value?.status || parseStatusFromText(rawMessage) || 0);
  const detail = cleanTechnicalDetail(context.detail || httpMatch?.[3] || rawMessage);
  const technicalCode = context.technicalCode || extractTechnicalCode(detail);
  const category = status === 401
    ? 'authentication'
    : status === 403
      ? 'authorization'
      : status === 409
        ? 'conflict'
        : status === 429
          ? 'rate_limit'
          : status >= 500
            ? 'service_failure'
            : status >= 400
              ? 'request_validation'
              : 'network_failure';
  const fingerprint = [path, status, technicalCode, detail, activeRoute()].join('|');

  return {
    rawMessage,
    path,
    status,
    detail,
    technicalCode,
    category,
    activeRoute: activeRoute(),
    referenceId: makeReferenceId(fingerprint),
    fingerprint,
    optional: Boolean(context.optional),
    contextLabel: cleanTechnicalDetail(context.contextLabel || '')
  };
}

function detailIsSafeForUsers(detail) {
  const value = cleanTechnicalDetail(detail);
  if (!value || value.length > 220) return false;
  if (/\/api\//i.test(value) || /\bHTTP\s+\d{3}\b/i.test(value)) return false;
  if (/\b[A-Z][A-Z0-9_]{4,}\b/.test(value)) return false;
  if (/stack|exception|postgres|sqlstate|connection string|bearer|token|password hash/i.test(value)) return false;
  return true;
}

function friendlyMessageFor(diagnostic) {
  const { status, path, detail } = diagnostic;
  const normalizedPath = String(path || '').toLowerCase();
  const normalizedDetail = String(detail || '').toLowerCase();

  if (status === 401 && normalizedPath.includes('/api/auth/local/login')) {
    return "We couldn't verify those sign-in details. Check the account and password, then try again.";
  }

  if (status === 401) {
    return 'Your session has expired or could not be verified. Sign in again to continue.';
  }

  if (status === 403 && normalizedPath.includes('/api/utilization')) {
    return "You don't have access to utilization information with your current role.";
  }

  if (status === 403 && normalizedPath.includes('/api/module-availability')) {
    return 'Only a Super Administrator can change module availability.';
  }

  if (status === 403) {
    return "You don't have permission to complete this action with your current role.";
  }

  if (status === 404) {
    return "We couldn't find the requested information. It may have been moved or removed.";
  }

  if (status === 409 && /timer.*already|already.*timer|timer_already_running/.test(normalizedDetail)) {
    return 'A timer is already running. Stop or discard it before starting another timer.';
  }

  if (status === 409) {
    return 'This information changed while you were working. Refresh the page and try again.';
  }

  if (status === 400 || status === 422) {
    return detailIsSafeForUsers(detail)
      ? cleanTechnicalDetail(detail)
      : 'Some information needs attention. Review your entries and try again.';
  }

  if (status === 429) {
    return 'Too many requests were made in a short period. Wait a moment and try again.';
  }

  if (status === 501) {
    return 'This feature is not available yet.';
  }

  if ([502, 503, 504].includes(status)) {
    return 'This service is temporarily unavailable. Try again shortly.';
  }

  if (status >= 500) {
    return 'Something went wrong while processing your request. Try again shortly.';
  }

  return 'We could not complete the request. Check your connection and try again.';
}

function shouldShowReference(diagnostic) {
  return diagnostic.status === 403
    || diagnostic.status === 409
    || diagnostic.status === 429
    || diagnostic.status >= 500;
}

function logDiagnostic(diagnostic) {
  if (loggedFingerprints.has(diagnostic.fingerprint)) return;
  loggedFingerprints.add(diagnostic.fingerprint);

  const label = `[ProjectPulse API diagnostic] ${diagnostic.category} · ${diagnostic.referenceId}`;
  console.groupCollapsed(label);
  console.error(diagnostic.rawMessage || diagnostic.detail || 'API request failed');
  console.table({
    referenceId: diagnostic.referenceId,
    endpoint: diagnostic.path || 'unknown',
    status: diagnostic.status || 'network',
    technicalCode: diagnostic.technicalCode || 'not supplied',
    route: diagnostic.activeRoute,
    context: diagnostic.contextLabel || 'not supplied',
    optionalRequest: diagnostic.optional
  });
  console.groupEnd();
}

function shouldAudit(diagnostic) {
  if (!currentSessionToken() || isViewAsActive()) return false;
  if (diagnostic.path === DIAGNOSTIC_ENDPOINT || diagnostic.path.startsWith('/api/audit/')) return false;
  return diagnostic.status === 403
    || diagnostic.status === 409
    || diagnostic.status === 429
    || diagnostic.status >= 500;
}

async function auditDiagnostic(diagnostic, userMessage) {
  if (!shouldAudit(diagnostic)) return;
  if (auditedFingerprints.has(diagnostic.fingerprint)) return;
  if (auditEventCount >= MAX_AUDIT_EVENTS_PER_SESSION) return;

  auditedFingerprints.add(diagnostic.fingerprint);
  auditEventCount += 1;

  const headers = {
    'Content-Type': 'application/json',
    'X-ProjectPulse-Session': currentSessionToken(),
    'X-ProjectPulse-Diagnostic': 'friendly-error-presentation'
  };

  try {
    await fetch(DIAGNOSTIC_ENDPOINT, {
      method: 'POST',
      headers,
      credentials: 'include',
      keepalive: true,
      body: JSON.stringify({
        referenceId: diagnostic.referenceId,
        category: diagnostic.category,
        statusCode: diagnostic.status || 0,
        endpointPath: diagnostic.path || 'unknown',
        technicalCode: diagnostic.technicalCode || null,
        userMessage,
        activeRoute: diagnostic.activeRoute,
        occurredAt: new Date().toISOString()
      })
    });
  } catch (error) {
    console.debug('[ProjectPulse API diagnostic] Audit write was unavailable.', error);
  }
}

function capture(value, context = {}) {
  const diagnostic = diagnosticFrom(value, context);
  const userMessage = friendlyMessageFor(diagnostic);
  logDiagnostic(diagnostic);
  void auditDiagnostic(diagnostic, userMessage);
  return { diagnostic, userMessage };
}

function isTechnicalErrorText(text) {
  const value = cleanTechnicalDetail(text);
  return RAW_HTTP_ERROR_PATTERN.test(value)
    || (/\/api\//i.test(value) && /\b(?:401|403|409|429|5\d\d)\b/.test(value))
    || RAW_PERMISSION_PATTERN.test(value);
}

function shouldSkipElement(element) {
  if (!(element instanceof HTMLElement)) return true;
  if (element.closest('.audit-history-panel, pre, code, script, style, textarea, select, option, [data-projectpulse-technical-diagnostic]')) return true;
  if (element.matches('input, textarea, select, option, button')) return true;
  return false;
}

function presentElement(element) {
  if (shouldSkipElement(element)) return;
  const rawText = cleanTechnicalDetail(element.textContent);
  if (!isTechnicalErrorText(rawText)) return;

  const { diagnostic, userMessage } = capture(rawText, { contextLabel: 'visible user interface' });
  const fingerprint = diagnostic.fingerprint;
  if (element.dataset.projectpulseFriendlyError === fingerprint) return;

  element.dataset.projectpulseFriendlyError = fingerprint;
  element.classList.add('projectpulse-friendly-error');
  element.setAttribute('role', 'alert');
  element.setAttribute('aria-live', 'polite');
  element.textContent = '';

  const title = document.createElement('strong');
  title.className = 'projectpulse-friendly-error-title';
  title.textContent = diagnostic.status === 403 ? 'Access is limited' : 'Request could not be completed';

  const message = document.createElement('span');
  message.className = 'projectpulse-friendly-error-message';
  message.textContent = userMessage;

  element.append(title, message);

  if (shouldShowReference(diagnostic)) {
    const reference = document.createElement('small');
    reference.className = 'projectpulse-friendly-error-reference';
    reference.textContent = `Reference: ${diagnostic.referenceId}`;
    element.append(reference);
  }
}

function scan(root = document.body) {
  if (!root) return;

  const elements = [];
  if (root instanceof HTMLElement) elements.push(root);
  if (root.querySelectorAll) {
    root.querySelectorAll('.error-text, .auth-status, .manager-empty-state.error, [role="alert"], [aria-live="assertive"], [aria-live="polite"], p, span, div, small').forEach((element) => elements.push(element));
  }

  elements.forEach((element) => {
    if (element.children.length > 0 && !element.classList.contains('error-text') && !element.classList.contains('auth-status')) return;
    presentElement(element);
  });
}

function install() {
  if (window.__projectPulseFriendlyErrorPresentationInstalled) return;
  window.__projectPulseFriendlyErrorPresentationInstalled = true;

  const queuedRoots = new Set();
  let scheduled = false;
  const flush = () => {
    scheduled = false;
    const roots = [...queuedRoots];
    queuedRoots.clear();
    roots.forEach((root) => scan(root));
  };
  const queue = (root) => {
    queuedRoots.add(root || document.body);
    if (scheduled) return;
    scheduled = true;
    window.setTimeout(flush, 40);
  };

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', () => queue(document.body), { once: true });
  } else {
    queue(document.body);
  }

  const observer = new MutationObserver((mutations) => {
    mutations.forEach((mutation) => {
      if (mutation.type === 'characterData') queue(mutation.target.parentElement);
      mutation.addedNodes.forEach((node) => queue(node instanceof HTMLElement ? node : node.parentElement));
    });
  });

  const startObserver = () => {
    if (!document.body) return;
    observer.observe(document.body, { childList: true, subtree: true, characterData: true });
  };

  if (document.body) startObserver();
  else document.addEventListener('DOMContentLoaded', startObserver, { once: true });

  window.addEventListener('hashchange', () => queue(document.body));
  window.addEventListener('pageshow', () => queue(document.body));
}

window.ProjectPulseErrorPresentation = Object.freeze({
  capture,
  friendlyMessageFor,
  diagnosticFrom,
  scan
});

install();

export { capture, diagnosticFrom, friendlyMessageFor };
