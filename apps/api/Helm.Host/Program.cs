using Helm.Core;
using Helm.Core.Api;
using Helm.Core.Auth;
using Helm.Core.Data;
using Helm.Core.Jobs;
using Helm.Core.Messaging;
using Helm.Crm;
using Microsoft.EntityFrameworkCore;

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
// <helm-modules-services>

var app = builder.Build();
app.UseHelmAuth();
app.UseHelmApi();
app.UseHelmJobs();
app.MapGet("/ping", () => Results.Ok(new { status = "ok" })).AllowAnonymous();
app.MapCrmEndpoints();
// <helm-modules-endpoints>
app.Run();

public partial class Program; // for WebApplicationFactory
