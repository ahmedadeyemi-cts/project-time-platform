(() => {
  const result =
    window.__projectPulse056BDashboardCardRouteGuardDiagnostics?.();

  const modulePattern =
    /\bmodule\s+0?(?:22|23|24|25|26|27|28|29|30)\b/i;

  const known = [
    "Production Notification Center",
    "Production Readiness Center",
    "Sales-to-Delivery Intake Foundation",
    "SOW Generator + Claude Research Review",
    "CRM Integration Framework",
    "Signed SOW Handoff + Assignment Trigger",
    "SOW-Aware AI Time Entry Generator",
    "User Acceptance / Role + Workflow Validation Center",
    "Reporting / Accounting / Invoicing / Analytics Command Center"
  ];

  const headings = [
    ...document.querySelectorAll("h1,h2,h3,h4,h5,h6,[role='heading']")
  ];

  const visibleLegacyDashboardHeadings = headings
    .filter((heading) => {
      const text = heading.textContent.replace(/\s+/g, " ").trim();

      return modulePattern.test(text) ||
        known.some((title) => text.includes(title));
    })
    .filter((heading) => {
      if (heading.closest("#root, .app-shell")) {
        return false;
      }

      const card =
        heading.closest(
          "[data-projectpulse-legacy-dashboard-summary-card='true'], [data-projectpulse-dashboard-only-card='true']"
        ) ||
        heading;

      const style = getComputedStyle(card);

      return !card.hidden &&
        style.display !== "none" &&
        style.visibility !== "hidden";
    })
    .map((heading) =>
      heading.textContent.replace(/\s+/g, " ").trim()
    );

  return {
    hash: location.hash,
    route: document.documentElement.getAttribute(
      "data-projectpulse-056b-route"
    ),
    guardVersion: document.documentElement.getAttribute(
      "data-projectpulse-056b-guard-version"
    ),
    markedCount: document.documentElement.getAttribute(
      "data-projectpulse-056b-marked-count"
    ),
    visibleOffenderCount: document.documentElement.getAttribute(
      "data-projectpulse-056b-visible-offender-count"
    ),
    runtimeResult: result,
    visibleLegacyDashboardHeadings
  };
})()
