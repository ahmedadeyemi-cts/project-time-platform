export function safeCsvCell(value) {
  let text = String(value ?? '').replaceAll('\0', ' ');
  const start = text.trimStart();
  if (/^[=+\-@]/.test(start) && !/^[+-]?(?:\d+(?:\.\d*)?|\.\d+)(?:[eE][+-]?\d+)?$/.test(start.trim())) text = `'${text}`;
  return /[",\r\n]/.test(text) ? `"${text.replaceAll('"', '""')}"` : text;
}

export function isInternalNavigation(target) {
  return typeof target === 'string' && target.length > 0 && target.length <= 500 &&
    !/[\u0000-\u0020\u007f\\]/.test(target) &&
    (target.startsWith('#') || (target.startsWith('/') && !target.startsWith('//')));
}
