# Legacy Module 011 Work Task Builder Recovery

The former Module 011 Work Task Builder was retired non-destructively before
Module 011 was reused for Pulse AI. Project creation and project/task management
remain owned by Modules 055D and 055C.

## Immutable recovery references

| Field | Value |
|---|---|
| Repository | `ahmedadeyemi-cts/project-time-platform` |
| Pre-reuse commit | `ad9fa2c76f6aba8df9bbdd4ab6970dcb0748fbb2` |
| Legacy component path | `src/frontend/project-time-web/src/WorkTaskBuilderPanel.jsx` |
| Legacy component blob | `cd58f58b77d9fe0dc9660c5fed75b9a6bf431c39` |
| Legacy stylesheet path | `src/frontend/project-time-web/src/work-task-builder-panel.css` |
| Historical route | `work-task-builder` |
| Replacement project routes | `work-register` and `create-work-register` |

## Recovery commands

The historical component can be inspected without changing the current branch:

```bash
git show ad9fa2c76f6aba8df9bbdd4ab6970dcb0748fbb2:src/frontend/project-time-web/src/WorkTaskBuilderPanel.jsx
```

A recovery copy can be written outside the active source path with:

```bash
git show ad9fa2c76f6aba8df9bbdd4ab6970dcb0748fbb2:src/frontend/project-time-web/src/WorkTaskBuilderPanel.jsx \
  > /tmp/LegacyWorkTaskBuilderPanel.jsx
```

These commands are recovery documentation only. They do not authorize restoring
the former route, moving project/task ownership away from Modules 055D/055C, or
replacing Pulse AI.

## Preserved business disposition

- Module 055D creates new projects.
- Module 055C manages existing projects, tasks, assignments, and delivery
  details.
- Module 020 remains the intake and resource-handoff owner.
- Modules 019 and 070 remain independent of the former Module 011 workflow.
- Pulse AI owns AI lifecycle governance only.
