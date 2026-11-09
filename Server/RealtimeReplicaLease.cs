using Npgsql;

/// <summary>
/// Holds a PostgreSQL advisory lock for the lifetime of the realtime authority.
/// Room and reconnect state is process-local, so a second authority using the
/// same database would split matchmaking and make reconnect routing unsafe.
/// </summary>
public sealed class RealtimeReplicaLease : IDisposable
{
    private const long LockKey = 0x42554D504552; // "BUMPER"
    private readonly NpgsqlConnection connection;
    private bool disposed;

    private RealtimeReplicaLease(NpgsqlConnection connection) => this.connection = connection;

    public static RealtimeReplicaLease Acquire(string connectionString)
    {
        // Advisory locks belong to the PostgreSQL session. Disable Npgsql
        // pooling so disposing this dedicated lifetime connection really ends
        // its session and releases the lock before an in-process reacquire.
        var leaseConnectionString = new NpgsqlConnectionStringBuilder(connectionString) { Pooling = false }.ConnectionString;
        var connection = new NpgsqlConnection(leaseConnectionString);
        try
        {
            connection.Open();
            using var command = new NpgsqlCommand("SELECT pg_try_advisory_lock(@lockKey)", connection);
            command.Parameters.AddWithValue("lockKey", LockKey);
            if (command.ExecuteScalar() is not true)
                throw new InvalidOperationException("Another realtime server instance already owns this database. Realtime rooms are process-local; run one replica until distributed room ownership is configured.");
            return new RealtimeReplicaLease(connection);
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    public void StartWatchdog(IHostApplicationLifetime applicationLifetime)
    {
        _ = WatchConnectionAsync(applicationLifetime);
    }

    private async Task WatchConnectionAsync(IHostApplicationLifetime applicationLifetime)
    {
        var stopping = applicationLifetime.ApplicationStopping;
        try
        {
            while (!stopping.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(5), stopping);
                await using var command = new NpgsqlCommand("SELECT 1", connection) { CommandTimeout = 5 };
                await command.ExecuteScalarAsync(stopping);
            }
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested) { }
        catch (Exception)
        {
            // If this session dies, PostgreSQL releases its lock. Stop accepting
            // traffic before another instance can start its own process-local rooms.
            applicationLifetime.StopApplication();
        }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        connection.Dispose(); // PostgreSQL releases the advisory lock with the session.
    }
}
