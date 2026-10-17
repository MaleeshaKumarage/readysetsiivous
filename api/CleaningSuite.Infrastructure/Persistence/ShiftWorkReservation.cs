using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using CleaningSuite.Application.Shifts;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace CleaningSuite.Infrastructure.Persistence;

/// <summary>One scheduling reservation per tenant, shared across API replicas.</summary>
public sealed class ShiftWorkReservation(IConfiguration configuration, ITenantSession session) : IShiftWorkReservation
{
    public async Task<IAsyncDisposable> AcquireAsync(CancellationToken ct = default)
    {
        var key = BinaryPrimitives.ReadInt64BigEndian(SHA256.HashData(Encoding.UTF8.GetBytes($"shift-work:{session.TenantId}")));
        var connection = new NpgsqlConnection(configuration.GetConnectionString("DefaultConnection"));
        Lease? lease = null;
        try
        {
            await connection.OpenAsync(ct);
            await using var command = new NpgsqlCommand("SET lock_timeout = '5s'; SELECT pg_advisory_lock(@key)", connection);
            command.Parameters.AddWithValue("key", key);
            await command.ExecuteNonQueryAsync(ct);
            lease = new Lease(connection, key);
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

    private sealed class Lease(NpgsqlConnection connection, long key) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
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
