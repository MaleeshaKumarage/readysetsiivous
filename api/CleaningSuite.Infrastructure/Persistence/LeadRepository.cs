using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using CleaningSuite.Application.Leads;
using CleaningSuite.Domain.Companies;
using CleaningSuite.Domain.Leads;
using Marten;
using Microsoft.Extensions.Configuration;
using Npgsql;
namespace CleaningSuite.Infrastructure.Persistence;

public sealed class LeadRepository(ITenantSession tenant,IConfiguration config) : ILeadRepository
{
    private IDocumentSession Session=>tenant.Session;
    public Task<Lead?> GetAsync(Guid id,CancellationToken ct)=>Session.LoadAsync<Lead>(id,ct);
    public async Task<(IReadOnlyList<Lead> Items,int Total)> ListAsync(string search,string status,int skip,int take,CancellationToken ct)
    {
        IQueryable<Lead> query=Session.Query<Lead>();
        query=status=="Converted" ? query.Where(x=>x.CompanyId!=null) : query.Where(x=>x.CompanyId==null);
        if(status!="Converted") query=status=="Inactive"?query.Where(x=>!x.IsActive):query.Where(x=>x.IsActive);
        if(status is not ("Active" or "Inactive" or "Converted")) query=query.Where(x=>x.Fields.Status==status);
        if(search.Length>0) query=query.Where(x=>x.Fields.Name.Contains(search,StringComparison.OrdinalIgnoreCase)
            || x.Fields.BusinessId.Contains(search,StringComparison.OrdinalIgnoreCase)
            || x.Fields.Emails.Any(email=>email.Contains(search,StringComparison.OrdinalIgnoreCase)));
        var total=await query.CountAsync(ct);
        return (await query.OrderByDescending(x=>x.UpdatedUtc).ThenBy(x=>x.Id).Skip(skip).Take(take).ToListAsync(ct),total);
    }
    public async Task SaveAsync(Lead lead,CancellationToken ct) { await LeadCollaborationRepository.CancelForLeadAsync(Session,lead,false,ct);Session.Store(lead);await Session.SaveChangesAsync(ct); }
    public async Task DeleteAsync(Lead lead,CancellationToken ct) { await LeadCollaborationRepository.CancelForLeadAsync(Session,lead,true,ct);Session.Delete(lead);await Session.SaveChangesAsync(ct); }
    public async Task<bool> CustomerAvailableAsync(Guid companyId,IReadOnlyList<Guid> branchIds,CancellationToken ct) {
        if(await Session.LoadAsync<Company>(companyId,ct) is null) return false;
        foreach(var id in branchIds) if((await Session.LoadAsync<Branch>(id,ct))?.CompanyId!=companyId) return false;
        return true;
    }
    public async Task ConvertAsync(Lead lead,Company company,IReadOnlyList<Branch> branches,CancellationToken ct)
    {
        await LeadCollaborationRepository.CancelForLeadAsync(Session,lead,false,ct);Session.Store(company);foreach(var branch in branches) Session.Store(branch);Session.Store(lead);
        try { await Session.SaveChangesAsync(ct); }
        catch(Exception e) when(UniqueConflict(e)) { throw new LeadConflictException("A customer with this business ID already exists, including inactive customers. No new customer was created."); }
    }
    private static bool UniqueConflict(Exception e)=>e is PostgresException { SqlState:PostgresErrorCodes.UniqueViolation,ConstraintName:MartenConfig.CompanyBusinessIdIndexName }
        || e.InnerException is not null && UniqueConflict(e.InnerException);
    public async Task<IAsyncDisposable> LockAsync(Guid id,CancellationToken ct)
    {
        var key=BinaryPrimitives.ReadInt64BigEndian(SHA256.HashData(Encoding.UTF8.GetBytes($"lead:{tenant.TenantId}:{id:N}")));
        var connection=new NpgsqlConnection(config.GetConnectionString("DefaultConnection"));
        try {
            await connection.OpenAsync(ct);
            await using var command=new NpgsqlCommand("SET lock_timeout = '5s'; SELECT pg_advisory_lock(@key)",connection);
            command.Parameters.AddWithValue("key",key);await command.ExecuteNonQueryAsync(ct);tenant.Reset();return new Lease(connection,key);
        } catch(PostgresException e) when(e.SqlState==PostgresErrorCodes.LockNotAvailable) { await connection.DisposeAsync();throw new LeadConflictException("This lead is being updated. Refresh and retry."); }
        catch { await connection.DisposeAsync();throw; }
    }
    private sealed class Lease(NpgsqlConnection connection,long key) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync() { try { await using var cmd=new NpgsqlCommand("SELECT pg_advisory_unlock(@key)",connection);cmd.Parameters.AddWithValue("key",key);await cmd.ExecuteNonQueryAsync(); } finally { await connection.DisposeAsync(); } }
    }
}
