using Npgsql;
using ProjectTime.Api.Modules;

internal static class SecurityCompletionTests
{
    internal static async Task RunAsync()
    {
        var checks = 0;
        void Check(bool result, string name) { if (!result) throw new Exception(name); checks++; }
        var keyName = "PROJECTPULSE_MICROSOFT_INTEGRATION_SECRET_KEY";
        var priorKey = Environment.GetEnvironmentVariable(keyName);
        var priorPassword = Environment.GetEnvironmentVariable("PTP_DB_PASSWORD");
        try
        {
            Environment.SetEnvironmentVariable(keyName, null);
            Environment.SetEnvironmentVariable("PTP_DB_PASSWORD", "database-password-is-not-an-encryption-key");
            Check(IntegrationSecretKeys.Microsoft() is null, "Database passwords cannot substitute for encryption keys");
            foreach (var key in new[] { "not-base64", Convert.ToBase64String(new byte[16]), Convert.ToBase64String(new byte[64]) })
            {
                Environment.SetEnvironmentVariable(keyName,key);
                Check(IntegrationSecretKeys.Microsoft() is null,"Malformed or non-256-bit keys fail closed");
            }
            var expected = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
            Environment.SetEnvironmentVariable(keyName,Convert.ToBase64String(expected));
            Check(IntegrationSecretKeys.Microsoft()!.SequenceEqual(expected),"A dedicated 256-bit key round-trips");
        }
        finally
        {
            Environment.SetEnvironmentVariable(keyName,priorKey);
            Environment.SetEnvironmentVariable("PTP_DB_PASSWORD",priorPassword);
        }
        Check(!SafeDocumentMedia.IsAllowed("payload.html") && !SafeDocumentMedia.IsAllowed("payload.svg"),"Active document formats are rejected");
        Check(SafeDocumentMedia.IsAllowed("report.PDF"),"Supported documents remain accepted");
        Check(SafeDocumentMedia.DownloadContentType=="application/octet-stream","Stored caller MIME cannot select an active download type");
        foreach (var role in new[] { "ENGINEER", "PROJECT_MANAGER", "MANAGER", "SOLUTION_ARCHITECT", "ACCOUNT_EXECUTIVE", "EXECUTIVE" })
        {
            var access = new EnterpriseGovernanceAccess(Guid.NewGuid(), Guid.NewGuid(), "Test", "test.invalid", "", false,
                false, false, false, false, false, new HashSet<string> { role }, new HashSet<string>());
            Check(!access.CanManageLabEquipment && !access.CanImportLabEquipment, "A business role alone cannot write/import lab equipment");
            var manage = access with { Permissions = new HashSet<string> { "MANAGE_LAB_EQUIPMENT_081" } };
            Check(manage.CanManageLabEquipment && !manage.CanImportLabEquipment, "Manage does not imply import authority");
            var import = access with { Permissions = new HashSet<string> { "IMPORT_LAB_EQUIPMENT_081" } };
            Check(import.CanImportLabEquipment && !import.CanManageLabEquipment, "Import does not imply general write authority");
            Check(!(manage with { IsViewAs = true }).CanManageLabEquipment
                && !(import with { IsViewAs = true }).CanImportLabEquipment, "View-As cannot mutate lab equipment");
        }
        Check(MicrosoftMailRuntimeConfigurationModule.IsApprovedSmtpEndpoint("smtp.office365.com", 587), "Approved SMTP relay accepted");
        Check(!MicrosoftMailRuntimeConfigurationModule.IsApprovedSmtpEndpoint("smtp.office365.com.attacker.invalid", 587), "Foreign SMTP host rejected");
        Check(!MicrosoftMailRuntimeConfigurationModule.IsApprovedSmtpEndpoint("smtp.office365.com", 25), "Unapproved SMTP port rejected");
        var db = Environment.GetEnvironmentVariable("SECURITY_TEST_DB");
        if (string.IsNullOrWhiteSpace(db))
        {
            if (Environment.GetEnvironmentVariable("SECURITY_COMPLETION_REQUIRED") == "true")
                throw new InvalidOperationException("SECURITY_TEST_DB must name an isolated database for completion validation.");
            Console.WriteLine($"SECURITY_COMPLETION_STATIC_REGRESSIONS=PASS assertions={checks}; database=NOT_RUN");
            return;
        }
        await using var connection = new NpgsqlConnection(db);
        await connection.OpenAsync();
        async Task Exec(string sql) { await using var c = new NpgsqlCommand(sql,connection); await c.ExecuteNonQueryAsync(); }
        await Exec("""
            CREATE TEMP TABLE timesheets(timesheet_id uuid PRIMARY KEY,user_id uuid,status text);
            CREATE TEMP TABLE timesheet_day_statuses(timesheet_id uuid,work_date date,status text);
            CREATE TEMP TABLE time_entries(time_entry_id uuid PRIMARY KEY,timesheet_id uuid,work_date date,status text);
            CREATE TEMP TABLE billing_invoices(billing_invoice_id uuid PRIMARY KEY,invoice_status text);
            CREATE TEMP TABLE billing_invoice_lines(billing_invoice_id uuid,time_entry_id uuid);
            INSERT INTO timesheets VALUES('10000000-0000-0000-0000-000000000001','20000000-0000-0000-0000-000000000001','draft');
            INSERT INTO timesheet_day_statuses VALUES('10000000-0000-0000-0000-000000000001','2026-09-28','draft');
            INSERT INTO time_entries VALUES('30000000-0000-0000-0000-000000000001','10000000-0000-0000-0000-000000000001','2026-09-28','draft');
            """);
        var sheet=Guid.Parse("10000000-0000-0000-0000-000000000001");
        var owner=Guid.Parse("20000000-0000-0000-0000-000000000001");
        var entry=Guid.Parse("30000000-0000-0000-0000-000000000001");
        var day=new DateOnly(2026,9,28);
        async Task<bool> Allowed(Guid targetOwner, Guid? targetEntry=null,bool editableOnly=false)
        {
            await using var tx=await connection.BeginTransactionAsync();
            var result=await TimeMutationSafety.LockAndAllowAsync(connection,tx,sheet,targetOwner,day,targetEntry,editableOnly:editableOnly);
            await tx.RollbackAsync();
            return result;
        }
        Check(await Allowed(owner,entry,true),"Owner can edit a draft day");
        Check(!await Allowed(Guid.NewGuid(),entry),"Another user's sheet is rejected");
        Check(!await Allowed(owner,Guid.NewGuid()),"An entry from another sheet or day is rejected");
        foreach (var status in new[]{"accounting_ready","reconciled","locked"})
        {
            await Exec($"UPDATE timesheet_day_statuses SET status='{status}';");
            Check(!await Allowed(owner,entry),"Protected day status prevents corrections: "+status);
        }
        await Exec("UPDATE timesheet_day_statuses SET status='draft'; UPDATE time_entries SET status='pm_approved';");
        Check(!await Allowed(owner,entry,true),"Editable submission cannot replace approved entries even with a draft day header");
        await Exec("UPDATE time_entries SET status='draft'; INSERT INTO billing_invoices VALUES('40000000-0000-0000-0000-000000000001','issued'); INSERT INTO billing_invoice_lines VALUES('40000000-0000-0000-0000-000000000001','30000000-0000-0000-0000-000000000001');");
        Check(!await Allowed(owner,entry),"Billed time remains immutable even when status metadata is stale");
        await Exec("UPDATE billing_invoices SET invoice_status='void'");
        Check(await Allowed(owner,entry),"A void invoice releases otherwise editable evidence");
        await Exec("""
            CREATE TEMP TABLE work_billing_readiness_reviews(project_id uuid,evidence_source_type text,
                review_status text,notes text,package_type text,billing_period_start date,billing_period_end date,
                updated_at timestamptz,evidence_amount numeric);
            CREATE TEMP TABLE project_expense_uploads(project_id uuid,is_current boolean,deleted_at timestamptz,
                period_start date,period_end date,uploaded_at timestamptz,reimbursable_amount numeric);
            INSERT INTO work_billing_readiness_reviews VALUES
                ('50000000-0000-0000-0000-000000000001','expense','ready','','expense-only-pass-through','2026-09-01','2026-09-30',NOW(),100),
                ('50000000-0000-0000-0000-000000000002','expense','ready','','expense-only-pass-through','2026-09-01','2026-09-30',NOW(),100);
            """);
        var invoiceProject=Guid.Parse("50000000-0000-0000-0000-000000000001");
        async Task<string> ReviewStatus(string project)
        {
            await using var c=new NpgsqlCommand("SELECT review_status FROM work_billing_readiness_reviews WHERE project_id=@project",connection);
            c.Parameters.AddWithValue("project",Guid.Parse(project));return (string)(await c.ExecuteScalarAsync())!;
        }
        await Module005ProjectExpenseUploadModule.BlockStaleExpenseReadinessAsync(connection,null,default,invoiceProject);
        Check(await ReviewStatus(invoiceProject.ToString())=="blocked","Deleted expense evidence blocks readiness");
        Check(await ReviewStatus("50000000-0000-0000-0000-000000000002")=="ready","Invoice evidence recheck does not mutate another project's readiness");
        await Exec("""
            INSERT INTO project_expense_uploads VALUES('50000000-0000-0000-0000-000000000001',TRUE,NULL,'2026-09-01','2026-09-30',NOW()-INTERVAL '1 minute',100);
            UPDATE work_billing_readiness_reviews SET review_status='ready',updated_at=NOW();
            """);
        await Module005ProjectExpenseUploadModule.BlockStaleExpenseReadinessAsync(connection,null,default,invoiceProject);
        Check(await ReviewStatus(invoiceProject.ToString())=="ready","Unchanged current acknowledged evidence remains ready");
        await Exec("UPDATE project_expense_uploads SET reimbursable_amount=101");
        await Module005ProjectExpenseUploadModule.BlockStaleExpenseReadinessAsync(connection,null,default,invoiceProject);
        Check(await ReviewStatus(invoiceProject.ToString())=="blocked","Changed expense amount invalidates acknowledgement");
        Console.WriteLine($"SECURITY_COMPLETION_REGRESSIONS=PASS assertions={checks}");
    }
}
