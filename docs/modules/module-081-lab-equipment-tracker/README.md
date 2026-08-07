# Module 081 — Lab Equipment Tracker

Module 081 is the authoritative ProjectPulse workspace for US Signal lab equipment, IP address management, cabling, rack placement, reviewed legacy-data imports, and operational evidence.

## Operating surfaces

- Equipment inventory with asset, host, physical placement, custodian, project, warranty, lifecycle, and immutable source provenance.
- IPv4/IPv6 allocations organized by location, pod, network zone, VLAN, VRF, equipment, interface, and reservation state.
- Cabling and logical connections between scoped equipment endpoints.
- 42U rack occupancy with database-enforced overlap prevention.
- CSV/XLSX preview, deterministic mapping, sensitive-column blocking, row validation, SHA-256 evidence, explicit commit, and cancellable previews.
- Role-scoped Excel/PDF evidence using the repository-owned US Signal brand asset and spreadsheet formula neutralization.

## Release contract

Migration `076_module_081_lab_equipment_tracker.sql` is additive and its paired rollback removes only Module 081-owned objects and grants. The route is `#lab-equipment-tracker`; the API root is `/api/lab-equipment-tracker`.

No external lab controller, IPAM product, switch, cloud, or identity service is mutated by this module.
