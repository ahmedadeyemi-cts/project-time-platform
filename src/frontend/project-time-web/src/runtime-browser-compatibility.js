const LOCAL_HOSTS = new Set(['localhost', '127.0.0.1', '::1']);

function shouldUpgradeToHttps() {
  const location = globalThis.location;
  if (!location || location.protocol !== 'http:') return false;
  if (LOCAL_HOSTS.has(location.hostname)) return false;
  return location.hostname === 'pulse.onenecklab.com'
    || location.hostname.endsWith('.onenecklab.com');
}

function upgradeToHttps() {
  if (!shouldUpgradeToHttps()) return false;

  const secureUrl = new URL(globalThis.location.href);
  secureUrl.protocol = 'https:';
  if (secureUrl.port === '80') secureUrl.port = '';
  globalThis.location.replace(secureUrl.toString());
  return true;
}

function uuidFromSecureRandomValues(cryptoObject) {
  const bytes = new Uint8Array(16);
  cryptoObject.getRandomValues(bytes);
  bytes[6] = (bytes[6] & 0x0f) | 0x40;
  bytes[8] = (bytes[8] & 0x3f) | 0x80;

  const hex = Array.from(bytes, (value) => value.toString(16).padStart(2, '0')).join('');
  return `${hex.slice(0, 8)}-${hex.slice(8, 12)}-${hex.slice(12, 16)}-${hex.slice(16, 20)}-${hex.slice(20)}`;
}

function installRandomUuidCompatibility() {
  const cryptoObject = globalThis.crypto;
  if (!cryptoObject || typeof cryptoObject.randomUUID === 'function') return false;
  if (typeof cryptoObject.getRandomValues !== 'function') return false;

  const fallback = () => uuidFromSecureRandomValues(cryptoObject);

  try {
    Object.defineProperty(cryptoObject, 'randomUUID', {
      configurable: true,
      enumerable: false,
      value: fallback,
      writable: false
    });
  } catch {
    try {
      cryptoObject.randomUUID = fallback;
    } catch {
      return false;
    }
  }

  return typeof cryptoObject.randomUUID === 'function';
}

const httpsUpgradeStarted = upgradeToHttps();
const randomUuidPolyfilled = installRandomUuidCompatibility();

globalThis.__PROJECTPULSE_BROWSER_RUNTIME__ = Object.freeze({
  httpsUpgradeStarted,
  randomUuidPolyfilled,
  secureContext: Boolean(globalThis.isSecureContext)
});
