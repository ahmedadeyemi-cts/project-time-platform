using Npgsql;

namespace ProjectTime.Api.Ai;

public sealed record CelarAiMonitorLeadershipState(
    bool Leader,
    string InstanceId,
    DateTimeOffset EvaluatedAt,
    string State);

/// <summary>
/// PostgreSQL advisory-lock leadership prevents every API replica from running
/// the same scheduled probes. The open connection is the fencing token; loss of
/// the session releases leadership automatically.
/// </summary>
public sealed class CelarAiMonitorLeadershipService
{
    private const long AdvisoryLockKey = 0x43454C41524D3037; // CELARM07
    private readonly string _instanceId =
        CelarAiOperationsPolicy.Clean(
            Environment.GetEnvironmentVariable("CONTAINER_APP_REPLICA_NAME")
                ?? Environment.GetEnvironmentVariable("HOSTNAME")
                ?? Environment.MachineName,
            160);

    public async Task<CelarAiMonitorLease?> TryAcquireAsync(CancellationToken cancellationToken)
    {
        var connection = await CelarAiOperationsDatabase.OpenReadyAsync(cancellationToken);
        try
        {
            await using var command = new NpgsqlCommand(
                "SELECT pg_try_advisory_lock(@key);",
                connection);
            command.Parameters.AddWithValue("key", AdvisoryLockKey);
            var acquired = await command.ExecuteScalarAsync(cancellationToken) is true;
            if (!acquired)
            {
                await UpdateHeartbeatAsync(connection, false, cancellationToken);
                await connection.DisposeAsync();
                return null;
            }

            await UpdateHeartbeatAsync(connection, true, cancellationToken);
            return new CelarAiMonitorLease(connection, AdvisoryLockKey, _instanceId);
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    public CelarAiMonitorLeadershipState StandbyState() => new(
        Leader: false,
        _instanceId,
        DateTimeOffset.UtcNow,
        "healthy_standby");

    private async Task UpdateHeartbeatAsync(
        NpgsqlConnection connection,
        bool leader,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            INSERT INTO module076_monitor_heartbeats(
                instance_id,is_leader,last_seen_at,last_cycle_started_at,last_state)
            VALUES(@instance,@leader,NOW(),CASE WHEN @leader THEN NOW() ELSE NULL END,
                   CASE WHEN @leader THEN 'leader_acquired' ELSE 'healthy_standby' END)
            ON CONFLICT (instance_id) DO UPDATE
            SET is_leader=EXCLUDED.is_leader,
                last_seen_at=EXCLUDED.last_seen_at,
                last_cycle_started_at=CASE
                    WHEN EXCLUDED.is_leader THEN EXCLUDED.last_cycle_started_at
                    ELSE module076_monitor_heartbeats.last_cycle_started_at
                END,
                last_state=EXCLUDED.last_state;
            """, connection);
        command.Parameters.AddWithValue("instance", _instanceId);
        command.Parameters.AddWithValue("leader", leader);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}

public sealed class CelarAiMonitorLease : IAsyncDisposable
{
    private readonly NpgsqlConnection _connection;
    private readonly long _key;
    private readonly string _instanceId;
    private bool _disposed;

    internal CelarAiMonitorLease(NpgsqlConnection connection, long key, string instanceId)
    {
        _connection = connection;
        _key = key;
        _instanceId = instanceId;
    }

    public string InstanceId => _instanceId;

    public async Task CompleteCycleAsync(
        string state,
        string? diagnosticCode,
        CancellationToken cancellationToken)
    {
        if (_disposed) return;
        await using var command = new NpgsqlCommand("""
            UPDATE module076_monitor_heartbeats
            SET last_seen_at=NOW(),last_cycle_completed_at=NOW(),last_state=@state,
                last_error_code=@code,is_leader=TRUE
            WHERE instance_id=@instance;
            """, _connection);
        command.Parameters.AddWithValue("state", CelarAiOperationsPolicy.Clean(state, 60));
        command.Parameters.AddWithValue("code", CelarAiOperationsPolicy.Clean(diagnosticCode, 120));
        command.Parameters.AddWithValue("instance", _instanceId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            await using var heartbeat = new NpgsqlCommand("""
                UPDATE module076_monitor_heartbeats
                SET is_leader=FALSE,last_seen_at=NOW(),last_state='healthy_standby'
                WHERE instance_id=@instance;
                """, _connection);
            heartbeat.Parameters.AddWithValue("instance", _instanceId);
            await heartbeat.ExecuteNonQueryAsync();
            await using var unlock = new NpgsqlCommand(
                "SELECT pg_advisory_unlock(@key);",
                _connection);
            unlock.Parameters.AddWithValue("key", _key);
            await unlock.ExecuteScalarAsync();
        }
        catch
        {
            // Closing the PostgreSQL session is the authoritative lock release.
        }
        finally
        {
            await _connection.DisposeAsync();
        }
    }
}
