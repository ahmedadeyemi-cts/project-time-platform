# AZ-09B1 — acme.sh Install Working-Directory Resume Ready

Date: 2026-07-13

## Observed result

The West custom-domain workflow successfully completed the following before stopping:

- Cloudflare token and zone validation passed.
- `phd-west-test.onenecklab.com` A record was created.
- Public DNS resolved the custom domain to `20.118.180.129`.
- Application Gateway remained `Succeeded` on WAF_v2.

The workflow stopped before certificate issuance with:

`cp: cannot stat 'acme.sh': No such file or directory`

`Installation failed, cannot copy acme.sh`

## Cause

The cloned `acme.sh` installer was invoked by absolute path while the shell remained outside the cloned repository. The official git-clone installation sequence changes into the cloned `acme.sh` directory before running `./acme.sh --install`.

## Correction

`deployment/azure/scripts/az09b1-acme-install-working-directory-resume.sh` downloads the canonical AZ-09B script, applies one guarded replacement so installation runs from inside the cloned source directory, validates the corrected script with `bash -n`, and resumes the TLS workflow.

## Safety

- Existing Cloudflare DNS A record is reused and updated idempotently.
- No certificate was issued before the failure.
- No Key Vault certificate was imported before the failure.
- No Application Gateway HTTPS listener was created before the failure.
- No image rebuild, Container App redeployment, database modification, or East PostgreSQL replica creation occurs.
- Oracle VM is not required and may remain stopped.

## Expected result

`WEST_CUSTOM_DOMAIN_TLS_RESULT=READY`
