# Module 025 standard download formats

Standard GSD uses a sanitized copy of the supplied 12-sheet General Services
Delivery workbook. Summary C11:C16 map customer, project, contract (FP/T&M), AE,
SA and SAA from saved engagement fields. Each delivery phase receives its saved
activities and technical tasks. Reviewed phase hours appear once, with formulas
linking phase totals to the summary and phase breakdown. Fractional hours remain
exact, and task lists expand beyond the original 96-row capacity.

The current persisted contract contains phase hours, not reviewed task hours,
engineering roles, rates, overtime, travel or reserve allocations. Those fields
stay blank. No sample customer contacts, prices, SKUs, automatic management
surcharges or assumed commercial rates are inherited. These missing inputs must
be captured and reviewed before this export can serve as a priced task estimate.
The supplied workbook's layout is retained; commercial calculations are withheld
where the application has no authoritative input. Existing HAEA exports retain
their separate profile.

SOW Word downloads embed the existing US Signal logo in a repeating letterhead,
use consistent black Arial text and headings, and include page-numbered footers.
Saved scope and authorization rules are unchanged.

Formatting uses saved data and makes no model calls. Retained immutable versions
remain byte-for-byte unchanged; new export formatting applies when creating new
artifacts, including newly confirmed versions. Downloading an already retained
version will still return its original format.

Validation: standalone offline C# export tests cover metadata, both contract
mappings, unknown contacts, formula-looking text, fractional totals, long task
lists, template data removal, HAEA isolation and DOCX package branding. The test
project links the real exporter code and embedded assets, without API or model
clients. Rendered synthetic documents are used for local visual review.

Deployment hold: do not merge automatically. Application changes under
Module025 can trigger Protected UAT generation on main. This PR prepares exports
only; the user's no-generation hold still applies.
