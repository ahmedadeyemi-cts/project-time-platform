import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const files = {
  app: path.join(root, 'src', 'App.jsx'),
  welcome: path.join(root, 'src', 'RoleWelcomeDashboard.jsx'),
  entra: path.join(root, 'src', 'EntraSecretAdministrationCenter.jsx'),
  crm: path.join(root, 'src', 'CrmErpIntegrationCenter.jsx'),
};

function read(file) {
  return fs.readFileSync(file, 'utf8');
}

function write(file, content) {
  fs.writeFileSync(file, content);
}

function replaceOnce(source, search, replacement, label) {
  const index = typeof search === 'string' ? source.indexOf(search) : source.search(search);
  if (index < 0) throw new Error(`Role workspace governance injector could not find ${label}.`);
  if (typeof search === 'string') {
    if (source.indexOf(search, index + search.length) >= 0) {
      throw new Error(`Role workspace governance injector found more than one ${label}.`);
    }
    return source.replace(search, replacement);
  }
  const matches = source.match(new RegExp(search.source, search.flags.includes('g') ? search.flags : `${search.flags}g`)) || [];
  if (matches.length !== 1) {
    throw new Error(`Role workspace governance injector expected one ${label}, found ${matches.length}.`);
  }
  return source.replace(search, replacement);
}

function injectApp(source) {
  if (!source.includes('ROLE_WORKSPACE_ENTRA_CRM_GOVERNANCE_IMPORTS')) {
    source = replaceOnce(
      source,
      "import { useEffect, useLayoutEffect, useMemo, useState, useRef } from 'react';",
      `/* ROLE_WORKSPACE_ENTRA_CRM_GOVERNANCE_IMPORTS */\nimport EntraSecretExpirationGlobalWarning from './EntraSecretExpirationGlobalWarning.jsx';\nimport { applyRoleWorkspaceGovernance, getRoleWorkspaceName } from './role-workspace-governance.js';\nimport { useEffect, useLayoutEffect, useMemo, useState, useRef } from 'react';`,
      'App React import',
    );
  }

  if (!source.includes('ROLE_WORKSPACE_GOVERNANCE_VISIBLE_MODULES')) {
    const pattern = /function getVisibleRoleModules\(user\) \{[\s\S]*?\n\}\n\nfunction getRoleDisplayName\(user\) \{[\s\S]*?\n\}/;
    source = replaceOnce(
      source,
      pattern,
      `/* ROLE_WORKSPACE_GOVERNANCE_VISIBLE_MODULES */\nfunction getVisibleRoleModules(user) {\n  if (!user) return [];\n\n  const assignedRoleCodes = new Set((user?.roles ?? []).map((role) => String(role.roleCode ?? '').toUpperCase()));\n  const permissionFilteredModules = roleWorkspaceModules.filter((module) => {\n    const strictRoleCodes = module.strictRoleCodes ?? [];\n    if (strictRoleCodes.length > 0\n        && !strictRoleCodes.some((roleCode) => assignedRoleCodes.has(String(roleCode).toUpperCase()))) {\n      return false;\n    }\n\n    return userHasAnyPermission(user, module.permissions)\n      || (module.roleCodes ?? []).some((roleCode) => assignedRoleCodes.has(String(roleCode).toUpperCase()));\n  });\n\n  return applyRoleWorkspaceGovernance(user, permissionFilteredModules, roleWorkspaceModules);\n}\n\nfunction getRoleDisplayName(user) {\n  return getRoleWorkspaceName(user);\n}`,
      'App visible-module and role-display functions',
    );
  }

  if (!source.includes('ROLE_WORKSPACE_SIGNED_IN_USER')) {
    source = replaceOnce(
      source,
      '            <h1>{activeWorkspaceTitle}</h1>',
      `            <h1>{activeWorkspaceTitle}</h1>\n            {/* ROLE_WORKSPACE_SIGNED_IN_USER */}\n            <small className="workspace-signed-in-user">\n              Signed in as {userPreferences.displayNameOverride || currentUser.data?.displayName || authSession?.displayName || authSession?.username || currentUser.data?.email || 'Unknown user'}\n            </small>`,
      'workspace heading',
    );
  }

  if (!source.includes('ENTRA_SECRET_EXPIRATION_GLOBAL_WARNING_MOUNT')) {
    const mainOpening = "    <main className={`app-shell route-${activeRoute} enterprise-nav-enabled ${isSideNavigationOpen ? 'sidebar-expanded' : 'sidebar-collapsed'}`}>";
    source = replaceOnce(
      source,
      mainOpening,
      `${mainOpening}\n      {/* ENTRA_SECRET_EXPIRATION_GLOBAL_WARNING_MOUNT */}\n      <EntraSecretExpirationGlobalWarning authSession={authSession} />`,
      'authenticated application main element',
    );
  }

  return source;
}

function injectWelcome(source) {
  if (!source.includes('ROLE_WORKSPACE_WELCOME_IMPORT')) {
    source = replaceOnce(
      source,
      "import './role-welcome-dashboard.css';",
      `/* ROLE_WORKSPACE_WELCOME_IMPORT */\nimport { getRoleWorkspaceLabel, getRoleWorkspaceName } from './role-workspace-governance.js';\nimport './role-welcome-dashboard.css';`,
      'RoleWelcomeDashboard stylesheet import',
    );
  }

  if (!source.includes('ROLE_WORKSPACE_TIME_ENTRY_EXCLUSIONS')) {
    source = replaceOnce(
      source,
      "  'PROJECT_TEAM_COORDINATOR'\n]);",
      `  'PROJECT_TEAM_COORDINATOR',\n  /* ROLE_WORKSPACE_TIME_ENTRY_EXCLUSIONS */\n  'ACCOUNTING',\n  'ACCOUNTING_BILLING',\n  'BILLING',\n  'FINANCE',\n  'RESALE'\n]);`,
      'RoleWelcomeDashboard time-entry exclusions',
    );
  }

  source = source.replace(
    "  const firstName = String(displayName || 'there').trim().split(/\\s+/)[0] || 'there';",
    "  const welcomeDisplayName = String(displayName || 'there').trim() || 'there';",
  );
  source = source.replace(
    "{firstName}</h1>",
    "{welcomeDisplayName}</h1>",
  );
  source = source.replace(
    '<small>{titleCase(persona)} workspace</small>',
    '<small>{getRoleWorkspaceLabel(normalizedRoles)}</small>',
  );
  source = source.replace(
    '<h2>{titleCase(persona)} operations</h2>',
    '<h2>{getRoleWorkspaceName(normalizedRoles)} operations</h2>',
  );

  if (!source.includes('welcomeDisplayName')
      || !source.includes('getRoleWorkspaceLabel(normalizedRoles)')) {
    throw new Error('RoleWelcomeDashboard canonical workspace labeling was not installed.');
  }

  return source;
}

function injectEntra(source) {
  if (!source.includes('ENTRA_EXPIRATION_GOVERNANCE_PANEL_IMPORT')) {
    source = replaceOnce(
      source,
      "import './entra-secret-administration-center.css';",
      `/* ENTRA_EXPIRATION_GOVERNANCE_PANEL_IMPORT */\nimport EntraSecretExpirationGovernancePanel from './EntraSecretExpirationGovernancePanel.jsx';\nimport './entra-secret-administration-center.css';`,
      'Entra administration stylesheet import',
    );
  }

  if (!source.includes('ENTRA_EXPIRATION_GOVERNANCE_PANEL_MOUNT')) {
    source = replaceOnce(
      source,
      '      <div className="entra-admin-panel">',
      `      {/* ENTRA_EXPIRATION_GOVERNANCE_PANEL_MOUNT */}\n      <EntraSecretExpirationGovernancePanel />\n\n      <div className="entra-admin-panel">`,
      'Entra administration panel',
    );
  }

  return source;
}

function injectCrm(source) {
  if (!source.includes('CRM_ERP_TOKEN_PERSISTENCE_PANEL_IMPORT')) {
    source = replaceOnce(
      source,
      "import './crm-erp-integration-center.css';",
      `/* CRM_ERP_TOKEN_PERSISTENCE_PANEL_IMPORT */\nimport CrmErpTokenPersistencePanel from './CrmErpTokenPersistencePanel.jsx';\nimport './crm-erp-integration-center.css';`,
      'CRM integration stylesheet import',
    );
  }

  if (!source.includes('CRM_ERP_TOKEN_PERSISTENCE_PANEL_MOUNT')) {
    source = replaceOnce(
      source,
      '      <section className="crm-erp-platform-overview" aria-label="Core integration platforms">',
      `      {/* CRM_ERP_TOKEN_PERSISTENCE_PANEL_MOUNT */}\n      <CrmErpTokenPersistencePanel\n        provider={selected}\n        canManage={canManage}\n        onRefresh={() => load(selected?.providerKey)}\n      />\n\n      <section className="crm-erp-platform-overview" aria-label="Core integration platforms">`,
      'CRM platform overview',
    );
  }

  return source;
}

const updated = {
  app: injectApp(read(files.app)),
  welcome: injectWelcome(read(files.welcome)),
  entra: injectEntra(read(files.entra)),
  crm: injectCrm(read(files.crm)),
};

for (const [key, content] of Object.entries(updated)) {
  write(files[key], content);
}

console.log('ROLE_WORKSPACE_ENTRA_CRM_GOVERNANCE_INJECTOR=PASS');
