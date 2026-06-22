# .NET Backend Setup Runbook

## 1. Purpose

This runbook documents the setup of the first ASP.NET Core backend for the Project Time Platform.

## 2. Version Decision

The backend will target:

```text
.NET 10
```

Reason: .NET 10 is the current Long Term Support release and provides a longer support window than .NET 8 for this project timeline.

## 3. Install .NET SDK

On the OCI Oracle Linux 9 development VM, try the native package first:

```bash
sudo dnf install -y dotnet-sdk-10.0
```

Validate:

```bash
dotnet --info
dotnet --list-sdks
dotnet --list-runtimes
```

## 4. If the Package Is Not Available

If `dotnet-sdk-10.0` is not available from the enabled repositories, check available packages:

```bash
sudo dnf search dotnet-sdk
sudo dnf list available 'dotnet-sdk*'
```

Do not install preview SDKs for this project unless explicitly approved.

## 5. Backend Project Location

The backend project is located at:

```text
src/backend/ProjectTime.Api
```

## 6. Build Backend

Run:

```bash
cd /opt/project-time-platform/app/project-time-platform/src/backend/ProjectTime.Api
dotnet restore
dotnet build
```

## 7. Run Backend Locally

Run:

```bash
cd /opt/project-time-platform/app/project-time-platform/src/backend/ProjectTime.Api
dotnet run --urls http://127.0.0.1:5080
```

In another SSH session, test:

```bash
curl http://127.0.0.1:5080/health
curl http://127.0.0.1:5080/api/version
curl http://127.0.0.1:5080/api/db-config-check
```

## 8. Database Environment File

The backend will read database environment values from:

```text
/opt/project-time-platform/config/postgres.env
```

This file must stay outside the Git repository.

## 9. Security Notes

- Do not commit database passwords.
- Do not expose the development API publicly until authentication and reverse proxy controls are in place.
- Keep the first API test bound to `127.0.0.1`.
- Do not open a public firewall rule for the backend test port.

## 10. Next Step

After the API health endpoints work locally:

1. Add PostgreSQL connectivity package.
2. Add a database connectivity health check.
3. Add service user configuration.
4. Add systemd service definition.
5. Add reverse proxy configuration.
