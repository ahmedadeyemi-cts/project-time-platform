import React from "react";import GovernedOperationalReadCenter from "./GovernedOperationalReadCenter";
const surfaces=["overview","domains","classifications","retention-policies","lineage","legal-holds","privacy-policy"];
export default function DataGovernanceRetentionCenter(){return <GovernedOperationalReadCenter module="079" title="Data Governance, Retention & Privacy" subtitle="Manage accountable data domains, classifications, lineage, retention, legal holds, and privacy controls." basePath="/api/data-governance-retention" surfaces={surfaces}/>;}
