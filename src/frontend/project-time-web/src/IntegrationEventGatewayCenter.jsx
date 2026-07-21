import React, { useEffect, useState } from "react";
import logo from "../brand/ussignal.png";
import "./integration-event-gateway-center.css";
const surfaces = ["overview","sources","contracts","deliveries","dead-letter-policy","security-policy"];
export default function IntegrationEventGatewayCenter() {
  const [data, setData] = useState({}); const [error, setError] = useState("");
  useEffect(() => { let active = true; Promise.all(surfaces.map(async key => { const response = await fetch(`/api/integration-event-gateway/${key}`, { credentials: "include" }); if (!response.ok) throw new Error(`Unable to load ${key}.`); return [key, await response.json()]; })).then(rows => active && setData(Object.fromEntries(rows))).catch(e => active && setError(e.message)); return () => { active = false; }; }, []);
  return <section className="module075" data-module="075"><header><img src={logo} alt="US Signal"/><div><span>ProjectPulse · Module 075</span><h1>Integration Automation &amp; Event Gateway</h1><p>Webhook intake, connector calls, delivery, replay, quarantine, persistence, notifications, secret access, and AI execution are not authorized.</p></div></header>{error && <p role="alert">{error}</p>}<aside>Validated recovery source. Shared registration remains deferred.</aside><main>{surfaces.map(key => <article key={key}><h2>{key.replaceAll("-", " ")}</h2><p>{data[key]?.boundary || "Governed contract ready; no live data connected."}</p></article>)}</main></section>;
}
