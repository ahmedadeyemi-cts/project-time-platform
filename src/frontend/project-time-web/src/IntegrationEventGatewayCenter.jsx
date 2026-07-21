import React from "react";import GovernedOperationalReadCenter from "./GovernedOperationalReadCenter";
const surfaces=["overview","sources","contracts","deliveries","dead-letter-policy","security-policy"];
export default function IntegrationEventGatewayCenter(){return <GovernedOperationalReadCenter module="075" title="Integration Automation & Event Gateway" subtitle="Register, govern, observe, retry, replay, and quarantine integration events." basePath="/api/integration-event-gateway" surfaces={surfaces}/>;}
