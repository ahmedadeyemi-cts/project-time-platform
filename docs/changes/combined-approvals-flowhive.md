# Combined approval and FlowHive candidate

PR #1213 consolidates approval routing and bulk review with PR #1212's customer sharing, WBS save normalization, multiple assignees, project contacts, CPM display, and unsent meeting drafts. The combined source includes main at `5c371854343cc8f5c26f2ae26cc30c904edc35ea` and PR #1212 at `7edee9b6917f01dee7e7fcbc3424823fd35ac26b`.

The source branches merge without text conflicts. Their two migration 131 files required reconciliation: project collaboration retains 131; time approval routing is now 132. All approval references and tests use 132. Both migrations are included in the existing immutable private-network package with individual checksum verification, after migration 130 and before API rollout. The initialization inventory keeps both at `review_required`; this does not approve or execute a release.

Pulse's current navigation, page shell, and theme remain in place. The approval section uses Pulse card/text variables; FlowHive additions remain scoped to its existing module and theme variables. The illustrative mockup is not a replacement application shell or proof of deployed appearance.

Authorization boundaries remain independent: external project contacts do not become application users or time approvers. Meeting drafts remain unsent; no calendar invitations or external notifications are introduced. PTC delegation, self-approval restrictions, View-As restrictions, stage ordering, and stale-selection protection remain as documented in `approval-routing-bulk-review.md`.

Combined validation covers production approval SQL, contract balances, bulk-review component interactions, FlowHive request and assignment behavior, actual C# handlers, full frontend build, migration package ordering/tamper rejection, and initialization inventory. The focused GitHub workflows rerun on the combined head, including FlowHive's isolated PostgreSQL and synthetic browser suites. Previously failed repository-wide admission/scope checks and authenticated live inspection remain release gates; no protections are bypassed and no deployment is performed as part of consolidation.
