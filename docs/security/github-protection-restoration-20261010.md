# GitHub protection restoration

Restore the main-branch and Test-environment controls required by the existing security posture baseline. The JSON specifies the two checks currently emitted by native CI, one approving review, CODEOWNER review, dismissal of stale reviews, last-push approval, conversation resolution, blocked force pushes/deletion, and enforcement for administrators. Test uses the previously documented reviewer, allows that reviewer to approve their own Test run, blocks administrator bypass, and accepts protected branches only.

This change does not activate a deployment workflow, approve a pending deployment, change application images, modify Production, rotate credentials, or close any historical security finding. Apply the configuration through the GitHub branch-protection and Test-environment APIs only after this repair has a PR record, then read both resources back and verify every specified control.

CodeQL requirements remain a separate follow-up until actual CodeQL checks are configured and observed. Independent review is mandatory after restoration; the single existing CODEOWNER cannot approve their own pull request. A second authorized reviewer/CODEOWNER is needed for owner-authored changes. Do not weaken protection to get around that requirement.
