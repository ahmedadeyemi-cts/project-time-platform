# Repository Clone and PostgreSQL Setup Runbook

## 1. Purpose

This runbook documents the next setup stage after the OCI VM baseline validation: cloning the private GitHub repository and installing PostgreSQL.

## 2. Current Decision

Because the OCI Free Tier VM is memory constrained, PostgreSQL will be installed natively on Oracle Linux for the initial development environment.

The end-state architecture may still use containers where appropriate, but native PostgreSQL reduces overhead on the current small VM.

## 3. Clone the Private Repository

Run:

```bash
cd /opt/project-time-platform/app

GIT_SSH_COMMAND='ssh -i ~/.ssh/github_project_time_platform -o IdentitiesOnly=yes' \
git clone git@github.com:ahmedadeyemi-cts/project-time-platform.git
```

Validate:

```bash
cd /opt/project-time-platform/app/project-time-platform
git remote -v
git status
ls -la
```

## 4. Install PostgreSQL Packages

Run:

```bash
sudo dnf install -y postgresql postgresql-server postgresql-contrib
```

Validate package availability:

```bash
rpm -qa | grep -i postgresql | sort
```

## 5. Initialize PostgreSQL

Run:

```bash
sudo postgresql-setup --initdb
```

If that command is not available, locate it:

```bash
sudo find /usr -name 'postgresql-setup' -type f 2>/dev/null
```

## 6. Enable and Start PostgreSQL

Run:

```bash
sudo systemctl enable --now postgresql
sudo systemctl status postgresql --no-pager
```

Validate PostgreSQL responds:

```bash
sudo -u postgres psql -c "SELECT version();"
```

## 7. Create Application Database and User

Generate a password and store it locally on the VM:

```bash
APP_DB_PASS=$(openssl rand -base64 32)
mkdir -p /opt/project-time-platform/config
cat > /opt/project-time-platform/config/postgres.env <<EOF
PTP_DB_NAME=project_time_platform
PTP_DB_USER=ptp_app
PTP_DB_PASSWORD=${APP_DB_PASS}
PTP_DB_HOST=localhost
PTP_DB_PORT=5432
EOF
chmod 600 /opt/project-time-platform/config/postgres.env
```

Create the database and role:

```bash
source /opt/project-time-platform/config/postgres.env
sudo -u postgres psql <<SQL
CREATE ROLE ${PTP_DB_USER} WITH LOGIN PASSWORD '${PTP_DB_PASSWORD}';
CREATE DATABASE ${PTP_DB_NAME} OWNER ${PTP_DB_USER};
GRANT ALL PRIVILEGES ON DATABASE ${PTP_DB_NAME} TO ${PTP_DB_USER};
SQL
```

Validate:

```bash
sudo -u postgres psql -c "\du"
sudo -u postgres psql -c "\l"
```

## 8. Connectivity Test

Run:

```bash
source /opt/project-time-platform/config/postgres.env
PGPASSWORD="$PTP_DB_PASSWORD" psql -h localhost -U "$PTP_DB_USER" -d "$PTP_DB_NAME" -c "SELECT current_database(), current_user, now();"
```

## 9. Security Notes

- Do not expose PostgreSQL publicly.
- Do not open port 5432 in the OCI security list.
- Do not open port 5432 in firewalld.
- Store database credentials only in protected local config files or a future secret manager.
- Do not commit `/opt/project-time-platform/config/postgres.env` to GitHub.

## 10. Firewall Reminder

Only SSH is currently required externally.

PostgreSQL should remain local-only for now.

## 11. Next Step

After PostgreSQL is installed and validated, continue with:

1. Create initial SQL migration files.
2. Create backend .NET solution skeleton.
3. Create frontend React skeleton.
4. Configure the backend connection string using local environment variables.
