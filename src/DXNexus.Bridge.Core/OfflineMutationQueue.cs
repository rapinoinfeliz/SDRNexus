using Microsoft.Data.Sqlite;

namespace DXNexus.Bridge.Core;

public sealed record QueuedMutation(Guid Id, string Type, string PayloadJson, int Attempts);

public sealed class OfflineMutationQueue(string? databasePath = null) : IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _databasePath = databasePath ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DXNexus",
        "offline-mutations.sqlite3");
    private bool _initialized;

    private string ConnectionString => new SqliteConnectionStringBuilder
    {
        DataSource = _databasePath,
        Mode = SqliteOpenMode.ReadWriteCreate,
        Cache = SqliteCacheMode.Shared,
    }.ToString();

    public async Task EnqueueAsync(Guid id, string type, string payloadJson, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        ArgumentException.ThrowIfNullOrWhiteSpace(payloadJson);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
            await using var connection = new SqliteConnection(ConnectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO queued_mutations(id, type, payload_json, created_at, attempts, next_attempt_at)
                VALUES ($id, $type, $payload, $now, 0, $now)
                ON CONFLICT(id) DO NOTHING
                """;
            command.Parameters.AddWithValue("$id", id.ToString());
            command.Parameters.AddWithValue("$type", type);
            command.Parameters.AddWithValue("$payload", payloadJson);
            command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    public async Task<IReadOnlyList<QueuedMutation>> DueAsync(int limit = 20, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
            await using var connection = new SqliteConnection(ConnectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            var command = connection.CreateCommand();
            command.CommandText = """
                SELECT id, type, payload_json, attempts FROM queued_mutations
                WHERE next_attempt_at <= $now ORDER BY created_at LIMIT $limit
                """;
            command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
            command.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 100));
            var result = new List<QueuedMutation>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                result.Add(new QueuedMutation(
                    Guid.Parse(reader.GetString(0)), reader.GetString(1), reader.GetString(2), reader.GetInt32(3)));
            }
            return result;
        }
        finally { _gate.Release(); }
    }

    public Task CompleteAsync(Guid id, CancellationToken cancellationToken = default) =>
        ExecuteAsync("DELETE FROM queued_mutations WHERE id = $id", id, null, cancellationToken);

    public Task RetryLaterAsync(Guid id, int attempts, CancellationToken cancellationToken = default)
    {
        var delaySeconds = Math.Min(900, 5 * Math.Pow(2, Math.Min(8, Math.Max(0, attempts))));
        return ExecuteAsync(
            "UPDATE queued_mutations SET attempts = attempts + 1, next_attempt_at = $next WHERE id = $id",
            id,
            DateTimeOffset.UtcNow.AddSeconds(delaySeconds).ToString("O"),
            cancellationToken);
    }

    private async Task ExecuteAsync(string sql, Guid id, string? next, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
            await using var connection = new SqliteConnection(ConnectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            var command = connection.CreateCommand();
            command.CommandText = sql;
            command.Parameters.AddWithValue("$id", id.ToString());
            if (next is not null) command.Parameters.AddWithValue("$next", next);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (_initialized) return;
        Directory.CreateDirectory(Path.GetDirectoryName(_databasePath)
            ?? throw new InvalidOperationException("Queue path has no parent directory."));
        await using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode=WAL;
            PRAGMA busy_timeout=5000;
            CREATE TABLE IF NOT EXISTS queued_mutations(
              id TEXT PRIMARY KEY,
              type TEXT NOT NULL CHECK(type IN ('wishlist', 'logbook')),
              payload_json TEXT NOT NULL,
              created_at TEXT NOT NULL,
              attempts INTEGER NOT NULL DEFAULT 0,
              next_attempt_at TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS queued_mutations_due_idx ON queued_mutations(next_attempt_at, created_at);
            """;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        _initialized = true;
    }

    public ValueTask DisposeAsync()
    {
        _gate.Dispose();
        return ValueTask.CompletedTask;
    }
}
