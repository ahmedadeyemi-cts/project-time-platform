import React, { useEffect, useState } from "react";
import logo from "../brand/ussignal.png";
import "./data-governance-retention-center.css";
const surfaces = ["overview","domains","classifications","retention-policies","lineage","legal-holds","privacy-policy"];
export default function DataGovernanceRetentionCenter() {
  const [data, setData] = useState({}); const [error, setError] = useState("");
  useEffect(() => { let active = true; Promise.all(surfaces.map(async key => { const response = await fetch(`/api/data-governance-retention/${key}`, { credentials: "include" }); if (!response.ok) throw new Error(`Unable to load ${key}.`); return [key, await response.json()]; })).then(rows => active && setData(Object.fromEntries(rows))).catch(e => active && setError(e.message)); return () => { active = false; }; }, []);
  return <section className="module079" data-module="079"><header><img src={logo} alt="US Signal"/><div><span>ProjectPulse · Module 079</span><h1>Data Governance, Retention &amp; Privacy Center</h1><p>Classification writes, retention execution, legal holds, exports, deletion, data movement, and persistence are not authorized.</p></div></header>{error && <p role="alert">{error}</p>}<aside>Validated recovery source. Shared registration remains deferred.</aside><main>{surfaces.map(key => <article key={key}><h2>{key.replaceAll("-", " ")}</h2><p>{data[key]?.boundary || "Governed contract ready; no live data connected."}</p></article>)}</main></section>;
}
