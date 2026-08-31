#!/usr/bin/env python3
from pathlib import Path
import argparse

parser = argparse.ArgumentParser()
parser.add_argument('--input', required=True)
parser.add_argument('--output', required=True)
args = parser.parse_args()

source_path = Path(args.input)
output_path = Path(args.output)
text = source_path.read_text(encoding='utf-8')

old_block = '''        var connector = await LoadConnectorAsync(connection, transaction);
        var card = await LoadCommercialRateCardAsync(connection, project, transaction);
        var rates = card is null
            ? []
            : await LoadCommercialRatesAsync(connection, card.RateCardId, transaction);

        var liveSyncEnabled = ReadFlag("PROJECTPULSE_SELL_LIVE_SYNC_ENABLED");
        var cutoverEnabled = ReadFlag("PROJECTPULSE_SELL_COMMERCIAL_READ_MODEL_ACTIVE");
        var connectorReady = connector is not null
            && connector.InboundEnabled
            && connector.ConnectionStatus is "configured" or "connected"
            && connector.LastSuccessfulSyncAt is not null;
        var quoteReady = !string.IsNullOrWhiteSpace(project.SellQuoteNumber);
        var rateReady = card is not null && rates.Count > 0;
        var cutoverReady = connectorReady && quoteReady && rateReady;
        var source = cutoverEnabled && cutoverReady
            ? "SELL"
            : "current_stored_rates";
        var readiness = !connectorReady
            ? "sell_connector_not_ready"
            : !quoteReady
                ? "sell_quote_missing"
                : !rateReady
                    ? "commercial_rate_missing"
                    : cutoverEnabled
                        ? "sell_active"
                        : "sell_ready_for_guarded_cutover";
        var billingMethod = BillingMethod(project.ContractType);
        var milestoneReadiness = billingMethod is "fixed_fee_milestone" or "hybrid_time_and_milestone"
            ? "milestone_schedule_required_not_configured"
            : "not_required_for_time_and_materials";
'''

new_block = '''        var customerSource = await CustomerSourceAuthorityModule.LoadAuthorityAsync(connection, transaction);
        var card = await LoadCommercialRateCardAsync(connection, project, transaction);
        var rates = card is null
            ? []
            : await LoadCommercialRatesAsync(connection, card.RateCardId, transaction);

        SellConnectorSummary? connector = null;
        var rateReady = card is not null && rates.Count > 0;
        var billingMethod = BillingMethod(project.ContractType);
        var milestoneReadiness = billingMethod is "fixed_fee_milestone" or "hybrid_time_and_milestone"
            ? "milestone_schedule_required_not_configured"
            : "not_required_for_time_and_materials";

        var liveSyncEnabled = false;
        var cutoverEnabled = true;
        var connectorReady = true;
        var cutoverReady = rateReady;
        var source = customerSource.IsManual
            ? "MANUAL"
            : customerSource.ProviderName;
        var readiness = rateReady
            ? "manual_source_active"
            : "commercial_rate_missing";
        DateTimeOffset? lastSuccessfulSyncAt = customerSource.LastSuccessfulCustomerSyncAt;

        if (customerSource.IsSell)
        {
            connector = await LoadConnectorAsync(connection, transaction);
            liveSyncEnabled = ReadFlag("PROJECTPULSE_SELL_LIVE_SYNC_ENABLED");
            cutoverEnabled = ReadFlag("PROJECTPULSE_SELL_COMMERCIAL_READ_MODEL_ACTIVE");
            connectorReady = connector is not null
                && connector.InboundEnabled
                && connector.ConnectionStatus is "configured" or "connected"
                && connector.LastSuccessfulSyncAt is not null;
            var quoteReady = !string.IsNullOrWhiteSpace(project.SellQuoteNumber);
            cutoverReady = connectorReady && quoteReady && rateReady;
            source = cutoverEnabled && cutoverReady
                ? "SELL"
                : "current_stored_rates";
            readiness = !connectorReady
                ? "sell_connector_not_ready"
                : !quoteReady
                    ? "sell_quote_missing"
                    : !rateReady
                        ? "commercial_rate_missing"
                        : cutoverEnabled
                            ? "sell_active"
                            : "sell_ready_for_guarded_cutover";
            lastSuccessfulSyncAt = connector?.LastSuccessfulSyncAt;
        }
        else if (!customerSource.IsManual)
        {
            liveSyncEnabled = customerSource.ProviderReady;
            connectorReady = customerSource.ProviderReady;
            cutoverReady = connectorReady && rateReady;
            source = string.IsNullOrWhiteSpace(customerSource.ProviderName)
                ? customerSource.ProviderKey ?? "CRM"
                : customerSource.ProviderName;
            readiness = !connectorReady
                ? "customer_source_not_ready"
                : !rateReady
                    ? "commercial_rate_missing"
                    : "crm_source_active";
        }
'''

if text.count(old_block) != 1:
    raise SystemExit('Expected exactly one SELL commercial readiness block to replace.')
text = text.replace(old_block, new_block)

old_sync = '            connector?.LastSuccessfulSyncAt,\n'
new_sync = '            lastSuccessfulSyncAt,\n'
if text.count(old_sync) != 1:
    raise SystemExit('Expected exactly one commercial last-successful-sync constructor argument.')
text = text.replace(old_sync, new_sync)

marker = 'public static class SellCommercialReadModelModule\n'
if marker not in text:
    raise SystemExit('Sell commercial read model marker is missing.')

output_path.parent.mkdir(parents=True, exist_ok=True)
output_path.write_text(text, encoding='utf-8')
