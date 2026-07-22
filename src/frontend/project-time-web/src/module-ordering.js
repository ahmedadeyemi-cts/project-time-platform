const MODULE_CODE_PATTERN = /^\s*(?:MODULE\s+)?(\d{1,3})([A-Z]*)\s*$/i;

function moduleCodeCandidate(value) {
  if (value && typeof value === 'object') {
    return value.navLabel
      ?? value.moduleNumber
      ?? value.moduleCode
      ?? value.label
      ?? value.code
      ?? '';
  }

  return value ?? '';
}

export function parseProjectPulseModuleCode(value) {
  const candidate = String(moduleCodeCandidate(value)).trim();
  const match = candidate.match(MODULE_CODE_PATTERN);

  if (!match) return null;

  const numericPart = Number.parseInt(match[1], 10);

  if (!Number.isInteger(numericPart)) return null;

  return {
    number: numericPart,
    suffix: match[2].toUpperCase(),
    code: `${String(numericPart).padStart(3, '0')}${match[2].toUpperCase()}`
  };
}

function stableModuleLabel(value) {
  if (!value || typeof value !== 'object') return String(value ?? '');

  return String(
    value.title
      ?? value.route
      ?? value.navLabel
      ?? value.moduleNumber
      ?? value.moduleCode
      ?? value.code
      ?? ''
  );
}

export function compareProjectPulseModules(left, right) {
  const leftCode = parseProjectPulseModuleCode(left);
  const rightCode = parseProjectPulseModuleCode(right);

  if (leftCode && rightCode) {
    if (leftCode.number !== rightCode.number) {
      return leftCode.number - rightCode.number;
    }

    if (leftCode.suffix !== rightCode.suffix) {
      if (!leftCode.suffix) return -1;
      if (!rightCode.suffix) return 1;
      return leftCode.suffix.localeCompare(rightCode.suffix, 'en', { sensitivity: 'base' });
    }
  } else if (leftCode) {
    return -1;
  } else if (rightCode) {
    return 1;
  }

  return stableModuleLabel(left).localeCompare(
    stableModuleLabel(right),
    'en',
    { sensitivity: 'base', numeric: true }
  );
}

export function sortProjectPulseModules(modules) {
  return [...(Array.isArray(modules) ? modules : [])].sort(compareProjectPulseModules);
}
