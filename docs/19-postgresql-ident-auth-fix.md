# PostgreSQL Ident Authentication Fix

## Purpose

This document records the PostgreSQL application-user login issue encountered after creating the `project_time_platform` database and `ptp_app` role.

## Confirmed Working Items

The following steps completed successfully:

- PostgreSQL installed.
- PostgreSQL initialized.
- PostgreSQL service enabled and running.
- PostgreSQL version validated.
- Application role created: `ptp_app`.
- Application database created: `project_time_platform`.
- Application database owned by: `ptp_app`.
- Credentials stored locally in `/opt/project-time-platform/config/postgres.env`.

## Error Encountered

Application login test failed with:

```text
psql: error: FATAL: Ident authentication failed for user "ptp_app"
```

## Meaning

The database and user exist, but PostgreSQL is rejecting password-based login because the local TCP authentication rule is using `ident` authentication.

For this development VM, local application connections should use password authentication for `localhost`.

## Fix

Back up the PostgreSQL host-based authentication file:

```bash
sudo cp /var/lib/pgsql/data/pg_hba.conf /var/lib/pgsql/data/pg_hba.conf.bak.$(date +%Y%m%d%H%M%S)
```

Review current active rules:

```bash
sudo grep -nE '^(local|host)' /var/lib/pgsql/data/pg_hba.conf
```

Change local TCP rules from `ident` to `md5`:

```bash
sudo sed -i -E 's/^(host[[:space:]]+all[[:space:]]+all[[:space:]]+127\.0\.0\.1\/32[[:space:]]+)ident/\1md5/' /var/lib/pgsql/data/pg_hba.conf
sudo sed -i -E 's/^(host[[:space:]]+all[[:space:]]+all[[:space:]]+::1\/128[[:space:]]+)ident/\1md5/' /var/lib/pgsql/data/pg_hba.conf
```

Reload PostgreSQL:

```bash
sudo systemctl reload postgresql
```

Validate active rules:

```bash
sudo grep -nE '^(local|host)' /var/lib/pgsql/data/pg_hba.conf
```

Test login:

```bash
source /opt/project-time-platform/config/postgres.env

PGPASSWORD="$PTP_DB_PASSWORD" psql \
  -h localhost \
  -U "$PTP_DB_USER" \
  -d "$PTP_DB_NAME" \
  -c "SELECT current_database(), current_user, now();"
```

## Security Notes

- Do not expose PostgreSQL publicly.
- Do not open port `5432` in OCI.
- Do not open port `5432` in firewalld.
- Keep PostgreSQL bound to local/private access for now.
- Do not print or commit the password stored in `/opt/project-time-platform/config/postgres.env`.

## Expected Result

The login test should return:

```text
current_database     | current_user | now
project_time_platform | ptp_app      | <timestamp>
```
