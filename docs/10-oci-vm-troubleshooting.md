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
3. Confirm the image is not an Arm/aarch64 image.
4. Use this temporarily to validate the application architecture.
5. Rebuild on Rocky Linux later if needed.

## 3. Issue: Shape Changed but Same Image Error Remains

### Error

```text
API Error
Shape VM.Standard.E2.1.Micro is not valid for image ocid1.image.oc1.us-sanjose-1.aaaaaaaa32w7nllwp3hl6encgotpkyts2pve2eeg7l3gbdnpmlcxycbjvr4a.
```

### Meaning

Changing only the shape is not enough if the previously selected image remains attached to the VM creation form. The image OCID in the error is still the selected image, and that image is not compatible with `VM.Standard.E2.1.Micro`.

### Recommended Fix

1. Return to the `Image and shape` section.
2. Click `Change image`.
3. Select an Oracle-provided platform image for `Oracle Linux`.
4. Choose the x86_64 build if the UI shows architecture choices.
5. Then select `VM.Standard.E2.1.Micro`.
6. Retry VM creation.

If the UI continues to reuse the incompatible image, abandon the current VM creation form and start a new `Create VM instance` flow from scratch:

1. Select the image first: Oracle Linux x86_64.
2. Select the shape second: `VM.Standard.E2.1.Micro`.
3. Confirm the final summary shows Oracle Linux and E2 Micro before clicking Create.

## 4. Issue: Out of Capacity for A1 Flex Shape

### Error

```text
API Error
Out of capacity for shape VM.Standard.A1.Flex in availability domain AD-1.
Create the instance in a different availability domain or try again later.
If you specified a fault domain, try creating the instance without specifying a fault domain.
```

### Meaning

This is not an operating system error and not a project configuration error. It means OCI does not currently have available capacity for the selected Always Free Arm shape in the selected availability domain.

## 5. Issue: Only One Availability Domain Is Available

### Observation

During VM creation in the selected OCI region, the Placement screen only showed:

```text
AD 1
```

No alternate availability domain was available in the UI.

### Meaning

Because only one availability domain is shown, the A1 Flex capacity issue cannot be resolved by selecting another availability domain in the current region.

### Recommended Fix Order

Try the following in order:

1. Remove any manually selected fault domain and let OCI choose automatically.
2. Keep `VM.Standard.A1.Flex` but reduce the allocation to the smallest useful size, such as 1 OCPU and 4-6 GB RAM.
3. Retry later because A1 Free Tier capacity can become available at a later time.
4. Use `VM.Standard.E2.1.Micro` with a compatible x86_64 Oracle Linux image as the practical short-term fallback.
5. If OCI remains blocked, use a local Rocky Linux VM temporarily until OCI capacity is available.

## 6. Project Decision

For the Project Time Platform, the preference remains:

```text
Rocky Linux end-state
PostgreSQL database
.NET backend
React frontend
Podman containers
```

If Rocky Linux is blocked during OCI provisioning, Oracle Linux may be used temporarily because it is also RHEL-compatible. The final deployment process should still be documented for Rocky Linux.

For the initial no-cost test server, Oracle Linux on `VM.Standard.E2.1.Micro` is an acceptable fallback because it is RHEL-compatible and will keep the deployment process closer to Rocky Linux than Ubuntu.

Ubuntu may be used temporarily only if it is the only available free option, but it should be documented as a short-term development workaround because its package manager and OS conventions differ from Rocky Linux.

## 7. Do Not Create Managed Oracle Database Yet

Do not create Oracle Autonomous Database for the application database at this stage. The project target database is PostgreSQL, running on the VM or inside a container.

## 8. Information to Capture After Resolution

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
