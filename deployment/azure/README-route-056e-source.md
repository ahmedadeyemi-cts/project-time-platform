# 056E global legacy dashboard-card suppression

Base source commit:

`050c4548078505bd3a9b501bbb297905eaec4c82`

056E suppresses legacy injected Module 022-030 dashboard summary cards across
all routes, including `#dashboard`.

It preserves the actual React route workspaces by excluding descendants of
`#root`, `.app-shell`, route shells, route pages, modals, drawers, and panel
roots.

This source change does not deploy API code and does not change the database.
