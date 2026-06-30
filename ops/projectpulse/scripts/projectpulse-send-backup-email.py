#!/usr/bin/env python3
import json
import os
import smtplib
import sys
import urllib.error
import urllib.request
from email.message import EmailMessage

if len(sys.argv) != 5:
    print("Usage: projectpulse-send-backup-email.py <subject> <to> <cc> <body_file>", file=sys.stderr)
    sys.exit(2)

subject, to_recipients, cc_recipients, body_file = sys.argv[1:]

def split_recipients(value):
    return [item.strip() for item in (value or "").replace(";", ",").split(",") if item.strip()]

to_list = split_recipients(to_recipients)
cc_list = split_recipients(cc_recipients)

if not to_list:
    print("No email recipients provided.", file=sys.stderr)
    sys.exit(12)

with open(body_file, "r", encoding="utf-8", errors="replace") as handle:
    body = handle.read()

brevo_enabled = os.environ.get("PROJECTPULSE_BACKUP_BREVO_ENABLED", "false").lower() == "true"
brevo_key = os.environ.get("PROJECTPULSE_BACKUP_BREVO_API_KEY", "").strip()
brevo_from_email = os.environ.get("PROJECTPULSE_BACKUP_BREVO_FROM_EMAIL", "").strip()
brevo_from_name = os.environ.get("PROJECTPULSE_BACKUP_BREVO_FROM_NAME", "ProjectPulse Backup").strip()

if brevo_enabled:
    if not brevo_key:
        print("Brevo API key is not configured.", file=sys.stderr)
        sys.exit(20)

    if not brevo_from_email:
        print("Brevo sender email is not configured.", file=sys.stderr)
        sys.exit(21)

    payload = {
        "sender": {
            "email": brevo_from_email,
            "name": brevo_from_name or "ProjectPulse Backup"
        },
        "to": [{"email": email} for email in to_list],
        "subject": subject,
        "textContent": body,
        "htmlContent": "<html><body><pre style=\"font-family:Arial, sans-serif; white-space:pre-wrap;\">" + body.replace("&", "&amp;").replace("<", "&lt;").replace(">", "&gt;") + "</pre></body></html>"
    }

    if cc_list:
        payload["cc"] = [{"email": email} for email in cc_list]

    request = urllib.request.Request(
        "https://api.brevo.com/v3/smtp/email",
        data=json.dumps(payload).encode("utf-8"),
        headers={
            "accept": "application/json",
            "api-key": brevo_key,
            "content-type": "application/json"
        },
        method="POST"
    )

    try:
        with urllib.request.urlopen(request, timeout=30) as response:
            response_body = response.read().decode("utf-8", errors="replace")
            print(f"Brevo email sent. HTTP {response.status}. Response: {response_body}")
            sys.exit(0)
    except urllib.error.HTTPError as error:
        error_body = error.read().decode("utf-8", errors="replace")
        print(f"Brevo email failed. HTTP {error.code}. Response: {error_body}", file=sys.stderr)
        sys.exit(22)
    except Exception as error:
        print(f"Brevo email failed: {error}", file=sys.stderr)
        sys.exit(23)

# Optional fallback to SMTP if Brevo is disabled.
host = os.environ.get("PROJECTPULSE_BACKUP_SMTP_HOST", "").strip()
port = int(os.environ.get("PROJECTPULSE_BACKUP_SMTP_PORT", "587"))
use_tls = os.environ.get("PROJECTPULSE_BACKUP_SMTP_USE_TLS", "true").lower() == "true"
sender = os.environ.get("PROJECTPULSE_BACKUP_SMTP_FROM", "").strip()
username = os.environ.get("PROJECTPULSE_BACKUP_SMTP_USERNAME", "").strip()
password = os.environ.get("PROJECTPULSE_BACKUP_SMTP_PASSWORD", "").strip()

if not host:
    print("Brevo disabled and SMTP host is not configured.", file=sys.stderr)
    sys.exit(10)

if not sender:
    print("SMTP sender/from address is not configured.", file=sys.stderr)
    sys.exit(11)

message = EmailMessage()
message["From"] = sender
message["To"] = ", ".join(to_list)
if cc_list:
    message["Cc"] = ", ".join(cc_list)
message["Subject"] = subject
message.set_content(body)

with smtplib.SMTP(host, port, timeout=30) as smtp:
    smtp.ehlo()
    if use_tls:
        smtp.starttls()
        smtp.ehlo()
    if username:
        smtp.login(username, password)
    smtp.send_message(message, to_addrs=to_list + cc_list)

print(f"SMTP email sent to {', '.join(to_list + cc_list)}")
