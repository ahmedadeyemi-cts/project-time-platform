const RAW_HTTP_ERROR_PATTERN = /(?:(\/api\/[\w\-./?=&%]+)\s+)?returned\s+HTTP\s+(\d{3})(?:\s*:\s*([\s\S]+))?/i;
const RAW_STATUS_ERROR_PATTERN = /\b(?:http|status|response|error)(?:\s+code)?\s*[:=]?\s*(\d{3})\b/i;
const RAW_PERMISSION_PATTERN = /\b(?:explicit\s+denial|access\s+denied|forbidden|not\s+authorized|permission\s+denied|not\s+available\s+for\s+this\s+role)\b/i;
const RAW_API_FAILURE_PATTERN = /\/api\/[\w\-./?=&%]+[\s\S]*\b(?:failed|failure|unavailable|could\s+not\s+be\s+verified|not\s+available|denied|timed\s+out)\b/i;
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
  const source = String(text || '');
  const httpMatch = source.match(/\bHTTP\s+(\d{3})\b/i);
  if (httpMatch) return Number(httpMatch[1]);

  const genericMatch = source.match(RAW_STATUS_ERROR_PATTERN);
  if (genericMatch) return Number(genericMatch[1]);

  if (RAW_PERMISSION_PATTERN.test(source)) return 403;
  if (/module\s+availability\s+could\s+not\s+be\s+verified/i.test(source)) return 503;
  return null;
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

  if ([502, 503, 504].includes(status)
      && /module availability could not be verified|access.*could not be verified/.test(normalizedDetail)) {
    return 'This information is temporarily unavailable while access is being verified. The rest of the page is still available.';
  }

  if ([502, 503, 504].includes(status)) {
    return 'A supporting service is temporarily unavailable. The rest of the page may still be available. Try again shortly.';
  }

  if (status >= 500) {
    return 'Something went wrong while processing your request. Try again shortly.';
  }

  return 'We could not complete the request. Check your connection and try again.';
}

function friendlyTitleFor(diagnostic) {
  if (diagnostic.status === 401) return 'Sign-in unsuccessful';
  if (diagnostic.status === 403) return 'Access is limited';
  if (diagnostic.status === 404) return 'Information not found';
  if (diagnostic.status === 409) return 'Refresh needed';
  if (diagnostic.status === 429) return 'Please wait';
  if (diagnostic.status === 501) return 'Feature unavailable';
  if ([502, 503, 504].includes(diagnostic.status)) return 'Service temporarily unavailable';
  if (diagnostic.status >= 500) return 'Something went wrong';
  if (diagnostic.status === 400 || diagnostic.status === 422) return 'Review required';
  return 'Request could not be completed';
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

function friendlyErrorItem(value, context = {}) {
  const { diagnostic, userMessage } = capture(value, context);
  return {
    message: userMessage,
    referenceId: shouldShowReference(diagnostic) ? diagnostic.referenceId : null,
    status: diagnostic.status,
    title: friendlyTitleFor(diagnostic)
  };
}

function isTechnicalErrorText(text) {
  const value = cleanTechnicalDetail(text);
  return RAW_HTTP_ERROR_PATTERN.test(value)
    || RAW_STATUS_ERROR_PATTERN.test(value)
    || RAW_API_FAILURE_PATTERN.test(value)
    || (/\/api\//i.test(value) && /\b(?:401|403|404|409|422|429|5\d\d)\b/.test(value))
    || RAW_PERMISSION_PATTERN.test(value);
}

function shouldSkipElement(element) {
  if (!(element instanceof HTMLElement)) return true;
  return Boolean(element.closest([
    '.audit-history-panel',
    'pre',
    'code',
    'script',
    'style',
    'textarea',
    'select',
    'option',
    'input',
    'button',
    '[contenteditable="true"]',
    '[data-projectpulse-technical-diagnostic]',
    '[data-projectpulse-error-policy-exempt]'
  ].join(', ')));
}

function isCompactErrorContainer(element) {
  return ['LI', 'TD', 'TH', 'DD', 'DT'].includes(element.tagName)
    || Boolean(element.closest('ul, ol, table, dl'));
}

function renderFriendlyError(element, diagnostic, userMessage) {
  const fingerprint = diagnostic.fingerprint;
  if (element.dataset.projectpulseFriendlyError === fingerprint) return;

  const compact = isCompactErrorContainer(element);
  element.dataset.projectpulseFriendlyError = fingerprint;
  element.classList.add('projectpulse-friendly-error');
  if (compact) element.classList.add('compact');
  element.setAttribute('role', 'alert');
  element.setAttribute('aria-live', 'polite');
  element.textContent = '';

  if (!compact) {
    const title = document.createElement('strong');
    title.className = 'projectpulse-friendly-error-title';
    title.textContent = friendlyTitleFor(diagnostic);
    element.append(title);
  }

  const message = document.createElement('span');
  message.className = 'projectpulse-friendly-error-message';
  message.textContent = userMessage;
  element.append(message);

  if (shouldShowReference(diagnostic)) {
    const reference = document.createElement('small');
    reference.className = 'projectpulse-friendly-error-reference';
    reference.textContent = `Reference: ${diagnostic.referenceId}`;
    element.append(reference);
  }
}

function presentTextNode(textNode) {
  if (!textNode || textNode.nodeType !== Node.TEXT_NODE) return;
  const element = textNode.parentElement;
  if (!element || shouldSkipElement(element)) return;

  const rawText = cleanTechnicalDetail(textNode.nodeValue);
  if (!isTechnicalErrorText(rawText)) return;

  const { diagnostic, userMessage } = capture(rawText, {
    contextLabel: isCompactErrorContainer(element)
      ? 'nested user-interface error detail'
      : 'visible user interface'
  });
  renderFriendlyError(element, diagnostic, userMessage);
}

function collectTechnicalTextNodes(root) {
  const start = root instanceof HTMLElement ? root : root?.parentElement || document.body;
  if (!start || shouldSkipElement(start)) return [];

  const nodes = [];
  const walker = document.createTreeWalker(start, NodeFilter.SHOW_TEXT, {
    acceptNode(node) {
      if (!node?.parentElement || shouldSkipElement(node.parentElement)) return NodeFilter.FILTER_REJECT;
      return isTechnicalErrorText(node.nodeValue) ? NodeFilter.FILTER_ACCEPT : NodeFilter.FILTER_SKIP;
    }
  });

  while (walker.nextNode()) nodes.push(walker.currentNode);
  return nodes;
}

function scan(root = document.body) {
  if (!root) return;
  collectTechnicalTextNodes(root).forEach(presentTextNode);
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
  friendlyErrorItem,
  friendlyMessageFor,
  friendlyTitleFor,
  diagnosticFrom,
  scan
});

install();

export {
  capture,
  diagnosticFrom,
  friendlyErrorItem,
  friendlyMessageFor,
  friendlyTitleFor
};
