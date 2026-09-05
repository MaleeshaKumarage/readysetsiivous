using System.Text.Json;
using CleaningSuite.Api.Auth;
using CleaningSuite.Api.Tenancy;
using CleaningSuite.Application;
using CleaningSuite.Application.Tenants;
using CleaningSuite.Application.Validation;
using CleaningSuite.Infrastructure.Auth;
using CleaningSuite.Infrastructure.Keycloak;
using CleaningSuite.Infrastructure.Persistence;
using FluentValidation;
using JasperFx;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.OpenApi.Models;

// Load .env before config builds. dotnet run executes with the project dir as cwd,
// so look in the project dir first, then the repo root. No-op if absent.
foreach (var envPath in new[] { ".env", Path.Combine("..", ".env") })
{
    if (File.Exists(envPath))
    {
        DotNetEnv.Env.Load(envPath);
        break;
    }
}

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddHttpContextAccessor();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "Cleaning Suite API", Version = "v1" });
});

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("ConnectionStrings__DefaultConnection missing");

var autoCreate = builder.Configuration.GetValue<AutoCreate?>("Marten:AutoCreateSchemaMode")
    ?? AutoCreate.CreateOrUpdate;

builder.Services.AddCleaningMarten(connectionString, autoCreate);

builder.Services.AddKeycloakAuth(builder.Configuration);

// Keycloak admin access for tenant provisioning.
builder.Services.AddHttpClient("KeycloakAdmin");
builder.Services.AddSingleton(sp =>
{
    var section = sp.GetRequiredService<IConfiguration>().GetSection("Auth:Keycloak");
    var http = sp.GetRequiredService<IHttpClientFactory>().CreateClient("KeycloakAdmin");
    return new KeycloakAdminClient(
        http,
        section["ServerUrl"] ?? throw new InvalidOperationException("Auth:Keycloak:ServerUrl missing"),
        section["ProvisionerRealm"] ?? "master",
        section["ProvisionerClientId"] ?? "cleaning-provisioner",
        section["ProvisionerClientSecret"] ?? "",
        section["AdminUser"],
        section["AdminPassword"]);
});
builder.Services.AddSingleton<IKeycloakProvisioner, KeycloakProvisioner>();

builder.Services.AddSingleton<ITenantStatusCache, CleaningSuite.Api.Tenancy.TenantStatusCache>();
builder.Services.AddScoped<ITenantContext, HttpTenantContext>();
builder.Services.AddScoped<ITenantSession, TenantSessionFactory>();
builder.Services.AddScoped<ITenantRegistry, TenantRegistry>();
builder.Services.AddScoped<ITenantProfileRepository, TenantProfileRepository>();
builder.Services.AddScoped<CleaningSuite.Application.Services.IServiceRepository, CleaningSuite.Infrastructure.Persistence.ServiceRepository>();
builder.Services.AddScoped<CleaningSuite.Application.Bookings.IBookingRepository, CleaningSuite.Infrastructure.Persistence.BookingRepository>();
builder.Services.AddScoped<CleaningSuite.Application.Bookings.AvailabilityEngine>();
builder.Services.AddScoped<CleaningSuite.Application.Employees.IEmployeeRepository, CleaningSuite.Infrastructure.Persistence.EmployeeRepository>();
builder.Services.AddScoped<CleaningSuite.Application.Invoicing.IInvoiceRepository, CleaningSuite.Infrastructure.Persistence.InvoiceRepository>();

builder.Services.AddMediatR(cfg =>
{
    cfg.RegisterServicesFromAssembly(typeof(AssemblyMarker).Assembly);
    cfg.AddOpenBehavior(typeof(ValidatorBehavior<,>));
});
builder.Services.AddValidatorsFromAssembly(typeof(AssemblyMarker).Assembly);

builder.Services.AddAuthorization(options =>
    options.AddRealmRolePolicies("admin", "employee"));
builder.Services.AddSingleton<IAuthorizationHandler, RealmRoleHandler>();

var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? ["http://localhost:3000"];

builder.Services.AddCors(options =>
{
    options.AddPolicy("SiteOrigins", policy =>
        policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod());
});

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ValidationExceptionHandler>();
builder.Services.AddExceptionHandler<DefaultExceptionHandler>();

var app = builder.Build();

// Health check: liveness only, no DB dependency.
app.MapGet("/healthz", () => Results.Ok(new { status = "ok", utc = DateTime.UtcNow }));

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseExceptionHandler();
app.UseCors("SiteOrigins");
app.UseMiddleware<CleaningSuite.Api.Middleware.PublicRateLimitMiddleware>();
app.UseAuthentication();
app.UseMiddleware<TenantResolutionMiddleware>();
app.UseAuthorization();
app.MapControllers();

app.Run();

public partial class Program;

/// <summary>Maps FluentValidation failures to RFC 7807 problem details.</summary>
public class ValidationExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext context,
        Exception exception,
        CancellationToken ct)
    {
        if (exception is not ValidationException validationException)
            return false;

        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        await context.Response.WriteAsJsonAsync(new ValidationProblemDetails
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "Validation failed",
            Errors = validationException.Errors
                .GroupBy(e => e.PropertyName, e => e.ErrorMessage)
                .ToDictionary(g => g.Key, g => g.ToArray()),
        }, ct);
        return true;
    }
}

/// <summary>Fallback: RFC 7807 problem details for everything else.</summary>
public class DefaultExceptionHandler : IExceptionHandler
{
    private readonly ILogger<DefaultExceptionHandler> _logger;

    public DefaultExceptionHandler(ILogger<DefaultExceptionHandler> logger) => _logger = logger;

    public async ValueTask<bool> TryHandleAsync(
        HttpContext context,
        Exception exception,
        CancellationToken ct)
    {
        var (status, title) = exception switch
        {
            CleaningSuite.Application.Common.SlugConflictException => (409, "Slug already exists"),
            CleaningSuite.Application.Common.NotFoundException => (404, "Not found"),
            CleaningSuite.Application.Bookings.SlotConflictException => (409, "Slot conflict"),
            UnauthorizedAccessException => (403, "Forbidden"),
            _ => (500, "Internal server error"),
        };

        if (status == 500)
            _logger.LogError(exception, "Unhandled exception for {Path}", context.Request.Path);

        context.Response.StatusCode = status;
        await context.Response.WriteAsJsonAsync(new ProblemDetails
        {
            Status = status,
            Title = title,
        }, ct);
        return true;
    }
}
