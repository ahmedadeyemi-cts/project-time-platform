import React from "react";import GovernedOperationalReadCenter from "./GovernedOperationalReadCenter";
const surfaces=["overview","services","signals","slos","alerts","integrations","retention-policy"];
export default function ObservabilitySloHealthCenter(){return <GovernedOperationalReadCenter module="078" title="Observability, SLO & Application Health" subtitle="Understand service health, dependencies, reliability objectives, error budgets, and actionable alerts." basePath="/api/observability-slo-health" surfaces={surfaces}/>;}
