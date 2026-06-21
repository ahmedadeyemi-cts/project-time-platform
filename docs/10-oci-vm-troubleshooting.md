# OCI VM Troubleshooting

## 1. Purpose

This document captures OCI VM provisioning issues encountered during setup and the recommended resolution path.

## 2. Issue: Image Not Valid for Shape

### Error

```text
API Error
Shape VM.Standard.E2.1.Micro is not valid for image ocid1.image.oc1.us-sanjose-1...
```

### Meaning

This usually means the selected image is not compatible with the selected compute shape. In this case, the likely mismatch is between the CPU architecture or image compatibility list and the selected Always Free shape.

OCI shapes may use different processor families, including AMD, Intel, and Arm-based processors. The selected operating system image must be compatible with the selected shape.

### Recommended Fix

Use one of the following paths:

#### Preferred Path: Arm-based Always Free VM

1. Select an Always Free eligible Arm shape, such as `VM.Standard.A1.Flex`, if capacity is available.
2. Select an Arm/aarch64-compatible Linux image.
3. Use Rocky Linux if an Arm-compatible Rocky image is available.
4. If Rocky Linux is not available, use Oracle Linux as a temporary fallback.

#### Fallback Path: AMD Micro VM

1. Keep shape `VM.Standard.E2.1.Micro`.
2. Change the image to an x86_64-compatible platform image, such as Oracle Linux.
3. Use this temporarily to validate the application architecture.
4. Rebuild on Rocky Linux later if needed.

## 3. Project Decision

For the Project Time Platform, the preference remains:

```text
Rocky Linux end-state
PostgreSQL database
.NET backend
React frontend
Podman containers
```

If Rocky Linux is blocked during OCI provisioning, Oracle Linux may be used temporarily because it is also RHEL-compatible. The final deployment process should still be documented for Rocky Linux.

## 4. Do Not Create Managed Oracle Database Yet

Do not create Oracle Autonomous Database for the application database at this stage. The project target database is PostgreSQL, running on the VM or inside a container.

## 5. Information to Capture After Resolution

After a VM is successfully created, document:

| Item | Value |
|---|---|
| Region | TBD |
| Shape | TBD |
| OS image | TBD |
| Architecture | x86_64 or aarch64 |
| Public IP | TBD |
| SSH username | TBD |
| Open ports | TBD |
| Notes | TBD |
