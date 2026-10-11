namespace CleaningSuite.Infrastructure.Persistence;

public static class TenantIds
{
    /// <summary>Partition that holds TenantRegistration documents for all tenants.</summary>
    public const string Registry = "registry";

    /// <summary>Partition that holds global entertainment game documents.</summary>
    public const string Entertainment = "entertainment";
}
