-- Ahmed's explicit current instruction supersedes workbook cells that would
-- otherwise deny or leave Super Administrator access unset. Modules 001-003
-- remain Custom because their notes define safer granular operations. Full
-- Control never grants a non-bypassable action and does not make read-only
-- modules writable.
UPDATE projectpulse040_workbook_cells
SET designation = 'Full Control',
    scope_code = 'ORGANIZATION'
WHERE role_code = 'SUPER_ADMINISTRATOR'
  AND designation IN ('No Access', 'Not Set');
