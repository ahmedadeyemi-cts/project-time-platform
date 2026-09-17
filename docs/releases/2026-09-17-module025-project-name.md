# Module 025 project name and document identity

Module 025 now stores a user-authoritative Project Name on each SOW/GSD engagement. Existing records remain valid with an empty project name and can be updated after reopening.

The Project Name is exposed in the authoring queue, editor, search, SOW Register, generated SOW metadata and GSD summary. Confirmed retained versions freeze the value through the existing version source JSON, so SELL processing receives the exact Project Name associated with the submitted version rather than reading a later mutable value.

Document download names use the immutable SOW number and sanitized Project Name:

- `SOW#2026-000123_CUCM_14_to_15_Upgrade_SOW.docx`
- `SOW#2026-000123_CUCM_14_to_15_Upgrade_GSD.xlsx`

The same names are carried by the SELL package for future document publication. Project Name is not AI-owned and is never overwritten by detailed-scope generation.

Migration 109 adds the project_name column and search index. The Protected Test migration runner applies and verifies it. Production is not changed by this PR.
