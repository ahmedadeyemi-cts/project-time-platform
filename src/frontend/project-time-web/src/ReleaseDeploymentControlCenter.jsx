import React, { useEffect, useState } from "react";
import logo from "../brand/ussignal.png";
import "./release-deployment-control-center.css";
const surfaces = ["overview","releases","environments","gates","evidence","rollback-policy"];
export default function ReleaseDeploymentControlCenter() {
  const [data, setData] = useState({}); const [error, setError] = useState("");
  useEffect(() => { let active = true; Promise.all(surfaces.map(async key => { const response = await fetch(`/api/release-deployment-control/${key}`, { credentials: "include" }); if (!response.ok) throw new Error(`Unable to load ${key}.`); return [key, await response.json()]; })).then(rows => active && setData(Object.fromEntries(rows))).catch(e => active && setError(e.message)); return () => { active = false; }; }, []);
  return <section className="module077" data-module="077"><header><img src={logo} alt="US Signal"/><div><span>ProjectPulse · Module 077</span><h1>Release, Deployment &amp; Rollback Control Center</h1><p>Deployment promotion, rollback, pipeline, repository, cloud execution, notifications, and persistence are not authorized.</p></div></header>{error && <p role="alert">{error}</p>}<aside>Validated recovery source. Shared registration remains deferred.</aside><main>{surfaces.map(key => <article key={key}><h2>{key.replaceAll("-", " ")}</h2><p>{data[key]?.boundary || "Governed contract ready; no live data connected."}</p></article>)}</main></section>;
}
