import fs from 'node:fs';
import path from 'node:path';

const webRoot = path.resolve(path.dirname(new URL(import.meta.url).pathname), '..');
const generatedAppPath = path.join(webRoot, 'src', 'App.Module001.g.jsx');

if (!fs.existsSync(generatedAppPath)) {
  throw new Error('Generate App.Module001.g.jsx before injecting the React-owned More menu.');
}

let source = fs.readFileSync(generatedAppPath, 'utf8');
const marker = '/* PROJECTPULSE_REACT_OWNED_MORE_MENU */';
if (source.includes(marker)) {
  throw new Error('The React-owned More menu marker was already injected unexpectedly.');
}

const dropdown = '<div id="enterprise-more-navigation-menu" className="enterprise-more-dropdown">';
if (!source.includes(dropdown)) {
  throw new Error('The React-owned More menu injector could not locate the enterprise More dropdown.');
}
source = source.replace(
  dropdown,
  `<div id="enterprise-more-navigation-menu" className="enterprise-more-dropdown projectpulse-more-intuitive" data-projectpulse-react-owned-menu="true" data-more-label-source="module-registry" data-permission-evidence="loading">\n                  <div className="projectpulse-more-menu-tools">\n                    <div className="projectpulse-more-intuitive-heading">\n                      <strong>More pages</strong>\n                      <span>Open another page available to your current role or View-As identity.</span>\n                    </div>\n                    <label htmlFor="projectpulse-more-menu-search">Search pages</label>\n                    <div className="projectpulse-more-menu-search-row">\n                      <span aria-hidden="true">⌕</span>\n                      <input\n                        id="projectpulse-more-menu-search"\n                        type="search"\n                        autoComplete="off"\n                        placeholder="Search by page name"\n                        aria-label="Search available pages by name"\n                        onChange={(event) => window.ProjectPulseMoreNavigation?.filter(event.currentTarget.value)}\n                      />\n                      <button\n                        type="button"\n                        aria-label="Clear More menu search"\n                        onClick={(event) => {\n                          const input = event.currentTarget.parentElement?.querySelector('input');\n                          if (input) { input.value = ''; input.focus(); }\n                          window.ProjectPulseMoreNavigation?.filter('');\n                        }}\n                      >\n                        Clear\n                      </button>\n                    </div>\n                    <p className="projectpulse-more-menu-status" role="status">Pages remain hidden until dynamic RBAC permission evidence is verified.</p>\n                  </div>`
);

const link = `                          <a
                            href={item.href}
                            key={\`enterprise-more-\${group.name}-\${item.route}\`}
                            className={activeRoute === item.route ? 'active' : ''}
                            onClick={() => setIsTopMoreNavigationOpen(false)}
                          >
                            {getNavigationDisplayLabel(item)}
                          </a>`;
const reactOwnedLink = `                          <a
                            href={item.href}
                            key={\`enterprise-more-\${group.name}-\${item.route}\`}
                            className={activeRoute === item.route ? 'active' : ''}
                            data-page-name={getNavigationDisplayLabel(item)}
                            onClick={() => setIsTopMoreNavigationOpen(false)}
                          >
                            <strong className="projectpulse-more-intuitive-name">{getNavigationDisplayLabel(item)}</strong>
                            <span className="projectpulse-more-intuitive-arrow" aria-hidden="true">›</span>
                          </a>`;
if (!source.includes(link)) {
  throw new Error('The React-owned More menu injector could not locate the More page link template.');
}
source = source.replace(link, reactOwnedLink);

source = source.replace(
  '/* MODULE_001_GENERATOR_ALREADY_APPLIED - generated; do not edit */',
  `/* MODULE_001_GENERATOR_ALREADY_APPLIED - generated; do not edit */\n${marker}`
);

for (const required of [
  'data-projectpulse-react-owned-menu="true"',
  'data-more-label-source="module-registry"',
  'Search by page name',
  'window.ProjectPulseMoreNavigation?.filter',
  'projectpulse-more-intuitive-name',
  'projectpulse-more-intuitive-arrow',
  'data-page-name={getNavigationDisplayLabel(item)}',
  '<strong className="projectpulse-more-intuitive-name">{getNavigationDisplayLabel(item)}</strong>'
]) {
  if (!source.includes(required)) throw new Error(`React-owned More menu missing: ${required}`);
}

const internalModuleLabelExpression = `item.${'label'}`;
for (const forbidden of [
  `data-page-name={${internalModuleLabelExpression}}`,
  `<strong className="projectpulse-more-intuitive-name">{${internalModuleLabelExpression}}</strong>`
]) {
  if (source.includes(forbidden)) throw new Error(`React-owned More menu retained internal module label: ${forbidden}`);
}

fs.writeFileSync(generatedAppPath, source, 'utf8');

// The shared evaluator is the authorization source of truth. Prebuild may
// validate this contract, but must never rewrite role or View-As behavior.
const navigationPolicyPath = path.join(webRoot, 'src', 'module-navigation-access-policy.js');
if (!fs.existsSync(navigationPolicyPath)) {
  throw new Error('Shared module navigation access policy is missing.');
}
const navigationPolicy = fs.readFileSync(navigationPolicyPath, 'utf8');
for (const required of [
  'const actionCode = canonicalRoleCode(grant?.actionCode ?? grant?.ActionCode);',
  "if (!['MODULE_ACCESS', 'MODULE_VIEW'].includes(actionCode)) continue;",
  "effect === 'DENY'",
  'explicitDeniedModuleNumbers.add(moduleCode)'
]) {
  if (!navigationPolicy.includes(required)) {
    throw new Error(`Shared module navigation policy missing dual-action contract: ${required}`);
  }
}

console.log('PROJECTPULSE_REACT_OWNED_MORE_MENU=PASS runtimeChildReplacement=0 labels=module-registry navigationPolicy=module-access-plus-view sourceMutation=0');
