import React, { useEffect, useState } from "react";
import logo from "../brand/ussignal.png";
import "./customer-delivery-acceptance-center.css";
const surfaces = ["overview","engagements","milestones","artifacts","reviews","acceptance-policy","sharing-policy"];
export default function CustomerDeliveryAcceptanceCenter() {
  const [data, setData] = useState({}); const [error, setError] = useState("");
  useEffect(() => { let active = true; Promise.all(surfaces.map(async key => { const response = await fetch(`/api/customer-delivery-acceptance/${key}`, { credentials: "include" }); if (!response.ok) throw new Error(`Unable to load ${key}.`); return [key, await response.json()]; })).then(rows => active && setData(Object.fromEntries(rows))).catch(e => active && setError(e.message)); return () => { active = false; }; }, []);
  return <section className="module080" data-module="080"><header><img src={logo} alt="US Signal"/><div><span>ProjectPulse · Module 080</span><h1>Customer Delivery &amp; Acceptance Portal</h1><p>External identity, invitations, links, sharing, comments, acceptance, rejection, notifications, and persistence are not authorized.</p></div></header>{error && <p role="alert">{error}</p>}<aside>Validated recovery source. Shared registration remains deferred.</aside><main>{surfaces.map(key => <article key={key}><h2>{key.replaceAll("-", " ")}</h2><p>{data[key]?.boundary || "Governed contract ready; no live data connected."}</p></article>)}</main></section>;
}
