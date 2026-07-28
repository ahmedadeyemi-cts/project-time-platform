# Module 064 automatic provider health

Module 064 hydrates encrypted Claude and OpenAI configuration before provider routing, performs an automatic startup probe, refreshes stale health on the configured interval, and reconciles live configuration before every shared-router decision.

The manual **Refresh provider health** action remains an immediate administrative override. Module 001 and other AI consumers do not depend on opening Module 064 or selecting that button.

Provider API keys remain encrypted and write-only. Browser code polls only the ProjectPulse health endpoint while an automatic backend probe is in progress and never contacts an AI provider directly.
