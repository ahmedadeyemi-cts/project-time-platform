# 025I SOW Route Isolation

## Purpose
Prevent dashboard cards from appearing above the SOW Generator workspace.

## Behavior
- On `#dashboard`, the SOW Generator card appears with other modules.
- On `#sow-generator`, dashboard cards are hidden and the SOW Generator workspace is shown by itself.
- This prevents the visual endless-scroll behavior caused by dashboard and route content rendering together.
