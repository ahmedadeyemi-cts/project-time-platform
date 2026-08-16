function asArray(value) {
  if (Array.isArray(value)) return value;
  if (value && typeof value !== 'string' && typeof value[Symbol.iterator] === 'function') {
    return [...value];
  }
  return value == null ? [] : [value];
}

function canonicalRoleCode(value) {
  return String(value || '')
    .trim()
    .toUpperCase()
    .replace(/[^A-Z0-9]+/g, '_')
    .replace(/^_+|_+$/g, '');
}

function canonicalModuleCode(value) {
  return String(value || '')
    .trim()
    .toUpperCase()
    .replace(/[^A-Z0-9]+/g, '');
}

function moduleCodeOf(value) {
  return canonicalModuleCode(
    value?.moduleCode
    ?? value?.ModuleCode
    ?? value?.moduleNumber
    ?? value?.ModuleNumber
    ?? value
  );
}

function roleCodeOf(value) {
  return canonicalRoleCode(value?.roleCode ?? value?.RoleCode ?? value);
}

function sorted(values) {
  return [...new Set(values)].sort((left, right) =>
    left.localeCompare(right, undefined, { numeric: true, sensitivity: 'base' })
  );
}

function isActiveModule(module) {
  const value = module?.isActive ?? module?.IsActive;
  return value !== false && String(value ?? 'true').toLowerCase() !== 'false';
}

export function resolveModuleNavigationAccess({
  applicationModules = [],
  dynamicModules = [],
  grants = [],
  legacyFallback = [],
  actorRoleCodes = [],
  actualSessionPermanentFullControl = false,
  retiredModuleNumbers = []
} = {}) {
  const applicationModuleNumbers = new Set(
    asArray(applicationModules).map(moduleCodeOf).filter(Boolean)
  );
  const activeDynamicModuleNumbers = new Set();
  const inactiveDynamicModuleNumbers = new Set();
  for (const module of asArray(dynamicModules)) {
    const moduleCode = moduleCodeOf(module);
    if (!moduleCode) continue;
    (isActiveModule(module)
      ? activeDynamicModuleNumbers
      : inactiveDynamicModuleNumbers).add(moduleCode);
  }

  const retired = new Set(
    asArray(retiredModuleNumbers).map(moduleCodeOf).filter(Boolean)
  );
  const roleSet = new Set(
    asArray(actorRoleCodes).map(roleCodeOf).filter(Boolean)
  );
  const explicitDeniedModuleNumbers = new Set();
  const explicitGrantedModuleNumbers = new Set();

  if (!actualSessionPermanentFullControl) {
    for (const grant of asArray(grants)) {
      if (!roleSet.has(roleCodeOf(grant))) continue;
      const actionCode = canonicalRoleCode(grant?.actionCode ?? grant?.ActionCode);
      if (!['MODULE_ACCESS', 'MODULE_VIEW'].includes(actionCode)) continue;
      const moduleCode = moduleCodeOf(grant);
      if (!moduleCode) continue;
      const effect = canonicalRoleCode(
        grant?.grantEffect
        ?? grant?.GrantEffect
        ?? grant?.effect
        ?? grant?.Effect
      );
      if (effect === 'DENY' || grant?.explicitDeny === true || grant?.ExplicitDeny === true) {
        explicitDeniedModuleNumbers.add(moduleCode);
        continue;
      }
      if (effect === 'GRANT' || effect === 'ALLOW' || grant?.granted === true || grant?.Granted === true) {
        explicitGrantedModuleNumbers.add(moduleCode);
      }
    }
  }

  const legacyFallbackModuleNumbers = new Set();
  for (const fallback of asArray(legacyFallback)) {
    if (!roleSet.has(roleCodeOf(fallback))) continue;
    const moduleCode = moduleCodeOf(fallback);
    if (moduleCode) legacyFallbackModuleNumbers.add(moduleCode);
  }

  const unregisteredLegacyModuleNumbers = new Set(
    [...applicationModuleNumbers].filter((moduleCode) =>
      !activeDynamicModuleNumbers.has(moduleCode)
      && !inactiveDynamicModuleNumbers.has(moduleCode)
      && !retired.has(moduleCode)
    )
  );

  const deniedModuleNumbers = new Set(retired);
  if (!actualSessionPermanentFullControl) {
    for (const moduleCode of inactiveDynamicModuleNumbers) deniedModuleNumbers.add(moduleCode);
    for (const moduleCode of explicitDeniedModuleNumbers) deniedModuleNumbers.add(moduleCode);
  }

  return {
    deniedModuleNumbers: sorted(deniedModuleNumbers),
    explicitDeniedModuleNumbers: sorted(explicitDeniedModuleNumbers),
    explicitGrantedModuleNumbers: sorted(explicitGrantedModuleNumbers),
    activeDynamicModuleNumbers: sorted(activeDynamicModuleNumbers),
    inactiveDynamicModuleNumbers: sorted(inactiveDynamicModuleNumbers),
    legacyFallbackModuleNumbers: sorted(legacyFallbackModuleNumbers),
    unregisteredLegacyModuleNumbers: sorted(unregisteredLegacyModuleNumbers),
    applicationModuleNumbers: sorted(applicationModuleNumbers),
    actualSessionPermanentFullControl: Boolean(actualSessionPermanentFullControl)
  };
}
