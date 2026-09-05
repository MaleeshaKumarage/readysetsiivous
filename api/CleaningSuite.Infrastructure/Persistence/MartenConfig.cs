using CleaningSuite.Domain.Bookings;
using CleaningSuite.Domain.Common;
using CleaningSuite.Domain.Employees;
using CleaningSuite.Domain.Invoicing;
using CleaningSuite.Domain.Services;
using CleaningSuite.Domain.Tenants;
using JasperFx;
using Marten;
using Microsoft.Extensions.DependencyInjection;

namespace CleaningSuite.Infrastructure.Persistence;

public static class MartenConfig
{
    /// <summary>
    /// Registers Marten with conjoined multi-tenancy. Every document lives in a
    /// tenant partition; the "registry" partition holds TenantRegistration docs.
    /// </summary>
    public static IServiceCollection AddCleaningMarten(
        this IServiceCollection services,
        string connectionString,
        AutoCreate autoCreate = AutoCreate.CreateOrUpdate)
    {
        var options = new StoreOptions();
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
        options.Schema.For<Employee>().Index(x => x.KeycloakUserId);

        // Registry partition: unique slug across all tenants.
        options.Schema.For<TenantRegistration>().UniqueIndex(x => x.Slug);

        services.AddMarten(options);

        return services;
    }
}
