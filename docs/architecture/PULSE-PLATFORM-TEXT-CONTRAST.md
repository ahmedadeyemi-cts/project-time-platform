# Platform text contrast

## Reported regressions and shared cause

The dashboard's `Administrator workspace` label was painted with the enterprise page's muted text color inside a dark gradient. The permission matrix's column background was changed to navy by the enterprise table theme, while `.roles-matrix-role-heading strong` retained a hard-coded dark foreground. Other nested secondary labels used the same conflicting typography rules.

The repair is imported from `enterprise-contrast-guard.css`, which remains the last stylesheet imported by `main.jsx`. It uses the existing canonical page-header selectors and `CriticalRoutePresentationBoundary` header adoption instead of adding runtime DOM recoloring or changing authorization.

## Source review and surface contract

The source review covers the shared theme, typography, table and control layers plus a compiled stylesheet inventory. The initial production build reported 193 source stylesheets. Its CSS was inspected for painted header/hero/banner/control surfaces; 134 candidate rules were reviewed to distinguish genuinely inverse surfaces from light headers, normal page cards, selected controls and decorative previews.

Canonical/adopted route headers, the dashboard and an explicit inventory of legacy inverse headers now share the accessible branded gradient and light foreground contract in both themes. The inventory includes FlowHive, Group 2-5 workspaces, closeout, capacity forecasting, defect/governance workspaces, AI workspaces, the module directory and drawer headers. The contextual assistant's inverse header is scoped to enterprise presentation; its classic header uses paired base-theme colors. The active view-as warning retains a darker orange gradient.

The gradient's brightest stop and overlay are darkened slightly. Secondary labels use opaque light text rather than low-contrast opacity. Nested role names, role-code labels and pinned table columns receive paired foreground/background colors. Pinned matrix body cells switch with the theme; allow/unset states retain semantic palettes and supporting permission text inherits the permission control's foreground without opacity reduction.

An independently painted card, dialog, menu or form control resets its foreground context. A white action button retains dark text, and a blue action button retains light text. Disabled attributes and behavior are unchanged; disabled controls use an opaque neutral palette instead of compounded opacity. Shared enterprise primary actions and selected navigation/theme controls use a darker blue in dark mode rather than white text on a light-blue link color. Explicit destructive action variants are excluded from that primary-action override.

Dashboard card labels, links and inset summaries use theme-aware colors instead of fixed dark gray/teal. Body-owned legacy theme state gets paired enterprise table/control surfaces too. New independent containers can use `data-contrast-surface="base"`; new inverse surfaces can opt into `data-contrast-surface="inverse"`. Do not infer the surface type from arbitrary class-name substrings. Navigation structure, authorization, data, AI routing and deployment behavior are not changed.

## Validation

`platform-text-contrast-ci.yml` runs the complete production frontend build and browser regressions against the resulting CSS. The fixtures exercise dashboard text and cards; canonical/adopted module headers; inset controls/cards; 24 additional inverse header classes; the contextual assistant header; matrix/shared/generic tables; permission supporting text; and shared primary, secondary, disabled and selected navigation/theme/tab controls in Chromium and Firefox. Each browser switches enterprise/classic presentation, light/dark themes, and root/body-owned theme state.

The test samples the real rendered backdrop with only glyph paint hidden, preserving gradients, layout and currentColor. It composites text alpha and ancestor opacity and requires at least 4.5:1 without rounding the threshold down. Screenshots, measurements and a stylesheet inventory are retained as CI evidence. Browser dependencies are isolated from the application package/lockfile. No live API, AI provider or signed-in customer data is accessed.

These are shared-component fixtures with production CSS, not an assertion that every authenticated role, route, overlay or interaction has been manually reviewed. Before deployment, review the dashboard workspace label, scroll all matrix role columns, and inspect module headers, forms, dialogs, status cells, selected controls and hover/focus states in both themes using representative roles. Keep this PR unmerged and undeployed until the owner is ready.

Contrast reference: https://www.w3.org/WAI/WCAG22/Understanding/contrast-minimum.html
