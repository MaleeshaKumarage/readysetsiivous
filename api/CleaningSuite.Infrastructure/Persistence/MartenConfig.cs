using CleaningSuite.Domain.Agreements;
using CleaningSuite.Domain.Bookings;
using CleaningSuite.Domain.Common;
using CleaningSuite.Domain.Companies;
using CleaningSuite.Domain.Employees;
using CleaningSuite.Domain.Entertainment;
using CleaningSuite.Domain.Invoicing;
using CleaningSuite.Domain.QualityCycle;
using CleaningSuite.Domain.Services;
using CleaningSuite.Domain.Shifts;
using CleaningSuite.Domain.Tenants;
using CleaningSuite.Infrastructure.Entertainment;
using JasperFx;
using Marten;
using Marten.Newtonsoft;
using Microsoft.Extensions.DependencyInjection;

namespace CleaningSuite.Infrastructure.Persistence;

public static class MartenConfig
{
    public const string CompanyBusinessIdIndexName = "mt_doc_company_uidx_business_id";
    /// <summary>
    /// Registers Marten with conjoined multi-tenancy. Every document lives in a
    /// tenant partition; the "registry" partition holds TenantRegistration docs.
    /// </summary>
    public static IServiceCollection AddCleaningMarten(
        this IServiceCollection services,
        string connectionString,
        AutoCreate autoCreate = AutoCreate.CreateOrUpdate,
        string? databaseSchemaName = null)
    {
        var options = new StoreOptions();
        if (databaseSchemaName is not null) options.DatabaseSchemaName = databaseSchemaName;
        // Keep existing JSON, document/session behavior and event schema defaults
        // while taking the patched LINQ provider and .NET10-compatible runtime.
        options.RestoreV8Defaults();
        options.UseNewtonsoftForSerialization();
        options.Connection(connectionString);
        options.AutoCreateSchemaObjects = autoCreate;

        // Conjoined tenancy: shared tables with tenant_id column.
        options.Policies.AllDocumentsAreMultiTenanted();

        options.Policies.ForAllDocuments(doc =>
            doc.UseOptimisticConcurrency = true);

        options.Schema.For<Booking>()
            .Index(x => x.StartUtc)
            .Index(x => x.EmployeeId)
            .Index(x => x.BookingNumber);

        options.Schema.For<Service>().Index(x => x.Slug);
        options.Schema.For<Invoice>().Index(x => x.InvoiceNumber);
        options.Schema.For<ShiftBillingProfile>();
        options.Schema.For<Employee>().Index(x => x.KeycloakUserId).UniqueIndex(Marten.Schema.UniqueIndexType.Computed, "mt_doc_employee_uidx_keycloak_identity_key", Marten.Schema.Indexing.Unique.TenancyScope.PerTenant, x => x.KeycloakIdentityKey!);

        options.Schema.For<Company>()
            .UniqueIndex(Marten.Schema.UniqueIndexType.Computed, CompanyBusinessIdIndexName,
                Marten.Schema.Indexing.Unique.TenancyScope.PerTenant, x => x.BusinessId)
            .Index(x => x.Name);

        options.Schema.For<Branch>()
            .Index(x => x.CompanyId)
            .Index(x => x.Name);

        options.Schema.For<Shift>()
            .Index(x => x.CompanyId)
            .Index(x => x.BranchId);

        options.Schema.For<DeletedShift>();
        options.Schema.For<ShiftOccurrenceAssignment>().Index(x => x.ShiftId);

        options.Schema.For<ShiftAssignment>()
            .Index(x => x.ShiftId)
            .Index(x => x.EmployeeId);

        options.Schema.For<ShiftOccurrenceException>()
            .Index(x => x.ShiftId)
            .Index(x => x.OriginalStartUtc)
            .Index(x => x.StartUtc);

        options.Schema.For<QualityCycleTemplate>()
            .Index(x => x.CompanyId)
            .Index(x => x.BranchId);

        options.Schema.For<QualityCycleForm>()
            .UniqueIndex(x => x.Token)
            .Index(x => x.ShiftId)
            .Index(x => x.EmployeeId)
            .Index(x => x.ShiftOccurrenceUtc);

        options.Schema.For<QualityCycleReportShare>().UniqueIndex(x => x.Token);
        options.Schema.For<EntertainmentGroup>()
            .UniqueIndex(x => x.JoinKey);

        options.Schema.For<EntertainmentPlayer>()
            .Index(x => x.GroupId);

        options.Schema.For<EntertainmentRound>()
            .Index(x => x.GroupId);
        options.Schema.For<EntertainmentSentenceReservation>().Index(x => x.GroupId);
        // Account-level provider quota timestamps shared by all game groups.
        options.Schema.For<EntertainmentAiUsageWindow>();

        // Include documents otherwise discovered only on their first repository call.
        options.Schema.For<Agreement>();
        options.Schema.For<AgreementAssociationRepairAudit>();
        options.Schema.For<TenantProfile>();
        options.Schema.For<Counter>();

        // Registry partition: unique slug across all tenants.
        options.Schema.For<TenantRegistration>().UniqueIndex(x => x.Slug);

        services.AddMarten(options).UseIdentitySessions();

        return services;
    }
}
