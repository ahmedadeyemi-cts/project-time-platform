(() => {
  const result =
    window.__projectPulse056BDashboardCardRouteGuardDiagnostics?.();

  const known = [
    "Production Notification Center",
    "SOW Generator + Claude Research Review",
    "Sales-to-Delivery Intake Foundation",
    "CRM Integration Framework",
    "Signed SOW Handoff + Assignment Trigger"
  ];

  const headings = [
    ...document.querySelectorAll("h1,h2,h3,h4,h5,h6,[role='heading']")
  ];

  const visibleKnownDashboardHeadings = headings
    .filter((heading) =>
      known.some((title) =>
        heading.textContent.replace(/\s+/g, " ").trim().includes(title)
      )
    )
    .filter((heading) => {
      const card =
        heading.closest("[data-projectpulse-dashboard-only-card='true']") ||
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
    visibleKnownDashboardHeadings
  };
})()
