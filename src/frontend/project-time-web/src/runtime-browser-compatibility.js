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

const randomUuidPolyfilled = installRandomUuidCompatibility();

globalThis.__PROJECTPULSE_BROWSER_RUNTIME__ = Object.freeze({
  randomUuidPolyfilled,
  secureContext: Boolean(globalThis.isSecureContext)
});
