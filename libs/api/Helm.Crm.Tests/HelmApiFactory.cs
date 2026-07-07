using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using Helm.Core.Auth;
using Helm.Core.Data;
using Helm.Core.MultiCompany;
using Helm.Crm.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.Tokens;
using Testcontainers.PostgreSql;
using Xunit;

namespace Helm.Crm.Tests;

/// <summary>
/// Shared API test harness: one Testcontainers Postgres per fixture instance, InMemory messaging,
/// outbox relay disabled for determinism, and a symmetric-key JWT issuer ("helm-tests") so tests
/// can mint their own tokens instead of depending on Entra. Public and reused by later module test
/// projects (e.g. Task 15) via a project reference to Helm.Crm.Tests.
/// </summary>
public class HelmApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string Issuer = "helm-tests";
    private static readonly SymmetricSecurityKey SigningKey =
        new(Encoding.UTF8.GetBytes("helm-tests-signing-key-at-least-32-bytes-long!!"));

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17").Build();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        // Force host creation now (rather than lazily on first request) so migrations run before
        // any test issues a request. CrmDbContext's constructor requires an ICompanyContext,
        // which is only resolvable within an HTTP request (see AddHelmAuth) — migrations run
        // outside any request, so we build the contexts directly with a throwaway system
        // ICompanyContext instead of asking DI to resolve them (same reasoning as
        // CrmDbContextFactory, the design-time factory used by `dotnet ef`).
        using var core = new CoreDbContext(new DbContextOptionsBuilder<CoreDbContext>()
            .UseNpgsql(_container.GetConnectionString(), npgsql => npgsql.MigrationsHistoryTable("__ef_migrations", "core"))
            .Options);
        await core.Database.MigrateAsync();

        using var crm = CreateSystemCrmDbContext();
        await crm.Database.MigrateAsync();
    }

    /// <summary>Builds a <see cref="CrmDbContext"/> outside DI, with a throwaway group-admin
    /// <see cref="ICompanyContext"/>, for infrastructure operations that run outside any HTTP
    /// request (migrations, test setup/assertions) — never resolved by the app itself. Group-admin
    /// so the company query filter doesn't hide/block rows across the different test companies
    /// used by AsCompany callers.</summary>
    private CrmDbContext CreateSystemCrmDbContext() => new(
        new DbContextOptionsBuilder<CrmDbContext>()
            .UseNpgsql(_container.GetConnectionString(), npgsql => npgsql.MigrationsHistoryTable("__ef_migrations", "crm"))
            .Options,
        new SystemCompanyContext(),
        NullLogger<CrmDbContext>.Instance);

    private sealed class SystemCompanyContext : ICompanyContext
    {
        public string CompanyId => "ntg";
        public bool IsGroupAdmin => true;
    }

    /// <summary>Truncates crm.customer and crm.outbox between tests sharing this fixture instance
    /// (IClassFixture is one instance per test class, not per test) so state from one [Fact]
    /// doesn't leak into the next.</summary>
    public async Task ResetAsync()
    {
        using var db = CreateSystemCrmDbContext();
        await db.Database.ExecuteSqlRawAsync("TRUNCATE TABLE crm.customer, crm.outbox, crm.processed_events");
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await _container.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Helm"] = _container.GetConnectionString(),
                ["Messaging:Provider"] = "InMemory",
                ["Outbox:RelayEnabled"] = "false",
                ["Jobs:Enabled"] = "false",
                ["Authentication:Schemes:Bearer:ValidIssuer"] = Issuer,
                ["Authentication:Schemes:Bearer:ValidAudiences:0"] = Issuer,
            });
        });

        builder.ConfigureServices(services =>
        {
            services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, o =>
            {
                o.TokenValidationParameters.IssuerSigningKey = SigningKey;
                o.TokenValidationParameters.ValidIssuer = Issuer;
                o.TokenValidationParameters.ValidAudience = Issuer;
                o.TokenValidationParameters.ValidateIssuerSigningKey = true;
            });
        });
    }

    /// <summary>Mints a bearer-token-authenticated client for <paramref name="companyId"/> with the given module roles.</summary>
    public HttpClient AsCompany(string companyId, params string[] roles)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", MintToken(companyId, roles));
        return client;
    }

    private static string MintToken(string companyId, string[] roles)
    {
        var claims = new List<Claim>
        {
            new(HelmClaims.CompanyId, companyId),
            new(JwtRegisteredClaimNames.Sub, Guid.NewGuid().ToString()),
        };
        claims.AddRange(roles.Select(r => new Claim(HelmClaims.ModuleRole, r)));

        var token = new JwtSecurityToken(
            issuer: Issuer,
            audience: Issuer,
            claims: claims,
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: new SigningCredentials(SigningKey, SecurityAlgorithms.HmacSha256));

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    /// <summary>Runs a caller-supplied scalar SQL query (typically a `SELECT count(*) ...`) against
    /// the test database and returns the single bigint result. Wraps the caller's query as a
    /// subquery aliased to "Value" — the column name SqlQueryRaw&lt;long&gt; requires by convention
    /// for an unmapped scalar type — so callers can pass a plain, unaliased `SELECT count(*) ...`.
    /// The query is the caller's own literal string (test code, not user input), so raw execution
    /// is safe here.</summary>
    public async Task<long> CountAsync(string sql)
    {
        using var db = CreateSystemCrmDbContext();
#pragma warning disable EF1002 // sql is the test's own literal string, not user input
        return await db.Database.SqlQueryRaw<long>($"SELECT ({sql}) AS \"Value\"").SingleAsync();
#pragma warning restore EF1002
    }
}
