from pathlib import Path


def replace_exact(source: str, old: str, new: str, label: str, expected: int = 1) -> str:
    count = source.count(old)
    if count != expected:
        raise SystemExit(f"{label}: expected {expected} occurrence(s), found {count}")
    return source.replace(old, new, expected)


def patch_permission_bridge() -> None:
    path = Path('src/frontend/project-time-web/src/module-availability-bridge.js')
    source = path.read_text()

    source = replace_exact(
        source,
        "const MORE_SEARCH_HIDDEN_ATTRIBUTE = 'data-projectpulse-more-search-hidden';\nconst RETIRED_ROUTE_NOTICE_KEY = 'projectPulseRetiredWorkTaskBuilderNotice';",
        "const MORE_SEARCH_HIDDEN_ATTRIBUTE = 'data-projectpulse-more-search-hidden';\nconst PERMISSION_REFRESH_SINGLE_FLIGHT_CONTRACT = 'PERMISSION_REFRESH_SINGLE_FLIGHT_V1';\nconst RETIRED_ROUTE_NOTICE_KEY = 'projectPulseRetiredWorkTaskBuilderNotice';",
        'permission refresh contract constant'
    )

    source = replace_exact(
        source,
        "  return headers;\n}\n\nfunction canonicalRoleCode(value) {",
        "  return headers;\n}\n\nfunction currentPermissionRefreshIdentity() {\n  const token = sessionToken();\n  const viewAsUserId = activeViewAs()?.userId || '';\n  return `${token || 'anonymous'}\\u0000${viewAsUserId}`;\n}\n\nfunction canonicalRoleCode(value) {",
        'permission refresh identity helper'
    )

    source = replace_exact(
        source,
        "  let moreSearchValue = '';\n  let refreshSequence = 0;",
        "  let moreSearchValue = '';\n  let refreshSequence = 0;\n  let permissionRefreshInFlight = null;\n  let permissionRefreshIdentity = '';\n  let lastReadyNavigation = null;\n  let lastReadyIdentity = '';",
        'permission refresh state variables'
    )

    source = replace_exact(
        source,
        "      authoritySource: effectiveActor.authoritySource || '',\n      deniedModuleNumbers: [...deniedModuleNumbers],",
        "      authoritySource: effectiveActor.authoritySource || '',\n      refreshing: effectiveActor.refreshing === true,\n      refreshFailed: effectiveActor.refreshFailed === true,\n      refreshContract: PERMISSION_REFRESH_SINGLE_FLIGHT_CONTRACT,\n      deniedModuleNumbers: [...deniedModuleNumbers],",
        'published refresh state contract'
    )

    source = replace_exact(
        source,
        "  async function refreshPermissions() {\n  const sequence = ++refreshSequence;\n  const requestedViewAsUserId = activeViewAs()?.userId || '';\n  const token = sessionToken();",
        "  async function executePermissionRefresh(requestedIdentity) {\n  const sequence = ++refreshSequence;\n  const requestedViewAsUserId = activeViewAs()?.userId || '';\n  const token = sessionToken();\n  const preserveReady = Boolean(\n    token\n    && lastReadyNavigation\n    && lastReadyIdentity === requestedIdentity\n  );",
        'permission refresh executor signature'
    )

    source = replace_exact(
        source,
        "  if (!token) {\n    deniedModuleNumbers = new Set(RETIRED_MODULE_NUMBERS);",
        "  if (!token) {\n    lastReadyNavigation = null;\n    lastReadyIdentity = '';\n    deniedModuleNumbers = new Set(RETIRED_MODULE_NUMBERS);",
        'anonymous refresh snapshot reset'
    )

    old_loading = """  // Clear stale decisions before requesting the next effective identity.
  // The More menu remains hidden while loading, and server endpoints remain
  // authoritative, but a prior user's denial cannot redirect the new user.
  permissionEvidenceState = 'loading';
  deniedModuleNumbers = new Set(RETIRED_MODULE_NUMBERS);
  effectiveActor = {
    roleCodes: [],
    isViewAs: Boolean(requestedViewAsUserId),
    permanentFullControl: false,
    authoritySource: 'permission_refresh_pending',
    explicitDeniedModuleNumbers: [],
    explicitGrantedModuleNumbers: [],
    activeDynamicModuleNumbers: [],
    inactiveDynamicModuleNumbers: [],
    legacyFallbackModuleNumbers: [],
    unregisteredLegacyModuleNumbers: []
  };
  applyVisibility();
  publishNavigationState();
"""
    new_loading = """  // A refresh for the same effective identity must not erase a previously
  // verified module list. Preserve the last server-authorized result while the
  // next request is in flight; only an actual identity change enters loading.
  if (preserveReady) {
    deniedModuleNumbers = new Set(lastReadyNavigation.deniedModuleNumbers);
    permissionEvidenceState = 'ready';
    effectiveActor = {
      ...lastReadyNavigation.effectiveActor,
      roleCodes: [...lastReadyNavigation.effectiveActor.roleCodes],
      explicitDeniedModuleNumbers: [...lastReadyNavigation.effectiveActor.explicitDeniedModuleNumbers],
      explicitGrantedModuleNumbers: [...lastReadyNavigation.effectiveActor.explicitGrantedModuleNumbers],
      activeDynamicModuleNumbers: [...lastReadyNavigation.effectiveActor.activeDynamicModuleNumbers],
      inactiveDynamicModuleNumbers: [...lastReadyNavigation.effectiveActor.inactiveDynamicModuleNumbers],
      legacyFallbackModuleNumbers: [...lastReadyNavigation.effectiveActor.legacyFallbackModuleNumbers],
      unregisteredLegacyModuleNumbers: [...lastReadyNavigation.effectiveActor.unregisteredLegacyModuleNumbers],
      refreshing: true,
      refreshFailed: false
    };
  } else {
    permissionEvidenceState = 'loading';
    deniedModuleNumbers = new Set(RETIRED_MODULE_NUMBERS);
    effectiveActor = {
      roleCodes: [],
      isViewAs: Boolean(requestedViewAsUserId),
      permanentFullControl: false,
      authoritySource: 'permission_refresh_pending',
      explicitDeniedModuleNumbers: [],
      explicitGrantedModuleNumbers: [],
      activeDynamicModuleNumbers: [],
      inactiveDynamicModuleNumbers: [],
      legacyFallbackModuleNumbers: [],
      unregisteredLegacyModuleNumbers: [],
      refreshing: true,
      refreshFailed: false
    };
  }
  applyVisibility();
  publishNavigationState();
"""
    source = replace_exact(source, old_loading, new_loading, 'permission refresh loading transition')

    source = replace_exact(
        source,
        "      legacyFallbackModuleNumbers: navigationAccess.legacyFallbackModuleNumbers,\n      unregisteredLegacyModuleNumbers: navigationAccess.unregisteredLegacyModuleNumbers\n    };\n    applyVisibility();",
        "      legacyFallbackModuleNumbers: navigationAccess.legacyFallbackModuleNumbers,\n      unregisteredLegacyModuleNumbers: navigationAccess.unregisteredLegacyModuleNumbers,\n      refreshing: false,\n      refreshFailed: false\n    };\n    lastReadyIdentity = requestedIdentity;\n    lastReadyNavigation = {\n      deniedModuleNumbers: [...deniedModuleNumbers],\n      effectiveActor: {\n        ...effectiveActor,\n        roleCodes: [...effectiveActor.roleCodes],\n        explicitDeniedModuleNumbers: [...effectiveActor.explicitDeniedModuleNumbers],\n        explicitGrantedModuleNumbers: [...effectiveActor.explicitGrantedModuleNumbers],\n        activeDynamicModuleNumbers: [...effectiveActor.activeDynamicModuleNumbers],\n        inactiveDynamicModuleNumbers: [...effectiveActor.inactiveDynamicModuleNumbers],\n        legacyFallbackModuleNumbers: [...effectiveActor.legacyFallbackModuleNumbers],\n        unregisteredLegacyModuleNumbers: [...effectiveActor.unregisteredLegacyModuleNumbers]\n      }\n    };\n    applyVisibility();",
        'permission refresh ready snapshot'
    )

    old_tail = """  } catch {
    const currentViewAsUserId = activeViewAs()?.userId || '';
    if (sequence !== refreshSequence || currentViewAsUserId !== requestedViewAsUserId) return;
    deniedModuleNumbers = new Set(RETIRED_MODULE_NUMBERS);
    permissionEvidenceState = 'unavailable';
    effectiveActor = {
      roleCodes: [],
      isViewAs: Boolean(activeViewAs()),
      permanentFullControl: false,
      authoritySource: 'server_endpoint_authorization_only',
      explicitDeniedModuleNumbers: [],
      explicitGrantedModuleNumbers: [],
      activeDynamicModuleNumbers: [],
      inactiveDynamicModuleNumbers: [],
      legacyFallbackModuleNumbers: [],
      unregisteredLegacyModuleNumbers: []
    };
    applyVisibility();
    publishNavigationState();
  }
}

  const boot = () => {
"""
    new_tail = """  } catch {
    const currentViewAsUserId = activeViewAs()?.userId || '';
    if (sequence !== refreshSequence || currentViewAsUserId !== requestedViewAsUserId) return;
    if (lastReadyNavigation && lastReadyIdentity === requestedIdentity) {
      deniedModuleNumbers = new Set(lastReadyNavigation.deniedModuleNumbers);
      permissionEvidenceState = 'ready';
      effectiveActor = {
        ...lastReadyNavigation.effectiveActor,
        roleCodes: [...lastReadyNavigation.effectiveActor.roleCodes],
        explicitDeniedModuleNumbers: [...lastReadyNavigation.effectiveActor.explicitDeniedModuleNumbers],
        explicitGrantedModuleNumbers: [...lastReadyNavigation.effectiveActor.explicitGrantedModuleNumbers],
        activeDynamicModuleNumbers: [...lastReadyNavigation.effectiveActor.activeDynamicModuleNumbers],
        inactiveDynamicModuleNumbers: [...lastReadyNavigation.effectiveActor.inactiveDynamicModuleNumbers],
        legacyFallbackModuleNumbers: [...lastReadyNavigation.effectiveActor.legacyFallbackModuleNumbers],
        unregisteredLegacyModuleNumbers: [...lastReadyNavigation.effectiveActor.unregisteredLegacyModuleNumbers],
        refreshing: false,
        refreshFailed: true
      };
    } else {
      deniedModuleNumbers = new Set(RETIRED_MODULE_NUMBERS);
      permissionEvidenceState = 'unavailable';
      effectiveActor = {
        roleCodes: [],
        isViewAs: Boolean(activeViewAs()),
        permanentFullControl: false,
        authoritySource: 'server_endpoint_authorization_only',
        explicitDeniedModuleNumbers: [],
        explicitGrantedModuleNumbers: [],
        activeDynamicModuleNumbers: [],
        inactiveDynamicModuleNumbers: [],
        legacyFallbackModuleNumbers: [],
        unregisteredLegacyModuleNumbers: [],
        refreshing: false,
        refreshFailed: true
      };
    }
    applyVisibility();
    publishNavigationState();
  }
}

  function publishPermissionRefreshState(inFlight, startedAt = 0) {
    const detail = {
      contract: PERMISSION_REFRESH_SINGLE_FLIGHT_CONTRACT,
      inFlight,
      viewAsUserId: activeViewAs()?.userId || '',
      startedAt: inFlight ? startedAt : 0,
      completedAt: inFlight ? 0 : Date.now()
    };
    window.__projectPulsePermissionRefreshState = detail;
  }

  function refreshPermissions() {
    const requestedIdentity = currentPermissionRefreshIdentity();
    if (permissionRefreshInFlight && permissionRefreshIdentity === requestedIdentity) {
      return permissionRefreshInFlight;
    }

    const startedAt = Date.now();
    const refresh = executePermissionRefresh(requestedIdentity);
    permissionRefreshInFlight = refresh;
    permissionRefreshIdentity = requestedIdentity;
    publishPermissionRefreshState(true, startedAt);

    const complete = () => {
      if (permissionRefreshInFlight !== refresh) return;
      permissionRefreshInFlight = null;
      permissionRefreshIdentity = '';
      publishPermissionRefreshState(false);
    };
    refresh.then(complete, complete);
    return refresh;
  }

  const boot = () => {
"""
    source = replace_exact(source, old_tail, new_tail, 'permission refresh catch and single-flight wrapper')

    path.write_text(source)


def patch_background_gate() -> None:
    path = Path('src/frontend/project-time-web/src/background-request-role-gate.js')
    source = path.read_text()

    source = replace_exact(
        source,
        "const MODULE_DIRECTORY_AUTHORITY_RETRY_MS = 100;\nconst MODULE_DIRECTORY_AUTHORITY_MAX_ATTEMPTS = 80;\nconst MODULE_DIRECTORY_PERMISSION_REFRESH_THROTTLE_MS = 1500;\nconst MODULE_DIRECTORY_SNAPSHOT_MAX_AGE_MS = 30 * 60 * 1000;",
        "const MODULE_DIRECTORY_AUTHORITY_RETRY_MS = 250;\nconst MODULE_DIRECTORY_AUTHORITY_MAX_ATTEMPTS = 32;\nconst MODULE_DIRECTORY_PERMISSION_REFRESH_THROTTLE_MS = 2000;\nconst MODULE_DIRECTORY_SNAPSHOT_MAX_AGE_MS = 30 * 60 * 1000;\nconst MODULE_DIRECTORY_SINGLE_FLIGHT_SCHEDULER_CONTRACT = 'MODULE_DIRECTORY_SINGLE_FLIGHT_SCHEDULER_V3';\n\nlet moduleDirectoryAuthorityTimer = 0;\nlet moduleDirectoryAuthorityAttempt = 0;\nlet moduleDirectoryAuthoritySource = '';",
        'module directory single-flight scheduler constants'
    )

    source = replace_exact(
        source,
        "function requestModuleDirectoryPermissionRefresh(source) {\n  const now = Date.now();",
        "function requestModuleDirectoryPermissionRefresh(source) {\n  const refreshState = window.__projectPulsePermissionRefreshState;\n  if (refreshState?.contract === 'PERMISSION_REFRESH_SINGLE_FLIGHT_V1'\n      && refreshState.inFlight === true) return;\n\n  const now = Date.now();",
        'module directory in-flight refresh guard'
    )

    old_scheduler = """function scheduleImmediateModulesAuthority(source, attempt = 0) {
  window.setTimeout(() => {
    if (currentRoute() !== MODULE_DIRECTORY_ROUTE) return;
    if (ensureImmediateModulesAuthority(source)) return;

    requestModuleDirectoryPermissionRefresh(source);
    if (attempt < MODULE_DIRECTORY_AUTHORITY_MAX_ATTEMPTS) {
      scheduleImmediateModulesAuthority(source, attempt + 1);
    }
  }, attempt === 0 ? 0 : MODULE_DIRECTORY_AUTHORITY_RETRY_MS);
}
"""
    new_scheduler = """function clearModuleDirectoryAuthoritySchedule() {
  window.clearTimeout(moduleDirectoryAuthorityTimer);
  moduleDirectoryAuthorityTimer = 0;
  moduleDirectoryAuthorityAttempt = 0;
  moduleDirectoryAuthoritySource = '';
}

function scheduleImmediateModulesAuthority(source) {
  moduleDirectoryAuthoritySource = source || moduleDirectoryAuthoritySource || 'module_directory_authority_retry';
  if (moduleDirectoryAuthorityTimer) return;

  moduleDirectoryAuthorityAttempt = 0;
  const run = () => {
    moduleDirectoryAuthorityTimer = 0;
    if (currentRoute() !== MODULE_DIRECTORY_ROUTE) {
      clearModuleDirectoryAuthoritySchedule();
      return;
    }
    if (ensureImmediateModulesAuthority(moduleDirectoryAuthoritySource)) {
      clearModuleDirectoryAuthoritySchedule();
      return;
    }

    requestModuleDirectoryPermissionRefresh(moduleDirectoryAuthoritySource);
    if (moduleDirectoryAuthorityAttempt >= MODULE_DIRECTORY_AUTHORITY_MAX_ATTEMPTS) {
      clearModuleDirectoryAuthoritySchedule();
      return;
    }

    moduleDirectoryAuthorityAttempt += 1;
    moduleDirectoryAuthorityTimer = window.setTimeout(run, MODULE_DIRECTORY_AUTHORITY_RETRY_MS);
  };

  window.__projectPulseModuleDirectoryAuthorityScheduler = {
    contract: MODULE_DIRECTORY_SINGLE_FLIGHT_SCHEDULER_CONTRACT,
    active: true
  };
  moduleDirectoryAuthorityTimer = window.setTimeout(run, 0);
}
"""
    source = replace_exact(source, old_scheduler, new_scheduler, 'single-flight module authority scheduler')

    source = replace_exact(
        source,
        "    if (detail?.state === 'ready'\n        && detail?.provisionalModuleDirectorySnapshot !== true) {\n      const signature =",
        "    if (detail?.state === 'ready'\n        && detail?.provisionalModuleDirectorySnapshot !== true) {\n      clearModuleDirectoryAuthoritySchedule();\n      const signature =",
        'final authority scheduler completion'
    )

    path.write_text(source)


def patch_modules_portal() -> None:
    path = Path('src/frontend/project-time-web/src/ModulesDirectoryPortal.jsx')
    source = path.read_text()

    source = replace_exact(
        source,
        "const MODULE_DIRECTORY_AUTHORITY_POLL_MS = 500;\nconst MODULE_DIRECTORY_NONBLOCKING_AUTHORITY_CONTRACT = 'MODULE_DIRECTORY_NONBLOCKING_AUTHORITY_V2';",
        "const MODULE_DIRECTORY_AUTHORITY_POLL_MS = 500;\nconst MODULE_DIRECTORY_AUTHORITY_REFRESH_THROTTLE_MS = 2500;\nconst MODULE_DIRECTORY_NONBLOCKING_AUTHORITY_CONTRACT = 'MODULE_DIRECTORY_NONBLOCKING_AUTHORITY_V2';\nconst MODULE_DIRECTORY_STABLE_HYDRATION_CONTRACT = 'MODULE_DIRECTORY_STABLE_HYDRATION_V3';",
        'Modules portal hydration constants'
    )

    source = replace_exact(
        source,
        "  const directoryResolvedRef = useRef(false);\n  const expandedForDirectory = useRef(new Set());",
        "  const directoryResolvedRef = useRef(false);\n  const authorityRefreshRequestedAtRef = useRef(0);\n  const expandedForDirectory = useRef(new Set());",
        'Modules portal authority throttle ref'
    )

    old_request = """    const requestAuthorityRefresh = (source) => {
      window.dispatchEvent(new CustomEvent('projectpulse:permissions-changed', {
        detail: { source, contract: MODULE_DIRECTORY_NONBLOCKING_AUTHORITY_CONTRACT }
      }));
    };
"""
    new_request = """    const requestAuthorityRefresh = (source) => {
      const refreshState = window.__projectPulsePermissionRefreshState;
      if (refreshState?.contract === 'PERMISSION_REFRESH_SINGLE_FLIGHT_V1'
          && refreshState.inFlight === true) return;

      const now = Date.now();
      if (now - authorityRefreshRequestedAtRef.current < MODULE_DIRECTORY_AUTHORITY_REFRESH_THROTTLE_MS) return;
      authorityRefreshRequestedAtRef.current = now;
      window.dispatchEvent(new CustomEvent('projectpulse:permissions-changed', {
        detail: {
          source,
          contract: MODULE_DIRECTORY_NONBLOCKING_AUTHORITY_CONTRACT,
          hydrationContract: MODULE_DIRECTORY_STABLE_HYDRATION_CONTRACT
        }
      }));
    };
"""
    source = replace_exact(source, old_request, new_request, 'Modules portal permission refresh throttle')

    source = replace_exact(
        source,
        "        directoryResolvedRef.current = true;\n        setDirectoryResolved(true);",
        "        directoryResolvedRef.current = true;\n        authorityRefreshRequestedAtRef.current = 0;\n        setDirectoryResolved(true);",
        'Modules portal resolved authority reset'
    )

    source = replace_exact(
        source,
        "      directoryResolvedRef.current = false;\n      setDirectoryResolved(false);",
        "      directoryResolvedRef.current = false;\n      authorityRefreshRequestedAtRef.current = 0;\n      setDirectoryResolved(false);",
        'Modules portal identity reset'
    )

    source = replace_exact(
        source,
        "    <section id=\"modules-directory-page\" className=\"modules-directory-page\" aria-labelledby=\"modules-directory-title\" data-authority-contract={MODULE_DIRECTORY_AUTHORITY_CONTRACT}>",
        "    <section id=\"modules-directory-page\" className=\"modules-directory-page\" aria-labelledby=\"modules-directory-title\" data-authority-contract={MODULE_DIRECTORY_AUTHORITY_CONTRACT} data-hydration-contract={MODULE_DIRECTORY_STABLE_HYDRATION_CONTRACT}>",
        'Modules portal stable hydration marker'
    )

    path.write_text(source)


def patch_validators() -> None:
    focused_path = Path('src/frontend/project-time-web/scripts/validate-module-loading-assignment-propagation.mjs')
    focused = focused_path.read_text()

    focused = replace_exact(
        focused,
        "const backgroundGate = read('src/frontend/project-time-web/src/background-request-role-gate.js');",
        "const availabilityBridge = read('src/frontend/project-time-web/src/module-availability-bridge.js');\n[\n  'PERMISSION_REFRESH_SINGLE_FLIGHT_V1',\n  'permissionRefreshInFlight',\n  'lastReadyNavigation',\n  'executePermissionRefresh(requestedIdentity)',\n  'if (permissionRefreshInFlight && permissionRefreshIdentity === requestedIdentity)',\n  'refreshing: effectiveActor.refreshing === true',\n  'refreshFailed: effectiveActor.refreshFailed === true',\n  '__projectPulsePermissionRefreshState'\n].forEach((contract) => requireText(availabilityBridge, contract, 'permission refresh single-flight authority'));\nrejectText(availabilityBridge, 'async function refreshPermissions() {', 'permission refreshes must pass through the single-flight wrapper');\n\nconst backgroundGate = read('src/frontend/project-time-web/src/background-request-role-gate.js');",
        'focused permission refresh validator'
    )

    focused = replace_exact(
        focused,
        "  'MODULE_DIRECTORY_PERMISSION_REFRESH_THROTTLE_MS',\n  'MODULE_DIRECTORY_SNAPSHOT_MAX_AGE_MS'",
        "  'MODULE_DIRECTORY_PERMISSION_REFRESH_THROTTLE_MS',\n  'MODULE_DIRECTORY_SNAPSHOT_MAX_AGE_MS'",
        'noop marker',
        expected=0
    ) if False else focused

    focused = replace_exact(
        focused,
        "  'projectpulse:permissions-changed',\n  'role_not_applicable',",
        "  'projectpulse:permissions-changed',\n  'MODULE_DIRECTORY_SINGLE_FLIGHT_SCHEDULER_V3',\n  'moduleDirectoryAuthorityTimer',\n  '__projectPulsePermissionRefreshState',\n  'role_not_applicable',",
        'focused background scheduler contracts'
    )

    focused = replace_exact(
        focused,
        "rejectText(backgroundGate, \"case 'owners':\", 'owner catalog must not be replaced with an empty client payload');",
        "rejectText(backgroundGate, \"case 'owners':\", 'owner catalog must not be replaced with an empty client payload');\nrejectText(\n  backgroundGate,\n  'scheduleImmediateModulesAuthority(source, attempt + 1)',\n  'Modules authority retries must use one scheduler chain'\n);",
        'focused recursive scheduler rejection'
    )

    focused = replace_exact(
        focused,
        "  'module_directory_route_activated',\n  'directoryResolved={directoryResolved}'",
        "  'module_directory_route_activated',\n  'MODULE_DIRECTORY_STABLE_HYDRATION_V3',\n  'MODULE_DIRECTORY_AUTHORITY_REFRESH_THROTTLE_MS',\n  'authorityRefreshRequestedAtRef',\n  '__projectPulsePermissionRefreshState',\n  'directoryResolved={directoryResolved}'",
        'focused stable Modules hydration contracts'
    )

    focused = replace_exact(
        focused,
        "console.log('module_directory_authority=nonblocking_identity_scoped_refresh');",
        "console.log('module_directory_authority=single_flight_preserved_ready_refresh');",
        'focused Modules authority result marker'
    )
    focused_path.write_text(focused)

    directory_path = Path('src/frontend/project-time-web/scripts/validate-modules-directory-page.mjs')
    directory = directory_path.read_text()
    directory = replace_exact(
        directory,
        "  'MODULE_DIRECTORY_NONBLOCKING_AUTHORITY_V2',\n  'directoryResolvedRef',",
        "  'MODULE_DIRECTORY_NONBLOCKING_AUTHORITY_V2',\n  'MODULE_DIRECTORY_STABLE_HYDRATION_V3',\n  'MODULE_DIRECTORY_AUTHORITY_REFRESH_THROTTLE_MS',\n  'authorityRefreshRequestedAtRef',\n  '__projectPulsePermissionRefreshState',\n  'directoryResolvedRef',",
        'Modules page stable hydration validator'
    )
    directory_path.write_text(directory)

    workflow_path = Path('.github/workflows/module-loading-assignment-propagation-ci.yml')
    workflow = workflow_path.read_text()
    workflow = replace_exact(
        workflow,
        "      - 'src/frontend/project-time-web/src/background-request-role-gate.js'\n      - 'src/frontend/project-time-web/src/ProductionOperationsPanel.jsx'",
        "      - 'src/frontend/project-time-web/src/background-request-role-gate.js'\n      - 'src/frontend/project-time-web/src/module-availability-bridge.js'\n      - 'src/frontend/project-time-web/src/ProductionOperationsPanel.jsx'",
        'focused CI module availability bridge path'
    )
    workflow_path.write_text(workflow)


def main() -> None:
    patch_permission_bridge()
    patch_background_gate()
    patch_modules_portal()
    patch_validators()
    Path('scripts/release-test/finalize-modules-directory-authority-starvation.py').unlink(missing_ok=True)


if __name__ == '__main__':
    main()
