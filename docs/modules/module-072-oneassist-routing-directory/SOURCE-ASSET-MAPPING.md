# Existing Source Asset Mapping

Read-only discovery used the attached combined On-Call/OneAssist archive and current `ahmedadeyemi-cts/ussignal` `main` commit `da634f7620c2f76d6129020133f27481232edfbd`.

| Existing behavior | Source evidence | Module 072 disposition |
|---|---|---|
| Customer/PIN editor | `app.js` OneAssist section | Preserved with ProjectPulse authorization |
| Five-digit and uniqueness validation | admin save handlers | Preserved on client and server |
| CSV/XLSX import | admin UI JavaScript | Preserved through server-side preview parser without adding a frontend dependency |
| CSV and IVR CSV downloads | `app.js` | Preserved |
| Full public directory | `functions/api/ps-customers/index.js` | Preserved under versioned public API |
| PIN resolution | `functions/api/customers/index.js` | Preserved under versioned public API |
| Cloudflare KV persistence | `ONCALL:PS_CUSTOMERS` | Preserved temporarily through compatibility adapter |
| PIN described as authentication | legacy comments | Corrected: PIN is a public routing identifier and cannot authenticate a person |

No PIN values from the attachment, runtime Cloudflare KV, screenshots, or source environment were copied into ProjectPulse.
