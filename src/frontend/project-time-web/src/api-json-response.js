const ENVELOPE_KEYS = Object.freeze([
  'data', 'Data', 'result', 'Result', 'value', 'Value', 'payload', 'Payload'
]);

function objectValue(value) {
  return value && typeof value === 'object' && !Array.isArray(value) ? value : null;
}

export function unwrapApiPayload(payload, expectedKeys = []) {
  let current = objectValue(payload) || {};
  const hasExpected = (value) => expectedKeys.length === 0
    ? false
    : expectedKeys.some((key) => Object.prototype.hasOwnProperty.call(value, key));

  if (hasExpected(current)) return current;

  for (let depth = 0; depth < 3; depth += 1) {
    const envelopeKey = ENVELOPE_KEYS.find((key) => objectValue(current[key]));
    if (!envelopeKey) break;
    current = current[envelopeKey];
    if (hasExpected(current)) return current;
  }

  return current;
}

export async function readApiJson(response, path, expectedKeys = []) {
  const raw = await response.text();
  const contentType = String(response.headers.get('content-type') || '').toLowerCase();
  let parsed;

  try {
    parsed = raw ? JSON.parse(raw) : {};
  } catch {
    const error = new Error(
      response.ok
        ? `${path} returned non-JSON content instead of Pulse API data. Refresh the page after the API deployment completes.`
        : `${path} failed with HTTP ${response.status}.`
    );
    error.status = response.status;
    error.contentType = contentType;
    error.responsePreview = raw.slice(0, 160);
    throw error;
  }

  const payload = unwrapApiPayload(parsed, expectedKeys);
  if (!response.ok) {
    const error = new Error(
      payload?.message
      || payload?.Message
      || payload?.detail
      || payload?.Detail
      || `${path} returned HTTP ${response.status}`
    );
    error.status = response.status;
    error.payload = payload;
    throw error;
  }

  return payload;
}
