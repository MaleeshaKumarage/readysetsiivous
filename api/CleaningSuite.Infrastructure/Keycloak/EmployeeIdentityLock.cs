using System.Security.Cryptography;
using System.Text;
using CleaningSuite.Application.Employees;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace CleaningSuite.Infrastructure.Keycloak;
/// <summary>Session advisory lock; serializes account claims across API replicas.</summary>
public sealed class EmployeeIdentityLock(IConfiguration configuration) : IEmployeeIdentityLock
{
    public async Task<IAsyncDisposable> AcquireAsync(string tenantId, string email, CancellationToken ct = default)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"employee-identity:{tenantId}:{email.Trim().ToUpperInvariant()}"));
        var key = System.Buffers.Binary.BinaryPrimitives.ReadInt64BigEndian(bytes);
        var connection = new NpgsqlConnection(configuration.GetConnectionString("DefaultConnection"));
        try {
            await connection.OpenAsync(ct);
            await using var command = new NpgsqlCommand("SELECT pg_advisory_lock(@key)", connection);
            command.Parameters.AddWithValue("key", key); await command.ExecuteNonQueryAsync(ct);
            return new Lease(connection, key);
        } catch { await connection.DisposeAsync(); throw; }
    }
    private sealed class Lease(NpgsqlConnection connection, long key) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync() {
            try { await using var command = new NpgsqlCommand("SELECT pg_advisory_unlock(@key)", connection);
                command.Parameters.AddWithValue("key", key); await command.ExecuteNonQueryAsync();
            } finally { await connection.DisposeAsync(); }
        }
    }
}
