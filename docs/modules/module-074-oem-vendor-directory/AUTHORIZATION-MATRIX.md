# Module 074 authorization matrix

Authorization uses the **actual** ProjectPulse session identity and active canonical role assignments. Effective/View-As identity is returned for transparency but never grants edit authority.

| Capability | All authenticated users | Administrator / Super Administrator | Solution Architect | Project Team Coordinator |
| --- | ---: | ---: | ---: | ---: |
| View directory surface | Yes | Yes | Yes | Yes |
| Search and export visible draft | Yes | Yes | Yes | Yes |
| Add or remove a draft vendor | No | Yes | Yes | Yes |
| Edit canonical vendor fields | No | Yes | Yes | Yes |
| Submit server-side draft validation | No | Yes | Yes | Yes |
| Persist, import, synchronize, or publish | No | No | No | No |

No broad `MANAGE_ALL`, inferred department membership, View-As role, or external identity claim grants Module 074 edit access.
