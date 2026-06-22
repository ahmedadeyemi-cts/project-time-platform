# OCI Public IP and Networking Runbook

## 1. Purpose

This document captures how public IP addressing should be handled for the Project Time Platform OCI development VM.

## 2. Current Observation

The VM shows a private IP address:

```text
10.0.0.200
```

This is a private VCN address and is not directly reachable from a normal internet connection. It should not be expected to respond to ping from a local laptop or external network unless a VPN, bastion, or other private network path exists.

## 3. Public IP Requirement

To SSH into the instance from the internet, the instance must have a public IP address assigned to the private IP object associated with its VNIC.

The VM must also be in a public subnet with an internet gateway, route table, and security rule allowing SSH inbound access.

## 4. Assign Reserved Public IP to Instance

Use this process in the OCI Console:

1. Open the OCI navigation menu.
2. Go to `Compute`.
3. Select `Instances`.
4. Select the compartment where the instance was created.
5. Click the instance name.
6. Open the `Networking` tab.
7. Under `Attached VNICs`, click the VNIC name.
8. Open the `IP administration` tab.
9. Find the primary private IP address, such as `10.0.0.200`.
10. Click the three-dot `Actions` menu for the private IP.
11. Select `Edit`.
12. Under `Public IP type`, select `Reserved public IP`.
13. Choose `Select Existing Reserved IP Address`.
14. Select the reserved public IP from the list.
15. Click `Update`.

## 5. If No Reserved IP Appears

If the reserved IP is not listed:

- Confirm it was created in the same region.
- Confirm it is in the same or visible compartment.
- Confirm it is not already assigned to another resource.
- Change the compartment selector in the reserved IP selection box if needed.

## 6. Ping Note

Ping may still fail even after a public IP is assigned because ICMP is not always allowed by default. SSH is the important test.

The first connectivity test should be:

```bash
ssh -i /path/to/private_key opc@PUBLIC_IP_ADDRESS
```

## 7. Security List / Network Security Group Requirement

Inbound SSH requires port 22 to be allowed from the appropriate source.

For early testing, a temporary rule may allow SSH from the administrator's public IP only.

Avoid opening SSH to the entire internet if possible.

## 8. Required Post-Assignment Documentation

After assigning the public IP, document:

| Item | Value |
|---|---|
| Instance name | TBD |
| Private IP | 10.0.0.200 |
| Reserved public IP | TBD |
| Public subnet | TBD |
| VCN | TBD |
| Security list or NSG | TBD |
| SSH test result | TBD |

## 9. PostgreSQL Warning

Do not expose PostgreSQL publicly. PostgreSQL should remain accessible only locally on the VM or within the application container network.
