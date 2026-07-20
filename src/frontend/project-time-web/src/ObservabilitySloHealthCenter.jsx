import React, { useEffect, useState } from "react";
import logo from "../brand/ussignal.png";
import "./observability-slo-health-center.css";
const surfaces = ["overview","services","signals","slos","alerts","integrations","retention-policy"];
export default function ObservabilitySloHealthCenter() {
  const [data, setData] = useState({}); const [error, setError] = useState("");
  useEffect(() => { let active = true; Promise.all(surfaces.map(async key => { const response = await fetch(`/api/observability-slo-health/${key}`, { credentials: "include" }); if (!response.ok) throw new Error(`Unable to load ${key}.`); return [key, await response.json()]; })).then(rows => active && setData(Object.fromEntries(rows))).catch(e => active && setError(e.message)); return () => { active = false; }; }, []);
  return <section className="module078" data-module="078"><header><img src={logo} alt="US Signal"/><div><span>ProjectPulse · Module 078</span><h1>Observability, SLO &amp; Application Health Center</h1><p>Telemetry connectors, signal persistence, alert delivery, external notifications, and remediation are not authorized.</p></div></header>{error && <p role="alert">{error}</p>}<aside>Validated recovery source. Shared registration remains deferred.</aside><main>{surfaces.map(key => <article key={key}><h2>{key.replaceAll("-", " ")}</h2><p>{data[key]?.boundary || "Governed contract ready; no live data connected."}</p></article>)}</main></section>;
}
