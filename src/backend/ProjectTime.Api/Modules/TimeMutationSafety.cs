using Npgsql;

namespace ProjectTime.Api.Modules;

internal static class TimeMutationSafety
{
    internal static async Task<bool> LockAndAllowAsync(NpgsqlConnection connection,
        NpgsqlTransaction transaction, Guid timesheetId, Guid owner, DateOnly? day = null,
        Guid? entryId = null, CancellationToken cancellationToken = default, bool editableOnly = false)
    {
        await using (var sheet = new NpgsqlCommand(
            "SELECT user_id FROM timesheets WHERE timesheet_id=@sheet FOR UPDATE", connection, transaction))
        {
            sheet.Parameters.AddWithValue("sheet", timesheetId);
            if (await sheet.ExecuteScalarAsync(cancellationToken) is not Guid actual || actual != owner) return false;
        }
        await using (var rows = new NpgsqlCommand("""
            SELECT status FROM timesheet_day_statuses
            WHERE timesheet_id=@sheet AND (@day::date IS NULL OR work_date=@day) FOR UPDATE;
            """, connection, transaction))
        {
            rows.Parameters.AddWithValue("sheet", timesheetId);
            rows.Parameters.AddWithValue("day", (object?)day ?? DBNull.Value);
            await using var reader = await rows.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var status = reader.GetString(0).ToLowerInvariant();
                if (status is "accounting_ready" or "reconciled" or "locked"
                    || (editableOnly && status is not ("draft" or "manager_declined"))) return false;
            }
        }
        await using (var rows = new NpgsqlCommand("""
            SELECT time_entry_id, status FROM time_entries
            WHERE timesheet_id=@sheet AND (@day::date IS NULL OR work_date=@day) FOR UPDATE;
            """, connection, transaction))
        {
            rows.Parameters.AddWithValue("sheet", timesheetId);
            rows.Parameters.AddWithValue("day", (object?)day ?? DBNull.Value);
            var found = entryId is null;
            await using var reader = await rows.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                if (reader.GetGuid(0) == entryId) found = true;
                var status = reader.GetString(1).ToLowerInvariant();
                if (status is "accounting_ready" or "reconciled" or "locked" or "invoiced"
                    || (editableOnly && status is not ("draft" or "manager_declined"))) return false;
            }
            if (!found) return false;
        }
        await using var invoices = new NpgsqlCommand("""
            SELECT EXISTS (
                SELECT 1 FROM time_entries te
                JOIN billing_invoice_lines line ON line.time_entry_id=te.time_entry_id
                JOIN billing_invoices invoice ON invoice.billing_invoice_id=line.billing_invoice_id
                WHERE te.timesheet_id=@sheet AND (@day::date IS NULL OR te.work_date=@day)
                  AND lower(COALESCE(invoice.invoice_status,'')) <> 'void');
            """, connection, transaction);
        invoices.Parameters.AddWithValue("sheet", timesheetId);
        invoices.Parameters.AddWithValue("day", (object?)day ?? DBNull.Value);
        return !Convert.ToBoolean(await invoices.ExecuteScalarAsync(cancellationToken));
    }
}
