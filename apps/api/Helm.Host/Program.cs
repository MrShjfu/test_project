using Helm.Core;
using Helm.Core.Api;
using Helm.Core.Auth;
using Helm.Core.Data;
using Helm.Core.Jobs;
using Helm.Core.Messaging;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<CoreDbContext>(o => o
    .UseNpgsql(builder.Configuration.GetConnectionString("Helm"), npgsql => npgsql.MigrationsHistoryTable("__ef_migrations", "core")));
builder.Services.AddHelmAuth(builder.Configuration);
builder.Services.AddHelmApi();
builder.Services.AddHelmMessaging(builder.Configuration);
builder.Services.AddHelmJobs(builder.Configuration);

// TODO: as vertical modules (CRM, CPQ, Design Engine, etc.) are scaffolded, each should call
// HelmModuleRegistry.Register(name, schema) from its own Add<Mod>Module extension. Only
// Helm.Core exists today, so its schema is registered directly here.
HelmModuleRegistry.Register("core", "core");

var app = builder.Build();
app.UseHelmAuth();
app.UseHelmApi();
app.UseHelmJobs();
app.MapGet("/ping", () => Results.Ok(new { status = "ok" })).AllowAnonymous();
app.Run();

public partial class Program; // for WebApplicationFactory
