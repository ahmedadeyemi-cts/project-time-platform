# Deployment access — obtain from the project admin

This project deploys to a specific host. The access details live in
`.verity/deploy-access.md`, which is **intentionally gitignored** — it is never
committed, so no host or credential detail ever lands in git history.

**If you do not have `.verity/deploy-access.md` on your machine, request it from the
project admin: rpmcds12.** They maintain it and share it out-of-band (not through
this repo).

Maintainers: write or update the real file with `/verity:architect`, or by hand.
Keep it to credential *locations* (key files, SSO profiles, secret-store entries) —
never raw secrets.
