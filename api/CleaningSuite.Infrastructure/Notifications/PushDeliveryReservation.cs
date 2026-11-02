using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using CleaningSuite.Application.Notifications;
using Microsoft.Extensions.Configuration;
using Npgsql;

using CleaningSuite.Infrastructure.Persistence;
namespace CleaningSuite.Infrastructure.Notifications;

/// <summary>Serializes one intent without reserving unrelated tenant scheduling while Resend responds.</summary>
public sealed class PushDeliveryReservation(IConfiguration configuration, ITenantSession session) : IPushDeliveryReservation
{
    public async Task<IAsyncDisposable?> TryAcquireAsync(Guid notificationId, CancellationToken ct = default)
    {
        var key = BinaryPrimitives.ReadInt64BigEndian(SHA256.HashData(
            Encoding.UTF8.GetBytes($"push-delivery:{session.TenantId}:{notificationId:N}")));
        var connection = new NpgsqlConnection(configuration.GetConnectionString("DefaultConnection"));
        try
        {
            await connection.OpenAsync(ct);
            await using var command = new NpgsqlCommand("SELECT pg_try_advisory_lock(@key)", connection);
            command.Parameters.AddWithValue("key", key);
            if (await command.ExecuteScalarAsync(ct) is true) return new Lease(connection, key);
            await connection.DisposeAsync();
            return null;
        }
        catch { await connection.DisposeAsync(); throw; }
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

