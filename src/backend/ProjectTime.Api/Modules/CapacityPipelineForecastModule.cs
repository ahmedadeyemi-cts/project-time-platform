using Npgsql;
using System.Globalization;

namespace ProjectTime.Api.Modules;

/// <summary>
/// Module 070 projects existing capacity plans and engineering resource demand
/// into a continuous weekly forecast. It is a read-only calculation surface:
/// identity and request maintenance remain with their established owners.
/// </summary>
public static class CapacityPipelineForecastModule
{
    private const string ModuleNumber = "070";
    private const string ContractVersion = "2026-07-19.1";
    private const string ImplementationBaseline =
        "2b4a6d1a1242a25b52110a2a209ff8ddda0b8ca4";

    public static WebApplication MapCapacityPipelineForecastEndpoints(
        this WebApplication app)
    {
        app.MapGet(
            "/api/capacity-forecast/model",
            (Func<HttpContext, Task<IResult>>)GetModelAsync);

        app.MapGet(
            "/api/capacity-forecast/engineers",
            (Func<HttpContext, Task<IResult>>)GetEngineersAsync);

        app.MapGet(
            "/api/capacity-forecast/forecast",
            (Func<string?, int?, string?, Guid?, decimal?, HttpContext, Task<IResult>>)GetForecastAsync);

        return app;
    }

    private static async Task<IResult> GetModelAsync(HttpContext context)
    {
        var opened = await OpenScopedConnectionAsync(context);
        if (opened.Failure is not null) return opened.Failure;

        await using var connection = opened.Connection!;
        var access = opened.Access!;

        return Results.Ok(new
        {
            module = ModuleNumber,
            moduleName = "Capacity & Pipeline Forecasting",
            status = "forecast_model_loaded",
            contractVersion = ContractVersion,
            implementationBaseline = ImplementationBaseline,
            access = AccessResponse(access, context),
            practices = new[]
            {
                new { code = "all", label = "All practices" },
                new { code = "collaboration", label = "Collaboration" },
                new { code = "systems", label = "Systems" },
                new { code = "networking", label = "Networking" },
                new { code = "other", label = "Other" }
            },
            horizon = new { defaultWeeks = 14, minimumWeeks = 4, maximumWeeks = 52, weekStartsOn = "Monday" },
            calculations = new
            {
                workbookRevisedDemand = "committedDemand + futureProjectDemand - supplementalCapacity",
                netDemand = "max(committedDemand + weightedUnfilledPipeline - supplementalCapacity, 0)",
                remainingCapacity = "availableCapacity - netDemand",
                utilizationPercent = "availableCapacity == 0 ? null : netDemand / availableCapacity * 100",
                pipelineDistribution = "weighted unfilled request hours divided evenly across overlapping forecast weeks"
            },
            statusWeights = PipelineWeights,
            sourceOwnership = new
            {
                engineers = "Module 062 identity and User Administration",
                requestDates = "Module 020 Project Intake & Engineering Resource Requests",
                capacity = "resource_capacity_plans",
                supplementalCapacity = "scenario input only until an approved supplemental/LTE source is available"
            },
            mutableControls = new[]
            {
                "startDate",
                "weeks",
                "practice",
                "engineerUserId",
                "supplementalHoursPerWeek"
            },
            databaseMutationEnabled = false
        });
    }

    private static async Task<IResult> GetEngineersAsync(HttpContext context)
    {
        var opened = await OpenScopedConnectionAsync(context);
        if (opened.Failure is not null) return opened.Failure;

        await using var connection = opened.Connection!;
        var access = opened.Access!;

        try
        {
            var engineers = await LoadEngineersAsync(connection, access);
            return Results.Ok(new
            {
                module = ModuleNumber,
                status = "identity_engineers_loaded",
                access = AccessResponse(access, context),
                identityAuthority = "Module 062 / app_users.user_id",
                count = engineers.Count,
                engineers
            });
        }
        catch (Exception exception)
        {
            LogFailure(context, exception, "load identity-backed engineer choices");
            return ForecastUnavailable();
        }
    }

    private static async Task<IResult> GetForecastAsync(
        string? startDate,
        int? weeks,
        string? practice,
        Guid? engineerUserId,
        decimal? supplementalHoursPerWeek,
        HttpContext context)
    {
        var opened = await OpenScopedConnectionAsync(context);
        if (opened.Failure is not null) return opened.Failure;

        await using var connection = opened.Connection!;
        var access = opened.Access!;
        var normalizedStart = NormalizeStartDate(startDate);
        if (normalizedStart is null)
        {
            return Results.BadRequest(new
            {
                module = ModuleNumber,
                status = "invalid_start_date",
                message = "Start date must use YYYY-MM-DD format."
            });
        }

        var horizonWeeks = Math.Clamp(weeks ?? 14, 4, 52);
        var normalizedPractice = NormalizePractice(practice);
        var supplemental = Math.Clamp(supplementalHoursPerWeek ?? 0m, 0m, 10000m);
        var endExclusive = normalizedStart.Value.AddDays(horizonWeeks * 7);

        try
        {
            var engineers = await LoadEngineersAsync(connection, access);
            if (engineerUserId is not null
                && engineers.All(engineer => engineer.UserId != engineerUserId.Value))
            {
                return Results.Json(new
                {
                    module = ModuleNumber,
                    status = "engineer_outside_authorized_scope",
                    message = "The selected engineer is not available in the current server-authorized identity scope."
                }, statusCode: StatusCodes.Status403Forbidden);
            }

            var capacityRows = await LoadCapacityAsync(
                connection,
                access,
                normalizedStart.Value,
                endExclusive,
                normalizedPractice,
                engineerUserId);
            var demandRows = await LoadDemandAsync(
                connection,
                access,
                normalizedStart.Value,
                endExclusive,
                normalizedPractice,
                engineerUserId);

            var forecast = BuildForecast(
                normalizedStart.Value,
                horizonWeeks,
                supplemental,
                capacityRows,
                demandRows);
            var selectedEngineer = engineerUserId is null
                ? null
                : engineers.First(engineer => engineer.UserId == engineerUserId.Value);

            return Results.Ok(new
            {
                module = ModuleNumber,
                moduleName = "Capacity & Pipeline Forecasting",
                status = "forecast_loaded",
                contractVersion = ContractVersion,
                generatedAt = DateTimeOffset.UtcNow,
                access = AccessResponse(access, context),
                filters = new
                {
                    requestedStartDate = startDate,
                    startDate = normalizedStart.Value,
                    endDate = endExclusive.AddDays(-1),
                    weeks = horizonWeeks,
                    practice = normalizedPractice,
                    engineerUserId,
                    engineerDisplayName = selectedEngineer?.DisplayName,
                    supplementalHoursPerWeek = supplemental
                },
                summary = BuildSummary(forecast),
                weeks = forecast,
                demand = demandRows.Select(ToDemandResponse).ToArray(),
                calculation = new
                {
                    netDemand = "max(committed + weightedPipeline - supplemental, 0)",
                    remaining = "available - netDemand",
                    utilization = "netDemand / available * 100",
                    zeroCapacityGuard = true,
                    continuousMondayWeeks = true,
                    numericInputsOnly = true
                },
                sourceNotes = new[]
                {
                    "Engineer labels are loaded on demand from the shared ProjectPulse identity source using stable user IDs.",
                    "Assigned capacity comes from resource_capacity_plans; future demand comes from engineering_resource_requests.",
                    "Already allocated request hours are removed from team-wide unfilled pipeline demand to reduce double counting.",
                    "An engineer view includes only that engineer's proposed or pending allocation demand; confirmed assigned work is expected in committed capacity.",
                    "Supplemental/LTE capacity is a non-persistent scenario input because the current schema has no canonical supplemental resource tag.",
                    "Opportunity revenue is never converted to labor hours."
                }
            });
        }
        catch (Exception exception)
        {
            LogFailure(context, exception, "calculate the capacity and pipeline forecast");
            return ForecastUnavailable();
        }
    }

    private static async Task<List<EngineerChoice>> LoadEngineersAsync(
        NpgsqlConnection connection,
        ForecastAccess access)
    {
        const string sql = """
            SELECT
                u.user_id,
                COALESCE(NULLIF(u.display_name, ''), u.email) AS display_name,
                u.email,
                COALESCE(NULLIF(u.job_title, ''), 'Engineer') AS job_title,
                COALESCE(NULLIF(u.team_name, ''), NULLIF(u.department_name, ''), NULLIF(u.department, ''), 'Unassigned') AS team_name,
                COALESCE(NULLIF(u.department_name, ''), NULLIF(u.department, ''), NULLIF(u.team_name, ''), 'Unassigned') AS department_name,
                COALESCE(NULLIF(rp.primary_function, ''), '') AS primary_function
            FROM app_users u
            LEFT JOIN resource_profiles rp ON rp.user_id = u.user_id
            WHERE u.is_active = TRUE
              AND COALESCE(u.login_enabled, TRUE) = TRUE
              AND (
                  @broad_scope
                  OR u.user_id = @user_id
                  OR (
                      @team_scope
                      AND (
                          (@team_name <> '' AND COALESCE(u.team_name, '') = @team_name)
                          OR (@department_name <> '' AND COALESCE(u.department_name, u.department, '') = @department_name)
                      )
                  )
              )
              AND (
                  EXISTS (
                      SELECT 1
                      FROM app_user_role_assignments ura
                      JOIN app_roles r ON r.app_role_id = ura.app_role_id AND r.is_active = TRUE
                      WHERE ura.user_id = u.user_id
                        AND ura.is_active = TRUE
                        AND r.role_code IN ('ENGINEER', 'ENGINEERING', 'ENGINEERING_MANAGER', 'ENGINEERING_TEAM_LEAD')
                  )
                  OR EXISTS (SELECT 1 FROM resource_capacity_plans rcp WHERE rcp.user_id = u.user_id)
                  OR EXISTS (SELECT 1 FROM engineering_resource_request_assignments erra WHERE erra.user_id = u.user_id)
              )
            ORDER BY display_name, u.email;
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        AddScopeParameters(command, access);
        var rows = new List<EngineerChoice>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var teamName = reader.GetString(4);
            var departmentName = reader.GetString(5);
            var primaryFunction = reader.GetString(6);
            rows.Add(new EngineerChoice(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                teamName,
                departmentName,
                primaryFunction,
                ClassifyPractice(primaryFunction, teamName, departmentName)));
        }
        return rows;
    }

    private static async Task<List<CapacityRow>> LoadCapacityAsync(
        NpgsqlConnection connection,
        ForecastAccess access,
        DateOnly start,
        DateOnly endExclusive,
        string practice,
        Guid? engineerUserId)
    {
        const string sql = """
            SELECT
                rcp.week_start_date,
                rcp.available_hours,
                rcp.assigned_hours,
                COALESCE(NULLIF(rp.primary_function, ''), '') AS primary_function,
                COALESCE(NULLIF(u.team_name, ''), '') AS team_name,
                COALESCE(NULLIF(u.department_name, ''), NULLIF(u.department, ''), '') AS department_name
            FROM resource_capacity_plans rcp
            JOIN app_users u ON u.user_id = rcp.user_id AND u.is_active = TRUE
            LEFT JOIN resource_profiles rp ON rp.user_id = u.user_id
            WHERE rcp.week_start_date >= @start_date
              AND rcp.week_start_date < @end_date
              AND (@engineer_user_id IS NULL OR u.user_id = @engineer_user_id)
              AND (
                  @broad_scope
                  OR u.user_id = @user_id
                  OR (
                      @team_scope
                      AND (
                          (@team_name <> '' AND COALESCE(u.team_name, '') = @team_name)
                          OR (@department_name <> '' AND COALESCE(u.department_name, u.department, '') = @department_name)
                      )
                  )
              )
            ORDER BY rcp.week_start_date;
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("start_date", start);
        command.Parameters.AddWithValue("end_date", endExclusive);
        command.Parameters.Add("engineer_user_id", NpgsqlTypes.NpgsqlDbType.Uuid).Value =
            (object?)engineerUserId ?? DBNull.Value;
        AddScopeParameters(command, access);

        var rows = new List<CapacityRow>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var primaryFunction = reader.GetString(3);
            var teamName = reader.GetString(4);
            var departmentName = reader.GetString(5);
            var rowPractice = ClassifyPractice(primaryFunction, teamName, departmentName);
            if (practice != "all" && rowPractice != practice) continue;
            rows.Add(new CapacityRow(
                ReadDateOnly(reader, 0),
                Math.Max(0m, reader.GetDecimal(1)),
                Math.Max(0m, reader.GetDecimal(2))));
        }
        return rows;
    }

    private static async Task<List<DemandRow>> LoadDemandAsync(
        NpgsqlConnection connection,
        ForecastAccess access,
        DateOnly start,
        DateOnly endExclusive,
        string practice,
        Guid? engineerUserId)
    {
        const string sql = """
            SELECT
                err.engineering_resource_request_id,
                err.request_number,
                COALESCE(p.project_code, ''),
                COALESCE(p.project_name, ''),
                err.requested_function,
                COALESCE(err.skill_requirements, ''),
                GREATEST(err.requested_hours, 0),
                COALESCE(err.target_start_date, @start_date),
                COALESCE(err.target_end_date, err.target_start_date, @end_date - 1),
                err.priority,
                err.request_status,
                COALESCE(SUM(erra.allocated_hours) FILTER (
                    WHERE LOWER(erra.assignment_status) IN ('assigned', 'confirmed', 'active', 'in_progress')
                ), 0) AS committed_allocation,
                COALESCE(SUM(erra.allocated_hours) FILTER (
                    WHERE erra.user_id = @engineer_user_id
                      AND LOWER(erra.assignment_status) IN ('proposed', 'pending', 'requested')
                ), 0) AS selected_future_allocation
            FROM engineering_resource_requests err
            LEFT JOIN projects p ON p.project_id = err.project_id
            LEFT JOIN engineering_resource_request_assignments erra
              ON erra.engineering_resource_request_id = err.engineering_resource_request_id
            WHERE COALESCE(err.target_end_date, err.target_start_date, @end_date - 1) >= @start_date
              AND COALESCE(err.target_start_date, @start_date) < @end_date
              AND LOWER(err.request_status) NOT IN ('cancelled', 'canceled', 'rejected', 'closed', 'fulfilled', 'complete', 'completed')
              AND (
                  @engineer_user_id IS NULL
                  OR EXISTS (
                      SELECT 1
                      FROM engineering_resource_request_assignments selected
                      WHERE selected.engineering_resource_request_id = err.engineering_resource_request_id
                        AND selected.user_id = @engineer_user_id
                  )
              )
              AND (
                  @broad_scope
                  OR EXISTS (
                      SELECT 1
                      FROM engineering_resource_request_assignments scoped
                      JOIN app_users scoped_user ON scoped_user.user_id = scoped.user_id
                      WHERE scoped.engineering_resource_request_id = err.engineering_resource_request_id
                        AND (
                            scoped_user.user_id = @user_id
                            OR (
                                @team_scope
                                AND (
                                    (@team_name <> '' AND COALESCE(scoped_user.team_name, '') = @team_name)
                                    OR (@department_name <> '' AND COALESCE(scoped_user.department_name, scoped_user.department, '') = @department_name)
                                )
                            )
                        )
                  )
                  OR (
                      @team_scope
                      AND NOT EXISTS (
                          SELECT 1
                          FROM engineering_resource_request_assignments any_assignment
                          WHERE any_assignment.engineering_resource_request_id = err.engineering_resource_request_id
                      )
                      AND @access_practice <> 'other'
                      AND CASE
                          WHEN LOWER(err.requested_function) ~ '(collaboration|unified communication|voice|ucaa|teams calling)' THEN 'collaboration'
                          WHEN LOWER(err.requested_function) ~ '(network|routing|switching|wireless|sd-wan|sdwan)' THEN 'networking'
                          WHEN LOWER(err.requested_function) ~ '(system|server|cloud|compute|storage|virtualization|microsoft 365|azure)' THEN 'systems'
                          ELSE 'other'
                      END = @access_practice
                  )
              )
            GROUP BY
                err.engineering_resource_request_id,
                err.request_number,
                p.project_code,
                p.project_name,
                err.requested_function,
                err.skill_requirements,
                err.requested_hours,
                err.target_start_date,
                err.target_end_date,
                err.priority,
                err.request_status
            ORDER BY COALESCE(err.target_start_date, @start_date), err.priority, err.request_number;
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("start_date", start);
        command.Parameters.AddWithValue("end_date", endExclusive);
        command.Parameters.Add("engineer_user_id", NpgsqlTypes.NpgsqlDbType.Uuid).Value =
            (object?)engineerUserId ?? DBNull.Value;
        AddScopeParameters(command, access);
        command.Parameters.AddWithValue(
            "access_practice",
            ClassifyPractice(access.TeamName, access.DepartmentName));
        var rows = new List<DemandRow>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var requestedFunction = reader.GetString(4);
            var rowPractice = ClassifyPractice(requestedFunction);
            if (practice != "all" && rowPractice != practice) continue;

            var requestedHours = reader.GetDecimal(6);
            var committedAllocation = Math.Max(0m, reader.GetDecimal(11));
            var selectedFutureAllocation = Math.Max(0m, reader.GetDecimal(12));
            var unfilledHours = engineerUserId is null
                ? Math.Max(0m, requestedHours - committedAllocation)
                : selectedFutureAllocation;
            var requestStatus = reader.GetString(10);
            var weight = PipelineWeight(requestStatus);
            var demandStart = ReadDateOnly(reader, 7);
            var demandEnd = ReadDateOnly(reader, 8);
            if (demandEnd < demandStart) demandEnd = demandStart;
            rows.Add(new DemandRow(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                requestedFunction,
                reader.GetString(5),
                requestedHours,
                demandStart,
                demandEnd,
                reader.GetString(9),
                requestStatus,
                rowPractice,
                committedAllocation,
                unfilledHours,
                weight,
                Decimal.Round(unfilledHours * weight, 2)));
        }
        return rows;
    }

    private static List<ForecastWeek> BuildForecast(
        DateOnly start,
        int weeks,
        decimal supplementalHoursPerWeek,
        IReadOnlyCollection<CapacityRow> capacityRows,
        IReadOnlyCollection<DemandRow> demandRows)
    {
        var result = Enumerable.Range(0, weeks)
            .Select(index => new MutableForecastWeek(start.AddDays(index * 7)))
            .ToDictionary(row => row.WeekStart);

        foreach (var capacity in capacityRows)
        {
            var key = MondayOf(capacity.WeekStart);
            if (!result.TryGetValue(key, out var week)) continue;
            week.Available += capacity.Available;
            week.Committed += capacity.Assigned;
        }

        foreach (var demand in demandRows.Where(row => row.WeightedHours > 0))
        {
            var overlapping = result.Values
                .Where(week => demand.StartDate < week.WeekStart.AddDays(7)
                    && demand.EndDate >= week.WeekStart)
                .ToArray();
            if (overlapping.Length == 0) continue;
            var perWeek = demand.WeightedHours / overlapping.Length;
            foreach (var week in overlapping) week.WeightedPipeline += perWeek;
        }

        return result.Values
            .OrderBy(row => row.WeekStart)
            .Select(row =>
            {
                var available = Decimal.Round(row.Available, 2);
                var committed = Decimal.Round(row.Committed, 2);
                var pipeline = Decimal.Round(row.WeightedPipeline, 2);
                var net = Decimal.Round(
                    Math.Max(0m, committed + pipeline - supplementalHoursPerWeek),
                    2);
                var remaining = Decimal.Round(available - net, 2);
                decimal? utilization = available == 0
                    ? null
                    : Decimal.Round(net / available * 100m, 1);
                var state = available == 0 && net > 0
                    ? "capacity_missing"
                    : remaining < 0
                        ? "over_capacity"
                        : utilization >= 85m
                            ? "watch"
                            : "available";
                return new ForecastWeek(
                    row.WeekStart,
                    row.WeekStart.AddDays(6),
                    available,
                    committed,
                    pipeline,
                    supplementalHoursPerWeek,
                    net,
                    remaining,
                    utilization,
                    state);
            })
            .ToList();
    }

    private static object BuildSummary(IReadOnlyCollection<ForecastWeek> rows)
    {
        var available = rows.Sum(row => row.AvailableHours);
        var committed = rows.Sum(row => row.CommittedHours);
        var pipeline = rows.Sum(row => row.WeightedPipelineHours);
        var supplemental = rows.Sum(row => row.SupplementalHours);
        var net = rows.Sum(row => row.NetDemandHours);
        var remaining = rows.Sum(row => row.RemainingHours);
        return new
        {
            availableHours = Decimal.Round(available, 2),
            committedHours = Decimal.Round(committed, 2),
            weightedPipelineHours = Decimal.Round(pipeline, 2),
            supplementalHours = Decimal.Round(supplemental, 2),
            netDemandHours = Decimal.Round(net, 2),
            remainingHours = Decimal.Round(remaining, 2),
            utilizationPercent = available == 0 ? (decimal?)null : Decimal.Round(net / available * 100m, 1),
            overCapacityWeeks = rows.Count(row => row.State == "over_capacity" || row.State == "capacity_missing")
        };
    }

    private static object ToDemandResponse(DemandRow row) => new
    {
        row.RequestId,
        row.RequestNumber,
        row.ProjectCode,
        row.ProjectName,
        row.RequestedFunction,
        row.SkillRequirements,
        row.RequestedHours,
        row.StartDate,
        row.EndDate,
        row.Priority,
        row.RequestStatus,
        row.Practice,
        row.CommittedAllocationHours,
        row.UnfilledHours,
        row.ProbabilityWeight,
        row.WeightedHours
    };

    private static async Task<OpenOutcome> OpenScopedConnectionAsync(HttpContext context)
    {
        var userId = EffectiveSessionUserId(context);
        if (userId is null)
        {
            return new OpenOutcome(null, null, Results.Json(new
            {
                module = ModuleNumber,
                status = "session_required",
                message = "A valid ProjectPulse session is required."
            }, statusCode: StatusCodes.Status401Unauthorized));
        }

        var connectionString = BuildConnectionString();
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return new OpenOutcome(null, null, Results.Json(new
            {
                module = ModuleNumber,
                status = "configuration_missing",
                message = "Capacity forecast authorization is temporarily unavailable."
            }, statusCode: StatusCodes.Status503ServiceUnavailable));
        }

        var connection = new NpgsqlConnection(connectionString);
        try
        {
            await connection.OpenAsync();
            var access = await LoadAccessAsync(connection, userId.Value);
            if (!access.Active)
            {
                await connection.DisposeAsync();
                return new OpenOutcome(null, null, Results.Json(new
                {
                    module = ModuleNumber,
                    status = "access_denied",
                    message = "The active ProjectPulse user could not be resolved."
                }, statusCode: StatusCodes.Status403Forbidden));
            }
            return new OpenOutcome(connection, access, null);
        }
        catch (Exception exception)
        {
            await connection.DisposeAsync();
            LogFailure(context, exception, "resolve forecast authorization");
            return new OpenOutcome(null, null, Results.Json(new
            {
                module = ModuleNumber,
                status = "authorization_dependency_unavailable",
                message = "Capacity forecast authorization is temporarily unavailable."
            }, statusCode: StatusCodes.Status503ServiceUnavailable));
        }
    }

    private static async Task<ForecastAccess> LoadAccessAsync(
        NpgsqlConnection connection,
        Guid userId)
    {
        await using var command = new NpgsqlCommand("""
            SELECT
                u.user_id,
                COALESCE(NULLIF(u.display_name, ''), u.email),
                u.email,
                COALESCE(u.team_name, ''),
                COALESCE(u.department_name, u.department, ''),
                COALESCE(string_agg(DISTINCT r.role_code, ','), ''),
                COALESCE(string_agg(DISTINCT p.permission_code, ','), '')
            FROM app_users u
            LEFT JOIN app_user_role_assignments ura
              ON ura.user_id = u.user_id AND ura.is_active = TRUE
            LEFT JOIN app_roles r
              ON r.app_role_id = ura.app_role_id AND r.is_active = TRUE
            LEFT JOIN app_role_permissions rp ON rp.app_role_id = r.app_role_id
            LEFT JOIN app_permissions p ON p.app_permission_id = rp.app_permission_id
            WHERE u.user_id = @user_id AND u.is_active = TRUE
            GROUP BY u.user_id, u.display_name, u.email, u.team_name, u.department_name, u.department;
            """, connection);
        command.Parameters.AddWithValue("user_id", userId);
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return ForecastAccess.Inactive(userId);

        var roles = SplitSet(reader.GetString(5));
        var permissions = SplitSet(reader.GetString(6));
        var broad = HasAny(roles,
            "SUPER_ADMINISTRATOR", "SYSTEM_ADMINISTRATOR", "ADMINISTRATOR",
            "PROJECT_TEAM_COORDINATOR", "PROJECT_COORDINATOR", "EXECUTIVE", "EXECUTIVE_LEADERSHIP")
            || HasAny(permissions, "SYSTEM_ADMINISTRATION", "MANAGE_ALL");
        var team = broad || HasAny(roles,
            "MANAGER", "ENGINEERING_MANAGER", "ENGINEERING_TEAM_LEAD",
            "PROJECT_MANAGEMENT_TEAM_LEAD", "PROJECT_MANAGER", "PROJECT_MANAGEMENT")
            || HasAny(permissions,
                "VIEW_TEAM_UTILIZATION", "VIEW_RESOURCE_SCHEDULING", "MANAGE_RESOURCE_SCHEDULING");

        return new ForecastAccess(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            roles,
            permissions,
            broad,
            team,
            true);
    }

    private static void AddScopeParameters(NpgsqlCommand command, ForecastAccess access)
    {
        command.Parameters.AddWithValue("user_id", access.UserId);
        command.Parameters.AddWithValue("broad_scope", access.BroadScope);
        command.Parameters.AddWithValue("team_scope", access.TeamScope);
        command.Parameters.AddWithValue("team_name", access.TeamName);
        command.Parameters.AddWithValue("department_name", access.DepartmentName);
    }

    private static object AccessResponse(ForecastAccess access, HttpContext context) => new
    {
        effectiveUserId = access.UserId,
        access.DisplayName,
        roles = access.Roles.OrderBy(value => value).ToArray(),
        scope = access.BroadScope ? "organization" : access.TeamScope ? "team" : "self",
        isViewAs = context.Items.TryGetValue("ProjectPulseIsViewAs", out var value) && value is true,
        serverAuthorized = true
    };

    private static Guid? EffectiveSessionUserId(HttpContext context)
    {
        foreach (var key in new[] { "ProjectPulseEffectiveUserId", "ProjectPulseSessionUserId" })
        {
            if (!context.Items.TryGetValue(key, out var value)) continue;
            if (value is Guid userId) return userId;
            if (Guid.TryParse(value?.ToString(), out var parsed)) return parsed;
        }
        return null;
    }

    private static DateOnly? NormalizeStartDate(string? value)
    {
        DateOnly date;
        if (string.IsNullOrWhiteSpace(value)) date = DateOnly.FromDateTime(DateTime.UtcNow);
        else if (!DateOnly.TryParseExact(
                     value,
                     "yyyy-MM-dd",
                     CultureInfo.InvariantCulture,
                     DateTimeStyles.None,
                     out date)) return null;
        return MondayOf(date);
    }

    private static DateOnly MondayOf(DateOnly value)
    {
        var offset = ((int)value.DayOfWeek + 6) % 7;
        return value.AddDays(-offset);
    }

    private static string NormalizePractice(string? value)
    {
        var normalized = (value ?? "all").Trim().ToLowerInvariant();
        return normalized is "collaboration" or "systems" or "networking" or "other"
            ? normalized
            : "all";
    }

    private static string ClassifyPractice(params string[] values)
    {
        var text = string.Join(' ', values).ToLowerInvariant();
        if (ContainsAny(text, "collaboration", "unified communication", "unified communications", "voice", "ucaa", "teams calling")) return "collaboration";
        if (ContainsAny(text, "network", "routing", "switching", "wireless", "sd-wan", "sdwan")) return "networking";
        if (ContainsAny(text, "system", "server", "cloud", "compute", "storage", "virtualization", "microsoft 365", "azure")) return "systems";
        return "other";
    }

    private static bool ContainsAny(string text, params string[] values) =>
        values.Any(text.Contains);

    private static decimal PipelineWeight(string status)
    {
        var normalized = status.Trim().ToLowerInvariant();
        return normalized switch
        {
            "approved" or "assigned" or "confirmed" or "in_progress" or "in progress" => 1m,
            "submitted" or "pm_review" or "manager_review" or "coordinator_review" or "requested" => 0.6m,
            "draft" or "proposed" => 0.25m,
            _ => 0.5m
        };
    }

    private static readonly object[] PipelineWeights =
    {
        new { statuses = new[] { "approved", "assigned", "confirmed", "in_progress" }, weight = 1m },
        new { statuses = new[] { "submitted", "pm_review", "manager_review", "coordinator_review", "requested" }, weight = 0.6m },
        new { statuses = new[] { "draft", "proposed" }, weight = 0.25m },
        new { statuses = new[] { "other open status" }, weight = 0.5m }
    };

    private static HashSet<string> SplitSet(string value) =>
        value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static bool HasAny(IReadOnlySet<string> values, params string[] candidates) =>
        candidates.Any(values.Contains);

    private static DateOnly ReadDateOnly(NpgsqlDataReader reader, int ordinal)
    {
        var value = reader.GetValue(ordinal);
        return value switch
        {
            DateOnly date => date,
            DateTime dateTime => DateOnly.FromDateTime(dateTime),
            _ => DateOnly.Parse(value.ToString() ?? string.Empty)
        };
    }

    private static IResult ForecastUnavailable() => Results.Problem(
        title: "Capacity forecast unavailable",
        detail: "The role-scoped capacity and pipeline forecast could not be loaded.",
        statusCode: StatusCodes.Status500InternalServerError);

    private static void LogFailure(HttpContext context, Exception exception, string operation)
    {
        context.RequestServices
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("CapacityPipelineForecastModule")
            .LogError(exception, "Module 070 could not {Operation}.", operation);
    }

    private static string? BuildConnectionString()
    {
        foreach (var name in new[]
                 {
                     "ConnectionStrings__DefaultConnection",
                     "ConnectionStrings__ProjectPulse",
                     "ConnectionStrings__ProjectTime",
                     "PROJECTPULSE_CONNECTION_STRING",
                     "PROJECTTIME_DATABASE_CONNECTION"
                 })
        {
            var value = Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrWhiteSpace(value)) return value;
        }

        var host = Environment.GetEnvironmentVariable("PTP_DB_HOST");
        var database = Environment.GetEnvironmentVariable("PTP_DB_NAME");
        var username = Environment.GetEnvironmentVariable("PTP_DB_USER");
        var password = Environment.GetEnvironmentVariable("PTP_DB_PASSWORD");
        if (string.IsNullOrWhiteSpace(host)
            || string.IsNullOrWhiteSpace(database)
            || string.IsNullOrWhiteSpace(username)
            || string.IsNullOrWhiteSpace(password)) return null;

        return new NpgsqlConnectionStringBuilder
        {
            Host = host,
            Port = int.TryParse(Environment.GetEnvironmentVariable("PTP_DB_PORT"), out var port) ? port : 5432,
            Database = database,
            Username = username,
            Password = password,
            IncludeErrorDetail = false,
            Pooling = true,
            MaxPoolSize = 5
        }.ConnectionString;
    }

    private sealed record OpenOutcome(
        NpgsqlConnection? Connection,
        ForecastAccess? Access,
        IResult? Failure);

    private sealed record ForecastAccess(
        Guid UserId,
        string DisplayName,
        string Email,
        string TeamName,
        string DepartmentName,
        IReadOnlySet<string> Roles,
        IReadOnlySet<string> Permissions,
        bool BroadScope,
        bool TeamScope,
        bool Active)
    {
        public static ForecastAccess Inactive(Guid userId) => new(
            userId, string.Empty, string.Empty, string.Empty, string.Empty,
            new HashSet<string>(), new HashSet<string>(), false, false, false);
    }

    private sealed record EngineerChoice(
        Guid UserId,
        string DisplayName,
        string Email,
        string JobTitle,
        string TeamName,
        string DepartmentName,
        string PrimaryFunction,
        string Practice);

    private sealed record CapacityRow(
        DateOnly WeekStart,
        decimal Available,
        decimal Assigned);

    private sealed record DemandRow(
        Guid RequestId,
        string RequestNumber,
        string ProjectCode,
        string ProjectName,
        string RequestedFunction,
        string SkillRequirements,
        decimal RequestedHours,
        DateOnly StartDate,
        DateOnly EndDate,
        string Priority,
        string RequestStatus,
        string Practice,
        decimal CommittedAllocationHours,
        decimal UnfilledHours,
        decimal ProbabilityWeight,
        decimal WeightedHours);

    private sealed class MutableForecastWeek(DateOnly weekStart)
    {
        public DateOnly WeekStart { get; } = weekStart;
        public decimal Available { get; set; }
        public decimal Committed { get; set; }
        public decimal WeightedPipeline { get; set; }
    }

    private sealed record ForecastWeek(
        DateOnly WeekStart,
        DateOnly WeekEnd,
        decimal AvailableHours,
        decimal CommittedHours,
        decimal WeightedPipelineHours,
        decimal SupplementalHours,
        decimal NetDemandHours,
        decimal RemainingHours,
        decimal? UtilizationPercent,
        string State);
}
