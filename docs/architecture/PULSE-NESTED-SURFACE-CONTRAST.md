# Nested surface contrast contract

## Why the prior fixes missed these cases

The inverse header theme correctly painted ordinary header copy white, but some named components inside those headers painted an independent light card. On-Call Scheduling (071), OneAssist (072), Sales Coverage (073), and OEM & Vendor Directory (074) authority cards were not registered as independent surfaces. On-Call's resource links also had a light background while the classless-link rule supplied white text.

The wider source audit found the same ownership mismatch in status notices, footers, dialogs, menu panels, empty states and support text. Legacy components additionally depended on root-owned dark-theme variables that were absent when the supported theme state was owned by `body`.

## Implementation

`enterprise-owned-surface-contrast.css` is loaded after the existing application, inverse-surface and shared-header action contracts. It contains an explicit, reviewed selector inventory. It does not infer semantics from arbitrary class substrings or change application state.

Each independent surface owns its background, text, supporting text and legacy aliases. A descendant resolves the nearest surface's tokens. White or dark controls retain their own `currentColor` boundary. A base card inside a navy header therefore uses the base theme, while a semantic badge inside that card can still own its success, warning, information or error palette.

New components should declare `data-contrast-surface="base"`, `"info"`, `"success"`, `"warning"`, or `"danger"` when they paint an independent background. Ordinary header text should continue inheriting the header's inverse palette. Add the real component structure and its relevant state variants to coverage rather than adding a generic white-text override.

The legacy `body[data-theme='dark']` variable bridge mirrors the existing dark values from `styles.css` and `account-center.css`. Keep these paired when changing those source palettes. The bridge does not rewrite storage, normalize settings, grant access or change routing.

Disabled attributes and archived-state labels remain intact. Text is not dimmed by an ancestor opacity; archived task cards retain a dashed outline. The original semantic distinctions between information, success, warning and error states remain separate.

## Source-derived browser coverage

`generate-module-surface-fixtures.cjs` parses every checked-in JSX file with a test-only TypeScript parser. At initial implementation the inventory contains 194 JSX files and produces 918 distinct fixtures from 178 files. Native headers, footers, notices and their nested contents are collected; components without such a surface receive native-root fallback coverage where possible.

The generator preserves native ancestors, classless wrappers, literal class branches, parameter defaults and relevant sibling layout slots. It records substitutions for unknown values, omitted custom components and layout placeholders. It adds the actual Module 025 phase/notice state classes rather than treating an unresolved dynamic class prefix as a completed state. Mutually exclusive More/profile overlays are inspected independently.

Only the reviewed DOM-only route-header adoption declarations are executed. The adapter is SHA-256 pinned, and a source change requires renewed review. No React application code, business event handlers, API response mocks, credentials or AI providers are used. Links are inert fixture links and all outbound requests are blocked.

`validate-module-surface-contrast.py` reads the stylesheets actually linked by the complete production build. It measures effective text fill, foreground alpha and ancestor opacity against six screenshot samples of the underlying painted surface. Text paint is temporarily masked with inline-important text-fill declarations so that high-specificity production rules cannot defeat the masking. Original inline styles are restored exactly. Self-tests cover reference contrast, white-on-white failure, alpha/opacity, hidden-text accounting and important-style masking/restoration.

The acceptance threshold is 4.5:1 for every measured text node. Empty fixture selection or zero measured text is an error. Native details are expanded for inspection. Module 025 subcomponents receive their actual workspace palette parent. Each report includes source and stylesheet hashes, substitutions, failed checks and text that could not be measured.

The read-only CI matrix runs Chromium and Firefox across table, enterprise and classic presentations, light/dark modes, and root/body theme ownership. It also reruns the two user-reported modules at an 800-pixel viewport. The existing Platform Text Contrast CI and its header action-state checks remain separate and unchanged.

## What passing does and does not establish

Passing establishes contrast for the measured, source-derived rendering cases under the complete built CSS. It is not proof that every live account, dynamic component state, image/logo, input value, placeholder, responsive breakpoint or workflow has been manually reviewed. Custom components are not flattened into false native markup; omitted/custom-only files remain in the inventory. Hidden, clipped or outside-viewport text is reported as unmeasured, never silently counted as a passing check.

The existing authenticated Protected UAT acceptance process separately checks deployment identity, health, the PM's assigned-role story and saved SOW/GSD exports. That release acceptance is not an AI-generation or all-role live test. The original contrast regression suites and all existing release controls must also pass before deployment.

## Local execution

Use isolated test dependencies: TypeScript 5.8.3, Playwright 1.55.0 and Pillow 11.3.0. Build the complete frontend first. From `src/frontend/project-time-web`:

```sh
node scripts/generate-module-surface-fixtures.cjs "$PWD" /tmp/pulse-surfaces.json
python scripts/validate-module-surface-contrast.py \
  --fixtures /tmp/pulse-surfaces.json \
  --output /tmp/pulse-surface-evidence
```

Set `SURFACE_TYPESCRIPT_MODULE` to the isolated parser's `lib/typescript.js` when it is not on Node's module path. Diagnostic `--css` and `--extra-css` options are local-only conveniences; CI uses the actual `dist/index.html` stylesheet links without an overlay. Fixtures and evidence are written outside the application source tree.

## Release boundary

This change is presentation CSS, its import, source-derived test tooling, documentation and read-only CI only. Do not modify role grants, application behavior, database schema, API contracts, Module 064 provider configuration, deployment-controller logic, admission protections, rollback guarantees or Production as part of this correction. Protected UAT deployment remains owned by the existing serialized supervisor.
