using Helm.Core;
using Helm.Core.Api;
using Helm.Core.Auth;
using Helm.Core.Data;
using Helm.Core.Jobs;
using Helm.Core.Messaging;
using Helm.Crm;
using Microsoft.EntityFrameworkCore;
using Helm.Cpq;
using Helm.ProposalOrder;
using Helm.Design;
using Helm.Planning;
using Helm.Inventory;
using Helm.PreProcessing;
using Helm.Manufacturing;
using Helm.Fulfilment;
using Helm.AfterSales;
using Helm.Bff.Internal;
using Helm.Bff.Portal;
using Helm.Bff.Kiosk;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<CoreDbContext>(o => o
    .UseNpgsql(builder.Configuration.GetConnectionString("Helm"), npgsql => npgsql.MigrationsHistoryTable("__ef_migrations", "core")));
builder.Services.AddHelmAuth(builder.Configuration);
builder.Services.AddHelmApi();
builder.Services.AddHelmMessaging(builder.Configuration);
builder.Services.AddHelmJobs(builder.Configuration);

// Each vertical module (CRM, CPQ, Design Engine, etc.) registers itself via its own
// Add<Mod>Module extension, which also calls HelmModuleRegistry.Register(name, schema).
HelmModuleRegistry.Register("core", "core");
builder.Services.AddCrmModule(builder.Configuration);
builder.Services.AddCpqModule(builder.Configuration);
builder.Services.AddProposalOrderModule(builder.Configuration);
builder.Services.AddDesignModule(builder.Configuration);
builder.Services.AddPlanningModule(builder.Configuration);
builder.Services.AddInventoryModule(builder.Configuration);
builder.Services.AddPreProcessingModule(builder.Configuration);
builder.Services.AddManufacturingModule(builder.Configuration);
builder.Services.AddFulfilmentModule(builder.Configuration);
builder.Services.AddAfterSalesModule(builder.Configuration);
// <helm-modules-services>

// Per-audience BFFs (Task 16, ADR-008): each registers its own OpenAPI document
// (AddOpenApi("internal"/"portal"/"kiosk")) so the three frontends get separate specs instead of
// one gateway-shaped API surface.
builder.Services.AddInternalBff();
builder.Services.AddPortalBff();
builder.Services.AddKioskBff();

var app = builder.Build();
app.UseHelmAuth();
app.UseHelmApi();
app.UseHelmJobs();

if (app.Environment.IsDevelopment())
{
    // Specs at /openapi/{documentName}.json — anonymous so docs tooling doesn't need a bearer
    // token; the fallback policy would otherwise 401 these like any other endpoint.
    app.MapOpenApi().AllowAnonymous();
}

app.MapGet("/ping", () => Results.Ok(new { status = "ok" })).AllowAnonymous();
app.MapCrmEndpoints();
app.MapCpqEndpoints();
app.MapProposalOrderEndpoints();
app.MapDesignEndpoints();
app.MapPlanningEndpoints();
app.MapInventoryEndpoints();
app.MapPreProcessingEndpoints();
app.MapManufacturingEndpoints();
app.MapFulfilmentEndpoints();
app.MapAfterSalesEndpoints();
// <helm-modules-endpoints>
app.MapInternalBff();
app.MapPortalBff();
app.MapKioskBff();
app.Run();

public partial class Program; // for WebApplicationFactory
