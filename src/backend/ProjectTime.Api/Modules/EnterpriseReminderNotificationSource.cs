using System.Globalization;
using System.Text.Json;
using Npgsql;
using static ProjectTime.Api.Modules.EnterpriseReminderPolicy;

namespace ProjectTime.Api.Modules;

// These are new governed producers, NOT a sender for historical dry-run or legacy outbox rows.
internal static class EnterpriseReminderNotificationSource
{
    internal static async Task<EnterpriseNotificationSourceObservation> ScanAsync(NpgsqlConnection connection,string correlationId,CancellationToken token,DateTimeOffset? clock=null)
    {
        await using (var ready = new NpgsqlCommand("SELECT count(*)=4 FROM enterprise_notification_policies WHERE producer_contract='enterprise-reminder-v1';", connection))
            if (await ready.ExecuteScalarAsync(token) is not true)
                return EnterpriseNotificationSourceObservation.Unavailable("governed_time_holiday_reminders","023","MIGRATION_129_REQUIRED",
                    "The optional governed reminder policies are not initialized. Existing dry-run previews remain non-sending.");
        var observed=0; var created=0; var errors=0;
        foreach(var code in Codes)
        {
            try
            {
                var policy=await EnterpriseNotificationRepository.LoadPolicyAsync(connection,code,token);
                if(policy is null || !policy.Enabled || policy.ProducerContract!="enterprise-reminder-v1") continue;
                var now=clock ?? DateTimeOffset.UtcNow; var local=LocalTime(now,policy.TriggerConfiguration);
                if(!DueToday(code,local,policy.TriggerConfiguration)) continue;
                var today=DateOnly.FromDateTime(local);
                if(code=="COMPANY_HOLIDAY_UPCOMING")
                {
                    var holidays=new List<(Guid Id,DateOnly Date,string Name)>();
                    await using(var query=new NpgsqlCommand("SELECT company_holiday_id,holiday_date,holiday_name FROM company_holidays WHERE is_active=TRUE AND is_floating_holiday=FALSE AND EXTRACT(ISODOW FROM holiday_date) BETWEEN 1 AND 5 AND holiday_date BETWEEN @today+1 AND @today+30;",connection))
                    {
                        query.Parameters.AddWithValue("today",today);
                        await using var reader=await query.ExecuteReaderAsync(token);
                        while(await reader.ReadAsync(token)) holidays.Add((reader.GetGuid(0),reader.GetFieldValue<DateOnly>(1),reader.GetString(2)));
                    }
                    foreach(var holiday in holidays.Where(h=>HolidayOffsets(policy.TriggerConfiguration).Contains(h.Date.DayNumber-today.DayNumber)))
                    {
                        var offset=holiday.Date.DayNumber-today.DayNumber;
                        if(!await RuleActiveAsync(connection,offset==7 ? "HOLIDAY_TIME_REMINDER_7_DAY" : offset==1 ? "HOLIDAY_TIME_REMINDER_1_DAY" : "",token)) continue;
                        // All currently active application users, one event per user. Never a shared financial/personnel chat.
                        foreach(var user in await AudienceAsync(connection,null,token))
                        {
                            observed++;
                            created+=await InsertAsync(policy,holiday.Id,user.Id,new { recipientName=user.Name,holidayName=holiday.Name,
                                holidayDate=holiday.Date.ToString("yyyy-MM-dd"),offsetDays=offset,reminderDate=today.ToString("yyyy-MM-dd"),
                                deepLink="#time-entry",correlationId },$"{holiday.Id:N}:{offset}:{user.Id:N}",now);
                        }
                    }
                }
                else
                {
                    var rule=Text(policy.TriggerConfiguration,"legacyRuleCode","");
                    if(!await RuleActiveAsync(connection,rule,token)) continue;
                    var group=Text(policy.TriggerConfiguration,"audienceGroup",code=="PM_MONTH_END_REMINDER" ? "PROJECT_MANAGEMENT" : "ENGINEERS");
                    var week=CompletedWeek(today);
                    foreach(var user in await AudienceAsync(connection,group,token))
                    {
                        observed++;
                        if(code!="PM_MONTH_END_REMINDER" && !await MissingAsync(connection,user.Id,week,token)) continue;
                        created+=await InsertAsync(policy,user.Id,user.Id,new { recipientName=user.Name,engineerName=user.Name,
                            weekStart=week.ToString("yyyy-MM-dd"),weekEnd=week.AddDays(6).ToString("yyyy-MM-dd"),
                            reminderDate=today.ToString("yyyy-MM-dd"),deepLink="#time-entry",correlationId },$"{today:yyyyMMdd}:{user.Id:N}",now);
                    }
                }
                async Task<int> InsertAsync(EnterpriseNotificationPolicyRow p,Guid entity,Guid user,object payload,string suffix,DateTimeOffset time)
                {
                    var key=$"enterprise-reminder:{p.PolicyCode}:{suffix}";
                    var inserted=await EnterpriseNotificationRepository.InsertEventAsync(connection,p.PolicyCode,p.SourceModule,key,key,
                        p.PolicyCode=="COMPANY_HOLIDAY_UPCOMING" ? "company_holiday" : "app_user",entity,null,user,time,time,
                        JsonSerializer.SerializeToElement(payload),"authoritative_scanner",null,correlationId,token);
                    return inserted.Created ? 1 : 0;
                }
            }
            catch(OperationCanceledException) when(token.IsCancellationRequested) { throw; }
            catch(Exception) { errors++; }
        }
        return errors==0 ? EnterpriseNotificationSourceObservation.Healthy("governed_time_holiday_reminders","023",observed,created,
            "Evaluated enabled non-submission, escalation, company holiday and month-end reminder policies. Historical previews were not sent.")
            : EnterpriseNotificationSourceObservation.Failed("governed_time_holiday_reminders","023","REMINDER_SOURCE_UNAVAILABLE",
                "One or more reminder sources could not be evaluated. Recipients were not broadened.");
    }

    internal static async Task<bool> IsCurrentAsync(NpgsqlConnection connection,EnterpriseNotificationEventRow notification,CancellationToken token,DateTimeOffset? clock=null)
    {
        if(!Codes.Contains(notification.PolicyCode)) return true;
        var policy=await EnterpriseNotificationRepository.LoadPolicyAsync(connection,notification.PolicyCode,token);
        if(policy is null || !policy.Enabled || policy.ProducerContract!="enterprise-reminder-v1" || !notification.SubjectUserId.HasValue) return false;
        var today=DateOnly.FromDateTime(LocalTime(clock ?? DateTimeOffset.UtcNow,policy.TriggerConfiguration));
        if(EnterpriseNotificationRecipientResolver.PayloadString(notification.Payload,"reminderDate")!=today.ToString("yyyy-MM-dd")) return false;
        if(notification.PolicyCode=="COMPANY_HOLIDAY_UPCOMING")
        {
            var date=EnterpriseNotificationRecipientResolver.PayloadDate(notification.Payload,"holidayDate");
            if(!date.HasValue || !HolidayOffsets(policy.TriggerConfiguration).Contains(date.Value.DayNumber-today.DayNumber)) return false;
            var offset=date.Value.DayNumber-today.DayNumber;
            if(!await RuleActiveAsync(connection,offset==7 ? "HOLIDAY_TIME_REMINDER_7_DAY" : offset==1 ? "HOLIDAY_TIME_REMINDER_1_DAY" : "",token)) return false;
            await using var query=new NpgsqlCommand("SELECT EXISTS(SELECT 1 FROM company_holidays WHERE company_holiday_id=@id AND holiday_date=@date AND is_active=TRUE AND is_floating_holiday=FALSE AND EXTRACT(ISODOW FROM holiday_date) BETWEEN 1 AND 5);",connection);
            query.Parameters.AddWithValue("id",notification.EntityId ?? Guid.Empty); query.Parameters.AddWithValue("date",date.Value);
            return await query.ExecuteScalarAsync(token) is true;
        }
        if(!await RuleActiveAsync(connection,Text(policy.TriggerConfiguration,"legacyRuleCode",""),token)) return false;
        var group=Text(policy.TriggerConfiguration,"audienceGroup",notification.PolicyCode=="PM_MONTH_END_REMINDER" ? "PROJECT_MANAGEMENT" : "ENGINEERS");
        var audience=await AudienceAsync(connection,group,token);
        if(!audience.Any(u=>u.Id==notification.SubjectUserId.Value)) return false;
        if(notification.PolicyCode=="PM_MONTH_END_REMINDER") return true;
        var week=EnterpriseNotificationRecipientResolver.PayloadDate(notification.Payload,"weekStart");
        return week.HasValue && week.Value==CompletedWeek(today) && await MissingAsync(connection,notification.SubjectUserId.Value,week.Value,token);
    }

    private static async Task<bool> MissingAsync(NpgsqlConnection connection,Guid user,DateOnly week,CancellationToken token)
    {
        await using var query=new NpgsqlCommand("""
            SELECT NOT EXISTS(SELECT 1 FROM timesheets WHERE user_id=@user AND week_start_date=@week
              AND status IN ('submitted','manager_approved','pm_approved','accounting_ready','reconciled','locked'))
              AND EXISTS(SELECT 1 FROM app_users WHERE user_id=@user AND is_active=TRUE AND login_enabled=TRUE);
            """,connection);
        query.Parameters.AddWithValue("user",user);query.Parameters.AddWithValue("week",week);
        return await query.ExecuteScalarAsync(token) is true;
    }
    private static async Task<bool> RuleActiveAsync(NpgsqlConnection connection,string code,CancellationToken token)
    {
        if(code.Length==0) return true; // Explicit non-default offsets are owned by the new policy configuration.
        await using var query=new NpgsqlCommand("SELECT EXISTS(SELECT 1 FROM reminder_rules WHERE rule_code=@code AND is_active=TRUE);",connection);
        query.Parameters.AddWithValue("code",code);return await query.ExecuteScalarAsync(token) is true;
    }
    private static async Task<List<(Guid Id,string Name)>> AudienceAsync(NpgsqlConnection connection,string? group,CancellationToken token)
    {
        var rows=new List<(Guid,string)>();
        await using var query=new NpgsqlCommand("""
            SELECT u.user_id,COALESCE(NULLIF(u.display_name,''),u.email) FROM app_users u
            WHERE u.is_active=TRUE AND u.login_enabled=TRUE AND (@group IS NULL OR EXISTS(
              SELECT 1 FROM notification_group_members m JOIN notification_groups g USING(notification_group_id)
              WHERE m.user_id=u.user_id AND m.is_active=TRUE AND g.is_active=TRUE AND g.group_code=@group))
            ORDER BY u.user_id;
            """,connection);
        query.Parameters.Add(new("group",NpgsqlTypes.NpgsqlDbType.Text){Value=(object?)group ?? DBNull.Value});
        await using var reader=await query.ExecuteReaderAsync(token);
        while(await reader.ReadAsync(token)) rows.Add((reader.GetGuid(0),reader.GetString(1)));
        return rows;
    }
}
