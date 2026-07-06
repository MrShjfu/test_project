using Helm.Core.Api;
using Helm.Core.Auth;
using Helm.Core.Data;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<CoreDbContext>(o => o
    .UseNpgsql(builder.Configuration.GetConnectionString("Helm"), npgsql => npgsql.MigrationsHistoryTable("__ef_migrations", "core")));
builder.Services.AddHelmAuth(builder.Configuration);
builder.Services.AddHelmApi();

var app = builder.Build();
app.UseHelmAuth();
app.UseHelmApi();
app.MapGet("/ping", () => Results.Ok(new { status = "ok" })).AllowAnonymous();
app.Run();

public partial class Program; // for WebApplicationFactory
