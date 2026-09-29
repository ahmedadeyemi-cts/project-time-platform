using Npgsql;

namespace ProjectTime.Api.Modules;

// Serialize configuration and credential use across API instances and the worker.
internal sealed class CrmProviderOperationLease : IAsyncDisposable
{
    private readonly NpgsqlConnection connection;
    private readonly string key;
    private CrmProviderOperationLease(NpgsqlConnection connection, string key)
        => (this.connection, this.key) = (connection, key);

    internal static async Task<CrmProviderOperationLease?> TryAcquireAsync(
        NpgsqlConnection connection, string providerKey, CancellationToken cancellationToken)
    {
        var key = $"module026-oauth-refresh:{providerKey}";
        await using var command = new NpgsqlCommand(
            "SELECT pg_try_advisory_lock(hashtext(@lock_key));", connection);
        command.Parameters.AddWithValue("lock_key", key);
        return await command.ExecuteScalarAsync(cancellationToken) is true
            ? new CrmProviderOperationLease(connection, key) : null;
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await using var command = new NpgsqlCommand(
                "SELECT pg_advisory_unlock(hashtext(@lock_key));", connection);
            command.Parameters.AddWithValue("lock_key", key);
            await command.ExecuteScalarAsync(CancellationToken.None);
        }
        catch
        {
            // Do not return an uncertain session lock to the connection pool.
            NpgsqlConnection.ClearPool(connection);
        }
    }
}
