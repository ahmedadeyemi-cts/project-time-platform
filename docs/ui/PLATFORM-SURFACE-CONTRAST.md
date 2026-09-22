# Platform light/dark surface contrast

## Defect and scope

The dashboard workspace label and role-matrix headings were using ordinary page
foregrounds on branded navy backgrounds. The enterprise typography rules target
nested `small`, `p`, and headings separately, so setting `color: white` only on a
header does not fix its descendants. The matrix also defines literal dark role
names while the enterprise table rules replace the cell background with navy.
The dark theme changes the link-blue token to a light blue; using that same token
as the background of a white-label primary button creates another contrast loss.

The shared contrast guard now imports `enterprise/surface-contrast.css` after the
canonical application theme. It supplies paired inverse foreground/background
colors to canonical/adopted module headers and all application table headers.
This is a shared module contract, not a dashboard-only patch. Long matrix role
codes wrap instead of disappearing behind ellipses. Dashboard nested rows,
permission-matrix panels and hover rows use the captured page palette in both
modes. Permission decision colors and smaller scope labels stay distinct.

Inputs, normal nested cards, buttons, and status badges are separate foreground
boundaries. They must not inherit white text solely because a parent header is
navy. Buttons and badges retain their own foreground through nested labels;
normal surfaces restore the page palette. No authorization, role data, provider
routing, persistence, or deployment controller is changed.

## Component contract

Use the existing canonical page-header classes or the existing adopted
`data-enterprise-page-header="true"` attribute. A new explicitly inverse surface
may use `data-contrast-surface="inverse"`; a normal surface inside it may use
`data-contrast-surface="surface"`. Use foreground/background pairs when adding a
new status variant. Do not use a light-mode muted literal on a branded header,
or reuse the dark-mode link token as a white-label button background.

The compatibility aliases are local to the surface. The page palette is captured
before those aliases are rebound. Do not replace this with a global wildcard
that paints every descendant white, and do not lower opacity on an entire
container to style secondary text.

## Automated validation

From `src/frontend/project-time-web`:

```sh
npm ci
npm run build
node scripts/validate-theme-contrast.mjs
```

Node 22 and Chromium/Chrome are required. Set `CHROME_BIN` when the executable is
not on PATH. The PR-only workflow performs the complete existing frontend build,
then renders the actual bundled CSS in a synthetic, non-authenticated fixture.
It does not call application APIs, change an environment, or deploy anything.

The browser checks rendered foreground/background contrast for the reported
workspace label, twelve role-heading/code pairs, shared module banners and table
headers, nested controls, placeholders, normal cards, permission states, and
primary/secondary/disabled labels. Both classic and enterprise presentation are
checked while switching light -> dark -> light without reloading. Normal, hover,
and focus-visible CSS states are exercised. The threshold is 4.5:1 even for the
fixture's larger headings. Transparency and all gradient-stop combinations are
considered conservatively. Unhandled image backgrounds or container opacity are
reported for review, never silently marked as passing.

Evidence includes a JSON report, stylesheet inventory, bundle hashes, a rendered
fixture, and light/dark screenshots. The source inventory counts declarations; it
is not a claim that every authenticated page or every UI state was visually
exercised. `--css FILE` is available for a limited source reproduction and labels
its report accordingly; the CI default always uses the production bundle.

## Review before release

Review the built screenshots and the PR checks. In an authorized UAT session,
check the dashboard and permissions matrix first, including horizontal scrolling
and long role names. Then inspect representative SOW/GSD, FlowHive, work register,
engineer/PM, administration, provider, navigation, dialog, and notification views
in both themes. Include narrow screens and browser high-contrast settings.
Authenticated route-by-route visual sign-off remains separate from the automated
surface fixture. Keep this PR unmerged and undeployed until the owner approves.
