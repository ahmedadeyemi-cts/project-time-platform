#!/usr/bin/env bash
set -euo pipefail

REPO_DIR="/opt/project-time-platform/app/project-time-platform"
SCHEDULER="$REPO_DIR/deployment/rocky-linux/project-pulse-reminder-scheduler.sh"

if [ ! -f "$SCHEDULER" ]; then
  echo "ERROR: Missing $SCHEDULER"
  exit 1
fi

chmod +x "$SCHEDULER"

sudo tee /etc/systemd/system/project-pulse-weekly-engineer-reminder.service >/dev/null <<EOF
[Unit]
Description=Project Pulse Weekly Engineer Reminder Queue
After=projecttime-api.service
Requires=projecttime-api.service

[Service]
Type=oneshot
User=opc
WorkingDirectory=$REPO_DIR
ExecStart=$SCHEDULER weekly-engineer
EOF

sudo tee /etc/systemd/system/project-pulse-weekly-engineer-reminder.timer >/dev/null <<EOF
[Unit]
Description=Run Project Pulse weekly engineer reminder every Friday

[Timer]
OnCalendar=Fri *-*-* 09:00:00
Persistent=true
Unit=project-pulse-weekly-engineer-reminder.service

[Install]
WantedBy=timers.target
EOF

sudo tee /etc/systemd/system/project-pulse-month-end-pm-reminder.service >/dev/null <<EOF
[Unit]
Description=Project Pulse Month-End PM Reminder Queue
After=projecttime-api.service
Requires=projecttime-api.service

[Service]
Type=oneshot
User=opc
WorkingDirectory=$REPO_DIR
ExecStart=$SCHEDULER month-end-pm
EOF

sudo tee /etc/systemd/system/project-pulse-month-end-pm-reminder.timer >/dev/null <<EOF
[Unit]
Description=Run Project Pulse month-end PM reminder check every Friday

[Timer]
OnCalendar=Fri *-*-* 09:05:00
Persistent=true
Unit=project-pulse-month-end-pm-reminder.service

[Install]
WantedBy=timers.target
EOF

sudo systemctl daemon-reload
sudo systemctl enable --now project-pulse-weekly-engineer-reminder.timer
sudo systemctl enable --now project-pulse-month-end-pm-reminder.timer

systemctl list-timers --all | grep 'project-pulse-.*reminder' || true

echo "==> Reminder timers installed. Weekly engineer reminders run Fridays at 09:00. Month-end PM reminder check runs Fridays at 09:05 and only queues on the last Friday."
