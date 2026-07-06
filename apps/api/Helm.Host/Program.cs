using Helm.Core.Data;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<CoreDbContext>(o => o
    .UseNpgsql(builder.Configuration.GetConnectionString("Helm"), npgsql => npgsql.MigrationsHistoryTable("__ef_migrations", "core")));

var app = builder.Build();
app.MapGet("/ping", () => Results.Ok(new { status = "ok" }));
app.Run();

public partial class Program; // for WebApplicationFactory
