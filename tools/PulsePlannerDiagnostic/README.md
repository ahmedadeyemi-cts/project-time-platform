# Bounded read-only planner incident diagnostic

Scope: the already-installed Protected Test source 26d34e5abad4a81f0158e4f40af53fcdaf4ef03b,
planner rows updated only from 2026-10-01 03:49 through 03:52 UTC, at most four.
The tool uses the API's existing scoped connection without revealing credentials.
It executes a database-enforced READ ONLY transaction, fixed SQL, 5-second query
limit and 20-second overall deadline. No command-line SQL, time range, identity or
target override is supported. It never changes schemas, accounts, plans, retries,
provider state or configuration. Output projects enums/keyword presence only;
raw documents, identifiers, row content and exception messages are not exported.

This is an incident-read tool, not an activation/deployment/controller bypass.
Its source must be reviewed in the PR before its ephemeral execution inside the
existing Test API. Test application routes and the existing local account remain
unchanged. Findings are not closed by this diagnostic.

The same fixed incident transaction also reads up to eight associated FlowHive
answer audit records, returning only enum diagnostics, provider labels, numeric
coverage and artifact-presence flags. No answer text or identity is selected.
