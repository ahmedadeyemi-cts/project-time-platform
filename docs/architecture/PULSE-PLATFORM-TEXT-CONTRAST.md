# Platform text contrast

## Reported regressions and shared cause

The dashboard's `Administrator workspace` label was painted with the enterprise page's muted text color inside a dark gradient. The permission matrix's column background was changed to navy by the enterprise table theme, while `.roles-matrix-role-heading strong` retained a hard-coded dark foreground. Other nested secondary labels used the same conflicting typography rules.

The repair is imported from `enterprise-contrast-guard.css`, which remains the last stylesheet imported by `main.jsx`. It uses the existing canonical page-header selectors and `CriticalRoutePresentationBoundary` header adoption instead of adding per-route DOM mutation or changing authorization.

## Surface contract

Branded route headers and the dashboard retain a dark background in both themes. Their local foreground and secondary-text aliases are light. The gradient's brightest stop and overlay are darkened slightly so secondary labels have contrast headroom rather than relying on opacity. Nested role names, role-code labels and pinned table columns receive matching foregrounds. Permission-cell supporting text inherits its status control's foreground without an opacity reduction.

An independently painted card, dialog, menu or form control resets the foreground context. A white action button must retain dark text; a blue action button must retain light text. New independent containers can use `data-contrast-surface="base"`; new inverse surfaces can opt into `data-contrast-surface="inverse"`. This is not a blanket white-text rule for every blue-looking class. The white navigation bar, authorization, data, AI routing and deployment behavior are not changed.

## Validation

`platform-text-contrast-ci.yml` runs the complete production frontend build and the browser regression script against the resulting CSS. The script exercises dashboard, canonical/adopted module headers, inset controls and cards, and matrix/shared table markup in Chromium and Firefox. It switches light/dark themes on the document root and body and checks enterprise/classic presentation.

The test samples the real rendered backdrop with only glyph paint hidden, preserving background gradients, layout and currentColor. It composites text alpha and ancestor opacity and requires at least 4.5:1 without rounding the threshold down. Normal screenshots, measurements and a stylesheet inventory are retained as CI evidence. Browser dependencies are isolated from the application package/lockfile. No live API, AI provider or signed-in customer data is accessed.

These are shared-component fixtures with production CSS, not an assertion that every authenticated role, route or interaction has been manually reviewed. Before deployment, review the dashboard workspace label, scroll all matrix role columns, and inspect module headers, forms, dialogs, status cells, selected controls and hover/focus states in both themes using representative roles. Keep this PR unmerged and undeployed until the owner is ready.

Contrast reference: https://www.w3.org/WAI/WCAG22/Understanding/contrast-minimum.html
