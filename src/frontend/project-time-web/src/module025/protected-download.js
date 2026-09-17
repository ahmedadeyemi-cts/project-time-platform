export function sessionHeaders(extra = {}) {
  try {
    const raw = window.localStorage.getItem('projectPulseAuthSession');
    const session = raw ? JSON.parse(raw) : null;
    return {
      ...(session?.sessionToken ? {
        Authorization: `Bearer ${session.sessionToken}`,
        'X-ProjectPulse-Session': session.sessionToken
      } : {}),
      ...extra
    };
  } catch {
    return extra;
  }
}

export async function downloadProtected(url, fileName) {
  const response = await fetch(url, { credentials: 'include', headers: sessionHeaders() });
  if (!response.ok) {
    const payload = await response.json().catch(() => ({}));
    throw new Error(payload.message || `Download failed (${response.status}).`);
  }
  const blobUrl = URL.createObjectURL(await response.blob());
  const anchor = document.createElement('a');
  anchor.href = blobUrl;
  anchor.download = fileName;
  document.body.appendChild(anchor);
  anchor.click();
  anchor.remove();
  window.setTimeout(() => URL.revokeObjectURL(blobUrl), 1000);
}
