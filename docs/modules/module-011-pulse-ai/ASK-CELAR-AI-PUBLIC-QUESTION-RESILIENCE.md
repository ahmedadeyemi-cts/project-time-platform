# Ask Celar AI public-question resilience

Ask Celar AI classifies country officeholder questions such as `Who is the president of Jordan?` as public general knowledge rather than as an internal named-record question. Country names are derived from the .NET region catalog and combined with a closed set of common aliases. Questions containing Pulse, project, customer, employee, private-document, financial, or internal-record context remain inside the governed Pulse boundary.

The private Oracle Celar AI target receives only the public question and a concise public-answer instruction. It does not receive Pulse records, private documents, attachments, identities, tool results, customer/project context, financial values, or internal runtime evidence. Public generation is capped at 256 output tokens and the Help Assistant private target has a bounded timeout so the router can continue to Claude, OpenAI, and the governed local fallback instead of allowing the browser request to terminate as a supporting-service error.

A successful private or external public answer is represented through the same public-answer contract and records a sanitized Module 064 evidence source. If every governed target is unavailable, Ask Celar AI returns an evidence-limited HTTP-success response with its correlation ID and troubleshooting action instead of fabricating an answer or surfacing a generic service outage.

This source package also corrects the disposable Migration 084 PostgreSQL password contract and adds a narrow enterprise-validation mode that permits only the reviewed Migration 084 forward/rollback pair. It does not apply a migration, deploy an environment, enable automatic defects, change Oracle, or modify Production.
