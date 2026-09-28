using System.Text.Json;
using Npgsql;

namespace ProjectTime.Api.Modules;

/// <summary>Attach a customer-owned funding contract in the project creation transaction.</summary>
public static class ContractProjectFunding
{
    public static async Task AttachAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        Guid projectId, Guid actorId, JsonElement request, CancellationToken cancellationToken)
    {
        if (request.ValueKind != JsonValueKind.Object)
            throw new ArgumentException("Project funding must be provided as an object.");
        if (!request.TryGetProperty("fundingContractId", out var idValue) || idValue.ValueKind == JsonValueKind.Null) return;
        if (idValue.ValueKind != JsonValueKind.String)
            throw new ArgumentException("Select a valid funding contract.");
        if (string.IsNullOrWhiteSpace(idValue.GetString())) return;
        if (!Guid.TryParse(idValue.GetString(), out var contractId)
            || !request.TryGetProperty("fundingDrawdownRate", out var rateValue)
            || rateValue.ValueKind != JsonValueKind.Number
            || !rateValue.TryGetDecimal(out var rate) || rate <= 0 || rate > 999999999999m
            || decimal.Round(rate, 2) != rate)
            throw new ArgumentException("Select a funding contract and enter a positive contract drawdown rate.");

        // Serialize selection against contract updates and recheck eligibility on save.
        await using (var guard = new NpgsqlCommand("""
            SELECT c.boh_contract_id
            FROM boh_contracts c JOIN projects p ON p.client_id = c.client_id
            JOIN vw_boh_prepaid_balance_rows b ON b.boh_contract_id = c.boh_contract_id
            WHERE p.project_id = @project AND c.boh_contract_id = @contract
              AND c.balance_unit = 'currency'
              AND c.contract_status IN ('active', 'low_balance', 'expiring')
              AND CURRENT_DATE BETWEEN c.start_date AND c.effective_expiration_date
              AND b.remaining_balance > 0
              AND CASE
                  WHEN LOWER(p.contract_type) IN ('fixed price', 'fixed_price', 'fp') THEN c.eligible_fixed_price
                  WHEN LOWER(p.contract_type) IN ('time and material', 'time and materials', 't&m', 'tm') THEN c.eligible_tm
                  ELSE FALSE END
            FOR UPDATE OF c;
            """, connection, transaction))
        {
            guard.Parameters.AddWithValue("project", projectId);
            guard.Parameters.AddWithValue("contract", contractId);
            if (await guard.ExecuteScalarAsync(cancellationToken) is null)
                throw new ArgumentException("The funding contract must belong to this customer, support the selected billing type, be within its dates, and have a positive available balance.");
        }
        await using var insert = new NpgsqlCommand("""
            INSERT INTO contract_project_funding
                (project_id, boh_contract_id, drawdown_hourly_rate, created_by_user_id)
            VALUES (@project, @contract, @rate, @actor);
            INSERT INTO boh_contract_work_links
                (boh_contract_id, project_id, billing_classification, created_by_user_id)
            SELECT @contract, project_id, contract_type, @actor FROM projects WHERE project_id = @project;
            INSERT INTO work_register_change_history
                (work_register_change_history_id, source_table, work_id, action, change_summary,
                 changed_fields_csv, changed_by_user_id, old_value_json, new_value_json, changed_at)
            VALUES (gen_random_uuid(), 'projects', @project, 'contract_funding_selected',
                'Existing contract selected as the project labor funding source.',
                'Funding contract,Contract drawdown rate', @actor, NULL,
                jsonb_build_object('fundingContractId', @contract, 'drawdownHourlyRate', @rate), NOW());
            """, connection, transaction);
        insert.Parameters.AddWithValue("project", projectId);
        insert.Parameters.AddWithValue("contract", contractId);
        insert.Parameters.AddWithValue("rate", rate);
        insert.Parameters.AddWithValue("actor", actorId);
        await insert.ExecuteNonQueryAsync(cancellationToken);
    }
}
