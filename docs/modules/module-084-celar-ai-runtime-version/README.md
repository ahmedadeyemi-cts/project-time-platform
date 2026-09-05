# Module 084 — Celar AI Runtime & Version Center

## Purpose

Module 084 is the administrator-only Pulse control and visibility surface for the private Oracle Celar AI runtime. It owns runtime-version visibility and governed maintenance scheduling; it does **not** own AI provider ordering, provider credentials, or external-provider model selection.

Module 064 remains the authority for the platform provider route:

**DeepSeek v4 → Celar AI → Claude → OpenAI → governed local template**

Module 084 manages only the private runtime that sits inside the Celar AI target.

## Initial runtime portfolio

| Component | Governed role |
|---|---|
| Celar gateway | Private HTTPS inference/document-processing boundary |
| Ollama | Local model execution engine |
| `gemma3:4b` | Structured/private compatibility specialist |
| `qwen3:4b-instruct` | General reasoning/coding/tool-use specialist |
| `llama3.2:3b` | Fast general/multilingual/summarization fallback |
| `embeddinggemma` | Private embeddings |
| Tesseract 5 | OCR |
| ClamAV/FreshClam | Malware scanning/signature maintenance |

## Default maintenance window

Automatic Ollama engine and approved-model maintenance runs weekly on **Sunday at 1:00 AM Central Time**.

The canonical IANA zone is `America/Chicago`, not a fixed UTC offset. This keeps the window at 1:00 AM local Central time through CST/CDT transitions.

The Oracle desired-state contract is recorded in:

- `deployment/oracle-celar/release.json`
- `deployment/oracle-celar/systemd/celar-ollama-update.timer`

Updates are accepted only after direct model probes and the complete Celar Oracle health suite pass. A failed update restores the previous engine/model state.

## Pulse UI scope

The Module 084 page should expose sanitized operational information only:

- Oracle runtime reachability and health.
- Celar gateway version.
- Ollama engine version.
- Desired and installed model tags/digests for Gemma, Qwen, Llama, and EmbeddingGemma.
- OCR and ClamAV/FreshClam versions/signature state.
- Last update attempt, last successful update, result, and rollback availability.
- Automatic maintenance enabled/disabled state.
- Current maintenance cadence, day, local time, and time zone.
- Next calculated maintenance window in both Central time and the viewer's browser-local time.

No bearer token, provider API key, private document text, prompt text, customer data, or filesystem path containing a secret may be returned to the browser.

## Schedule management

The schedule editor is restricted to the actual signed-in permanent administrator authority. View-As never grants mutation authority.

The UI may change only a closed maintenance policy:

- automatic maintenance enabled/disabled;
- weekly day of week;
- local start time;
- approved IANA time zone, initially `America/Chicago`.

A browser request never runs `systemctl`, `sudo`, a shell, or an arbitrary command. Pulse writes a validated desired schedule through a narrow maintenance contract. The Oracle root-owned reconciliation helper validates the closed schema before applying the systemd timer override. Invalid or unsupported values fail closed.

Manual maintenance execution, if added later, must be a separate explicitly confirmed operation with a maintenance lock, full health acceptance, audit evidence, and rollback. It is not part of the initial schedule editor.

## Security boundary

- Module 084 is administrator-only.
- Pulse never receives or displays the Oracle runtime token.
- Oracle services `11434`, `3310`, and `8787` remain loopback-only.
- Caddy remains the only public Oracle application listener on HTTPS 443.
- Schedule mutation uses a dedicated least-privilege maintenance authorization boundary; the normal inference bearer token is not expanded into a privileged system-management credential.
- Every accepted schedule change records actual user, previous policy, new policy, timestamp, and result without secrets.
- Production remains outside the Protected-Test Oracle runtime package until separately authorized.

## Relationship to other modules

- **Module 011 — Celar AI:** consumes the private runtime for governed intelligence.
- **Module 064 — AI Provider Configuration Center:** owns provider credentials, provider/model routing, health, circuit breakers, usage, and fallback order.
- **Module 077 — Release, Deployment & Rollback Control Center:** owns application release/deployment governance; Module 084 reports Celar runtime maintenance evidence without taking over application deployment authority.
- **Module 078 — Observability, SLO & Application Health Center:** may consume Module 084 health signals; Module 084 remains the source for Celar runtime version and maintenance state.

## Delivery sequence

1. Govern the Oracle updater at Sunday 1:00 AM `America/Chicago` and validate it in CI.
2. Add a sanitized Oracle runtime status contract for engine/model/component versions plus last-update evidence.
3. Add Pulse administrator read-only status display.
4. Add the dedicated least-privilege schedule-change contract and root-owned Oracle reconciliation helper.
5. Add audit/history presentation and Protected-Test UAT.
6. Keep production untouched until a separate production authorization exists.
