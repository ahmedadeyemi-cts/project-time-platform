import React from "react";import GovernedOperationalReadCenter from "./GovernedOperationalReadCenter";
const surfaces=["overview","releases","environments","gates","evidence","rollback-policy"];
export default function ReleaseDeploymentControlCenter(){return <GovernedOperationalReadCenter module="077" title="Release, Deployment & Rollback Control Center" subtitle="Track immutable releases, promotion gates, deployment evidence, verification, and controlled rollback." basePath="/api/release-deployment-control" surfaces={surfaces}/>;}
