import React from "react";import GovernedOperationalReadCenter from "./GovernedOperationalReadCenter";
const surfaces=["overview","engagements","milestones","artifacts","reviews","acceptance-policy","sharing-policy"];
export default function CustomerDeliveryAcceptanceCenter(){return <GovernedOperationalReadCenter module="080" title="Customer Delivery & Acceptance" subtitle="Coordinate customer deliverables, reviews, decisions, acceptance evidence, and controlled sharing." basePath="/api/customer-delivery-acceptance" surfaces={surfaces}/>;}
