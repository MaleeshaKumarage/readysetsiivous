using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using CleaningSuite.Application.Shifts;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace CleaningSuite.Infrastructure.Persistence;

/// <summary>
/// One scheduling reservation per tenant, shared across API replicas.
/// It is re-entrant within one instance (one request scope): a pipeline behaviour and the handler it wraps
/// may both acquire it, and the nested acquisition shares the outer hold instead of waiting on a second
/// connection for a lock the same request already holds. Between instances it stays exclusive.
/// </summary>
public sealed class ShiftWorkReservation(IConfiguration configuration, ITenantSession session) : IShiftWorkReservation
{
    // Holds taken by this instance. Only the outermost lease owns the database lock.
    private int _depth;

    public async Task<IAsyncDisposable> AcquireAsync(CancellationToken ct = default)
    {
        if (_depth > 0)
        {
            _depth++;
            return new NestedLease(this);
        }
        var key = BinaryPrimitives.ReadInt64BigEndian(SHA256.HashData(Encoding.UTF8.GetBytes($"shift-work:{session.TenantId}")));
        var connection = new NpgsqlConnection(configuration.GetConnectionString("DefaultConnection"));
        Lease? lease = null;
        try
        {
            await connection.OpenAsync(ct);
            await using var command = new NpgsqlCommand("SET lock_timeout = '5s'; SELECT pg_advisory_lock(@key)", connection);
            command.Parameters.AddWithValue("key", key);
            await command.ExecuteNonQueryAsync(ct);
            lease = new Lease(this, connection, key);
            _depth = 1;
            await session.BeginReservationAsync(ct);
            return lease;
        }
        catch (PostgresException error) when (error.SqlState == PostgresErrorCodes.LockNotAvailable)
        {
            if (lease is not null) await lease.DisposeAsync(); else await connection.DisposeAsync();
            throw new ShiftConflictException("Scheduling is being updated. Reload and retry.");
        }
        catch
        {
            if (lease is not null) await lease.DisposeAsync(); else await connection.DisposeAsync();
            throw;
        }
    }

    private sealed class NestedLease(ShiftWorkReservation owner) : IAsyncDisposable
    {
        private bool _released;
        public ValueTask DisposeAsync()
        {
            if (!_released) { _released = true; if (owner._depth > 1) owner._depth--; }
            return ValueTask.CompletedTask;
        }
    }

    private sealed class Lease(ShiftWorkReservation owner, NpgsqlConnection connection, long key) : IAsyncDisposable
    {
        private bool _released;
        public async ValueTask DisposeAsync()
        {
            if (_released) return;
            _released = true;
            owner._depth = 0;
            try
            {
                await using var command = new NpgsqlCommand("SELECT pg_advisory_unlock(@key)", connection);
                command.Parameters.AddWithValue("key", key);
                await command.ExecuteNonQueryAsync();
            }
            finally { await connection.DisposeAsync(); }
        }
    }
}
