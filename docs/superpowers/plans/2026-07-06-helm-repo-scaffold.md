# Helm Repo Scaffold Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Scaffold the Helm monorepo as a walking skeleton — Helm.Core infrastructure proven by a CRM→CPQ demo slice, all 10 domain modules generated as compliant skeletons, 3 frontends + 3 BFFs, docker-compose local dev, GitHub Actions CI.

**Architecture:** .NET 10 modular monolith (`Helm.Host` composition root, module implementations referenced only by Host, cross-module via `*.Contracts` + `IEventBus`/outbox) inside an Nx workspace with React frontends. Build order is generator-first: hand-build Core + CRM, distill the `helm-module` generator from CRM, generate the other 9 modules, then add the demo slice consumer in CPQ.

**Tech Stack:** .NET 10, EF Core + Npgsql, RabbitMQ.Client, Hangfire.PostgreSql, xUnit + FluentAssertions + Testcontainers + NetArchTest.Rules, Nx (latest) + @nx-dotnet/core (spike; fallback run-commands), React 19 + Vite + TypeScript strict, Vitest + RTL, Playwright, openapi-typescript, GitHub Actions, PostgreSQL 17, RabbitMQ 4.

## Global Constraints

- Spec: `docs/superpowers/specs/2026-07-06-helm-repo-scaffold-design.md`. Rules: `CLAUDE.md` + `docs/architecture/engineering-rules.md` — the 9 hard rules are non-negotiable.
- .NET **10** (`net10.0`), Node **22**, npm (no pnpm/yarn), React **19** + Vite, TypeScript **strict**, no `any`.
- Module list (exact names/schemas): Crm/crm, Cpq/cpq, ProposalOrder/proposal_order, Design/design, Planning/planning, Inventory/inventory, PreProcessing/pre_processing, Manufacturing/manufacturing, Fulfilment/fulfilment, AfterSales/after_sales.
- Only `Helm.Host` references module implementation projects. Modules reference other modules' `*.Contracts` only. BFF projects reference `*.Contracts` only.
- Every company-owned table has `company_id`; every query is scoped via the base DbContext global filter; `*:admin` bypass writes an audit row.
- Events: past-tense names, written to the module's own `outbox` table in the same transaction; consumers idempotent via per-schema `processed_events`.
- API: routes `/api/v1/<mod>`, RFC 7807 + `traceId`, `{ items, totalCount }` envelope, authorized by default.
- Tests: TDD — write the failing test first in every task. Testcontainers requires a running Docker daemon.
- Commits: small, frequent, message style `type: summary`, ending with `Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>`.
- All shell commands run from repo root (`/mnt/e/wspace/test_project`).

---

### Task 0: Baseline commit + workspace hygiene

**Files:**
- Create: `.gitignore`, `.editorconfig`
- Commit (already exist, untracked): `CLAUDE.md`, `docs/`

**Interfaces:**
- Produces: clean git baseline all later tasks commit onto.

- [ ] **Step 1: Create `.gitignore`**

```gitignore
# .NET
bin/
obj/
*.user
# Node
node_modules/
dist/
.nx/cache
.nx/workspace-data
coverage/
# Env & tools
.env
*.local
.vscode/
.idea/
# Test artifacts
TestResults/
playwright-report/
test-results/
```

- [ ] **Step 2: Create `.editorconfig`**

```ini
root = true

[*]
charset = utf-8
end_of_line = lf
insert_final_newline = true
indent_style = space
indent_size = 2

[*.cs]
indent_size = 4
```

- [ ] **Step 3: Commit the baseline (docs + hygiene)**

```bash
git add .gitignore .editorconfig CLAUDE.md docs/
git commit -m "chore: baseline — architecture docs, gitignore, editorconfig"
```

---

### Task 1: Nx workspace init

**Files:**
- Create: `package.json`, `nx.json`, `tsconfig.base.json` (via `create-nx-workspace`, then adjusted)

**Interfaces:**
- Produces: `npx nx` runnable at repo root; `apps/`, `libs/` conventions active.

- [ ] **Step 1: Initialize Nx into the existing repo**

```bash
npx nx@latest init --interactive=false
npm install -D @nx/js @nx/react @nx/vite @nx/eslint @nx/playwright
```

Note: `nx init` in an existing repo creates `nx.json` + `package.json` if missing. If it asks questions despite the flag, choose defaults; no cloud.

- [ ] **Step 2: Verify**

Run: `npx nx report`
Expected: prints nx version + installed plugins, exit 0.

- [ ] **Step 3: Set workspace defaults in `nx.json`** — ensure these keys exist (merge, don't clobber generated content):

```json
{
  "defaultBase": "main",
  "namedInputs": {
    "default": ["{projectRoot}/**/*"],
    "production": ["default", "!{projectRoot}/**/*.spec.*", "!{projectRoot}/**/*.test.*"]
  }
}
```

- [ ] **Step 4: Commit**

```bash
git add package.json package-lock.json nx.json tsconfig.base.json .nx 2>/dev/null; git add -A
git commit -m "chore: init Nx workspace"
```

---

### Task 2: .NET 10 + nx-dotnet spike (DECISION TASK)

**Files:**
- Create: `Helm.sln`, `apps/api/Helm.Host/Helm.Host.csproj`, `apps/api/Helm.Host/Program.cs`, `global.json`
- Modify: `docs/superpowers/specs/2026-07-06-helm-repo-scaffold-design.md` (record spike outcome), `docs/adr/ADR-009-monorepo-nx.md` (append note)

**Interfaces:**
- Produces: `Helm.Host` minimal API that builds via `npx nx build api` AND `dotnet build Helm.sln`. Decision recorded: nx-dotnet plugin OR run-commands fallback. All later .NET tasks use whichever integration this task lands.

- [ ] **Step 1: Verify .NET 10 SDK**

Run: `dotnet --list-sdks`
Expected: a `10.x` entry. If missing, install per https://learn.microsoft.com/dotnet/core/install/linux (do not silently downgrade). Then pin:

```bash
dotnet new globaljson --sdk-version $(dotnet --list-sdks | grep '^10\.' | head -1 | cut -d' ' -f1) --roll-forward latestFeature
```

- [ ] **Step 2: Attempt the plugin path**

```bash
npm install -D @nx-dotnet/core
npx nx g @nx-dotnet/core:init
npx nx g @nx-dotnet/core:app Helm.Host --directory apps/api --template webapi --language "C#" --pathScheme dotnet
```

- [ ] **Step 3: Evaluate the spike**

Run: `npx nx build Helm.Host` (or the project name the generator registered — check `npx nx show projects`).
**PASS** (builds net10.0): keep the plugin. **FAIL** (plugin errors, wrong TFM it can't target, graph breakage): remove it (`npm rm @nx-dotnet/core`, delete its config) and use the fallback — hand-write the csproj (Step 4) plus a `project.json` per .NET project:

```json
{
  "name": "api",
  "projectType": "application",
  "sourceRoot": "apps/api/Helm.Host",
  "targets": {
    "build": { "executor": "nx:run-commands", "options": { "command": "dotnet build apps/api/Helm.Host/Helm.Host.csproj" } },
    "test":  { "executor": "nx:run-commands", "options": { "command": "echo no tests" } },
    "serve": { "executor": "nx:run-commands", "options": { "command": "dotnet run --project apps/api/Helm.Host/Helm.Host.csproj" } }
  },
  "implicitDependencies": []
}
```

(Every later .NET project then gets the same shape; `implicitDependencies` lists referenced Helm projects so `nx affected` stays correct. Later tasks say "register with Nx" to mean: plugin auto-detection OR adding this project.json — per this decision.)

- [ ] **Step 4: Ensure Helm.Host content** (whichever path) — `apps/api/Helm.Host/Helm.Host.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
</Project>
```

`apps/api/Helm.Host/Program.cs`:

```csharp
var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();
app.MapGet("/ping", () => Results.Ok(new { status = "ok" }));
app.Run();

public partial class Program; // for WebApplicationFactory
```

- [ ] **Step 5: Solution file**

```bash
dotnet new sln -n Helm
dotnet sln add apps/api/Helm.Host/Helm.Host.csproj
dotnet build Helm.sln
```

Expected: Build succeeded.

- [ ] **Step 6: Record the decision** — append to the spec's "Open technical points" item 1 one sentence: plugin kept or fallback used and why. Append the same note under a new `## 2026-07 addendum` heading at the bottom of `docs/adr/ADR-009-monorepo-nx.md`.

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "feat: Helm.Host bootstrap on .NET 10 + Nx integration decision"
```

---

### Task 3: docker-compose local infra

**Files:**
- Create: `docker-compose.yml`, `.env.example`, `README.md`

**Interfaces:**
- Produces: Postgres on `localhost:5432` (db `helm`, user `helm`/`helm`), RabbitMQ on `5672` + UI `15672` (`helm`/`helm`). Connection strings all later tasks use: `Host=localhost;Database=helm;Username=helm;Password=helm` and `amqp://helm:helm@localhost:5672`.

- [ ] **Step 1: Write `docker-compose.yml`**

```yaml
services:
  postgres:
    image: postgres:17
    environment:
      POSTGRES_DB: helm
      POSTGRES_USER: helm
      POSTGRES_PASSWORD: helm
    ports: ["5432:5432"]
    volumes: [pgdata:/var/lib/postgresql/data]
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U helm -d helm"]
      interval: 5s
      timeout: 3s
      retries: 10

  rabbitmq:
    image: rabbitmq:4-management
    environment:
      RABBITMQ_DEFAULT_USER: helm
      RABBITMQ_DEFAULT_PASS: helm
    ports: ["5672:5672", "15672:15672"]
    healthcheck:
      test: ["CMD", "rabbitmq-diagnostics", "-q", "ping"]
      interval: 5s
      timeout: 3s
      retries: 10

volumes:
  pgdata:
```

(The `full` profile services are added in Task 13 — do not add them now.)

- [ ] **Step 2: Write `.env.example`**

```bash
ConnectionStrings__Helm=Host=localhost;Database=helm;Username=helm;Password=helm
Messaging__Provider=RabbitMQ
Messaging__RabbitMq__Uri=amqp://helm:helm@localhost:5672
```

- [ ] **Step 3: Verify**

Run: `docker compose up -d --wait`
Expected: both services healthy. Then `docker compose down` (keep volume).

- [ ] **Step 4: Write `README.md`** — quickstart section:

```markdown
# Helm — NTG Platform

## Quickstart
1. `docker compose up -d --wait`        # Postgres 17 + RabbitMQ 4
2. `npm ci`
3. `dotnet build Helm.sln`
4. `npx nx serve api`                   # API on http://localhost:5000
5. `npx nx serve web-internal`          # after Task 11
Auth (local): `dotnet user-jwts create --project apps/api/Helm.Host` — see docs/architecture/engineering-rules.md §5.
Architecture rules: CLAUDE.md (hard rules) + docs/.
```

- [ ] **Step 5: Commit**

```bash
git add docker-compose.yml .env.example README.md
git commit -m "feat: local dev infra — postgres + rabbitmq compose"
```

---

### Task 4: Helm.Core project + `core` schema + test fixture

**Files:**
- Create: `libs/api/Helm.Core/Helm.Core.csproj`, `libs/api/Helm.Core/Data/CoreDbContext.cs`, `libs/api/Helm.Core/Data/Company.cs`, `libs/api/Helm.Core/Data/User.cs`, `libs/api/Helm.Core.Tests/Helm.Core.Tests.csproj`, `libs/api/Helm.Core.Tests/PostgresFixture.cs`, `libs/api/Helm.Core.Tests/CoreDbContextTests.cs`
- Modify: `Helm.sln`, Nx registration per Task 2 decision

**Interfaces:**
- Produces: `CoreDbContext` (schema `core`, tables `companies`, `users`); `PostgresFixture` (xUnit collection fixture exposing `string ConnectionString`, one Testcontainers Postgres per test assembly); NuGet baseline for all module projects: `Microsoft.EntityFrameworkCore` + `Npgsql.EntityFrameworkCore.PostgreSQL`.
- `User` shape: `Guid Id, string Email, string CompanyId, string[] ModuleRoles`.
- `Company` shape: `string Id` (e.g. `"doyle"`), `string Name, string? ParentCompanyId`.

- [ ] **Step 1: Create projects**

```bash
dotnet new classlib -n Helm.Core -o libs/api/Helm.Core -f net10.0
dotnet new xunit -n Helm.Core.Tests -o libs/api/Helm.Core.Tests -f net10.0
dotnet sln add libs/api/Helm.Core libs/api/Helm.Core.Tests
dotnet add libs/api/Helm.Core package Npgsql.EntityFrameworkCore.PostgreSQL
dotnet add libs/api/Helm.Core package Microsoft.EntityFrameworkCore.Relational
dotnet add libs/api/Helm.Core.Tests reference libs/api/Helm.Core
dotnet add libs/api/Helm.Core.Tests package FluentAssertions
dotnet add libs/api/Helm.Core.Tests package Testcontainers.PostgreSql
dotnet add libs/api/Helm.Core.Tests package Microsoft.EntityFrameworkCore.Design
```

Register both with Nx (per Task 2 decision). `Helm.Core` also needs `<FrameworkReference Include="Microsoft.AspNetCore.App" />` in its csproj (middleware/auth types live here).

- [ ] **Step 2: Write the failing test** — `libs/api/Helm.Core.Tests/CoreDbContextTests.cs`:

```csharp
using FluentAssertions;
using Helm.Core.Data;
using Microsoft.EntityFrameworkCore;
using Xunit;

[Collection("postgres")]
public class CoreDbContextTests(PostgresFixture pg)
{
    [Fact]
    public async Task Migrates_and_stores_company_and_user()
    {
        await using var db = pg.CreateDbContext<CoreDbContext>(o => new CoreDbContext(o));
        await db.Database.MigrateAsync();
        db.Add(new Company { Id = "doyle", Name = "Doyle" });
        db.Add(new User { Id = Guid.NewGuid(), Email = "a@doyle.com", CompanyId = "doyle", ModuleRoles = ["crm:editor"] });
        await db.SaveChangesAsync();
        (await db.Set<User>().SingleAsync()).CompanyId.Should().Be("doyle");
    }
}
```

`libs/api/Helm.Core.Tests/PostgresFixture.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Xunit;

public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17").Build();
    public string ConnectionString => _container.GetConnectionString();
    public Task InitializeAsync() => _container.StartAsync();
    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    public TContext CreateDbContext<TContext>(Func<DbContextOptions<TContext>, TContext> factory)
        where TContext : DbContext =>
        factory(new DbContextOptionsBuilder<TContext>().UseNpgsql(ConnectionString).Options);
}

[CollectionDefinition("postgres")]
public class PostgresCollection : ICollectionFixture<PostgresFixture>;
```

- [ ] **Step 3: Run to verify it fails**

Run: `dotnet test libs/api/Helm.Core.Tests`
Expected: compile error — `CoreDbContext`/`Company`/`User` do not exist.

- [ ] **Step 4: Implement** — `libs/api/Helm.Core/Data/Company.cs`:

```csharp
namespace Helm.Core.Data;

public class Company
{
    public required string Id { get; init; }          // "north" | "quantum" | "doyle" | "ntg" | licensee ids
    public required string Name { get; set; }
    public string? ParentCompanyId { get; set; }       // licensee hierarchy (ADR-003)
}
```

`libs/api/Helm.Core/Data/User.cs`:

```csharp
namespace Helm.Core.Data;

public class User
{
    public Guid Id { get; init; }
    public required string Email { get; set; }
    public required string CompanyId { get; set; }
    public string[] ModuleRoles { get; set; } = [];    // e.g. "cpq:editor", "*:admin"
}
```

`libs/api/Helm.Core/Data/CoreDbContext.cs`:

```csharp
using Microsoft.EntityFrameworkCore;

namespace Helm.Core.Data;

public class CoreDbContext(DbContextOptions<CoreDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasDefaultSchema("core");
        b.Entity<Company>().ToTable("companies");
        b.Entity<User>().ToTable("users").HasIndex(u => u.Email).IsUnique();
    }
}
```

Generate the migration:

```bash
dotnet ef migrations add InitCore --project libs/api/Helm.Core --startup-project apps/api/Helm.Host --context Helm.Core.Data.CoreDbContext
```

(Requires `dotnet tool install -g dotnet-ef` and Helm.Host referencing Helm.Core + registering `CoreDbContext` with `UseNpgsql(config.GetConnectionString("Helm"), o => o.MigrationsHistoryTable("__ef_migrations", "core"))` — add that registration to `Program.cs` now.)

- [ ] **Step 5: Run to verify it passes**

Run: `dotnet test libs/api/Helm.Core.Tests`
Expected: PASS (pulls postgres:17 image on first run).

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat: Helm.Core with core schema (companies, users) + Testcontainers fixture"
```

---

### Task 5: Multi-company scoping (`company_id` global filter + audit)

**Files:**
- Create: `libs/api/Helm.Core/MultiCompany/ICompanyContext.cs`, `libs/api/Helm.Core/MultiCompany/CompanyOwnedEntity.cs`, `libs/api/Helm.Core/MultiCompany/ModuleDbContext.cs`, `libs/api/Helm.Core/MultiCompany/CrossCompanyAuditLogger.cs`
- Test: `libs/api/Helm.Core.Tests/CompanyScopingTests.cs`

**Interfaces:**
- Produces (all `namespace Helm.Core.MultiCompany`):
  - `interface ICompanyContext { string CompanyId { get; } bool IsGroupAdmin { get; } }`
  - `abstract class CompanyOwnedEntity { public required string CompanyId { get; set; } }`
  - `abstract class ModuleDbContext<TSelf> : DbContext` — ctor `(DbContextOptions<TSelf>, ICompanyContext, ILogger<TSelf>)`; applies `HasQueryFilter(e => CompanyId matches)` to every `CompanyOwnedEntity`; auto-stamps `CompanyId` on `SaveChanges`; when `IsGroupAdmin` the filter is bypassed and **one audit log line per query context creation** is written via `CrossCompanyAuditLogger`.
  - **Every module DbContext in later tasks derives from `ModuleDbContext<TSelf>`.**

- [ ] **Step 1: Write the failing test** — `libs/api/Helm.Core.Tests/CompanyScopingTests.cs`:

```csharp
using FluentAssertions;
using Helm.Core.MultiCompany;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

file class Widget : CompanyOwnedEntity { public Guid Id { get; init; } = Guid.NewGuid(); }

file class WidgetDb(DbContextOptions<WidgetDb> o, ICompanyContext c)
    : ModuleDbContext<WidgetDb>(o, c, NullLogger<WidgetDb>.Instance)
{
    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasDefaultSchema("widget_test");
        b.Entity<Widget>();
        base.OnModelCreating(b); // MUST be last — applies company filters
    }
}

file record Ctx(string CompanyId, bool IsGroupAdmin) : ICompanyContext;

[Collection("postgres")]
public class CompanyScopingTests(PostgresFixture pg)
{
    private WidgetDb Db(ICompanyContext ctx) =>
        new(new DbContextOptionsBuilder<WidgetDb>().UseNpgsql(pg.ConnectionString).Options, ctx);

    [Fact]
    public async Task Rows_are_scoped_by_company_and_admin_sees_all()
    {
        await using (var setup = Db(new Ctx("doyle", false)))
        {
            await setup.Database.EnsureCreatedAsync();
            setup.Add(new Widget()); // CompanyId auto-stamped "doyle"
            await setup.SaveChangesAsync();
        }
        await using (var north = Db(new Ctx("north", false)))
        {
            north.Add(new Widget());
            await north.SaveChangesAsync();
            (await north.Set<Widget>().CountAsync()).Should().Be(1); // only north's
        }
        await using var admin = Db(new Ctx("ntg", true));
        (await admin.Set<Widget>().CountAsync()).Should().Be(2);      // bypass
    }
}
```

- [ ] **Step 2: Run to verify it fails** — `dotnet test libs/api/Helm.Core.Tests --filter CompanyScoping` → compile error.

- [ ] **Step 3: Implement** — `ICompanyContext.cs`:

```csharp
namespace Helm.Core.MultiCompany;

public interface ICompanyContext
{
    string CompanyId { get; }
    bool IsGroupAdmin { get; }   // company_id == "ntg" && module_roles contains "*:admin"
}
```

`CompanyOwnedEntity.cs`:

```csharp
namespace Helm.Core.MultiCompany;

public abstract class CompanyOwnedEntity
{
    public string CompanyId { get; set; } = null!;
}
```

`CrossCompanyAuditLogger.cs`:

```csharp
using Microsoft.Extensions.Logging;

namespace Helm.Core.MultiCompany;

public static class CrossCompanyAuditLogger
{
    // Structured audit event — App Insights picks this up; a core.audit_log table is a later hardening step.
    public static void LogBypass(ILogger logger, string companyId) =>
        logger.LogWarning("AUDIT cross-company access granted to group admin of {CompanyId}", companyId);
}
```

`ModuleDbContext.cs`:

```csharp
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Helm.Core.MultiCompany;

public abstract class ModuleDbContext<TSelf>(
    DbContextOptions<TSelf> options, ICompanyContext company, ILogger<TSelf> logger)
    : DbContext(options) where TSelf : DbContext
{
    protected ICompanyContext Company { get; } = company;

    protected override void OnModelCreating(ModelBuilder b)
    {
        foreach (var et in b.Model.GetEntityTypes()
                     .Where(t => typeof(CompanyOwnedEntity).IsAssignableFrom(t.ClrType)))
        {
            var p = Expression.Parameter(et.ClrType);
            var body = Expression.OrElse(
                Expression.Property(Expression.Constant(this), nameof(BypassFilter)),
                Expression.Equal(
                    Expression.Property(p, nameof(CompanyOwnedEntity.CompanyId)),
                    Expression.Property(Expression.Constant(this), nameof(CurrentCompanyId))));
            et.SetQueryFilter(Expression.Lambda(body, p));
            et.GetProperty(nameof(CompanyOwnedEntity.CompanyId)).SetMaxLength(64);
        }
        if (Company.IsGroupAdmin) CrossCompanyAuditLogger.LogBypass(logger, Company.CompanyId);
    }

    public string CurrentCompanyId => Company.CompanyId;
    public bool BypassFilter => Company.IsGroupAdmin;

    public override Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        foreach (var e in ChangeTracker.Entries<CompanyOwnedEntity>().Where(e => e.State == EntityState.Added))
            e.Entity.CompanyId ??= Company.CompanyId;
        return base.SaveChangesAsync(ct);
    }
}
```

- [ ] **Step 4: Run to verify it passes** — `dotnet test libs/api/Helm.Core.Tests --filter CompanyScoping` → PASS.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat: company_id scoping — ModuleDbContext global filter, admin bypass audited"
```

---

### Task 6: AuthN/AuthZ — JWT bearer, authorized-by-default, claims from core.users

**Files:**
- Create: `libs/api/Helm.Core/Auth/HelmAuthExtensions.cs`, `libs/api/Helm.Core/Auth/UserClaimsTransformer.cs`, `libs/api/Helm.Core/Auth/HttpCompanyContext.cs`
- Modify: `apps/api/Helm.Host/Program.cs`, `apps/api/Helm.Host/appsettings.Development.json`
- Test: `libs/api/Helm.Core.Tests/AuthTests.cs` (unit, transformer only) + covered end-to-end in Task 15

**Interfaces:**
- Produces: `services.AddHelmAuth(config)` (JWT bearer + fallback authorization policy `RequireAuthenticatedUser` + `IClaimsTransformation` + scoped `ICompanyContext` from HttpContext) and `app.UseHelmAuth()`.
- Claim names (constants in `HelmClaims`): `company_id`, `module_role` (multi-value). Local dev tokens: `dotnet user-jwts create --project apps/api/Helm.Host --claim company_id=doyle --claim module_role=crm:editor`.
- `UserClaimsTransformer` looks up `core.users` by email claim, adds `company_id` + `module_role` claims, caches per email in `IMemoryCache` 5 min. If the token already carries `company_id` (dev tokens), the lookup is skipped — documented dev backdoor, removed when real Entra lands.

- [ ] **Step 1: Write the failing unit test** — `libs/api/Helm.Core.Tests/AuthTests.cs`:

```csharp
using System.Security.Claims;
using FluentAssertions;
using Helm.Core.Auth;
using Xunit;

public class AuthTests
{
    [Fact]
    public void HttpCompanyContext_reads_claims()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(HelmClaims.CompanyId, "ntg"), new Claim(HelmClaims.ModuleRole, "*:admin")], "test"));
        var ctx = HttpCompanyContext.FromPrincipal(principal);
        ctx.CompanyId.Should().Be("ntg");
        ctx.IsGroupAdmin.Should().BeTrue();
    }

    [Fact]
    public void Non_ntg_admin_claim_is_not_group_admin()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(HelmClaims.CompanyId, "doyle"), new Claim(HelmClaims.ModuleRole, "*:admin")], "test"));
        HttpCompanyContext.FromPrincipal(principal).IsGroupAdmin.Should().BeFalse();
    }
}
```

- [ ] **Step 2: Run to verify it fails** — compile error (`HelmClaims` missing).

- [ ] **Step 3: Implement** — `HttpCompanyContext.cs`:

```csharp
using System.Security.Claims;
using Helm.Core.MultiCompany;

namespace Helm.Core.Auth;

public static class HelmClaims
{
    public const string CompanyId = "company_id";
    public const string ModuleRole = "module_role";
}

public sealed record HttpCompanyContext(string CompanyId, bool IsGroupAdmin) : ICompanyContext
{
    public static HttpCompanyContext FromPrincipal(ClaimsPrincipal user)
    {
        var company = user.FindFirstValue(HelmClaims.CompanyId)
            ?? throw new UnauthorizedAccessException("missing company_id claim");
        var isAdmin = company == "ntg" && user.FindAll(HelmClaims.ModuleRole).Any(c => c.Value == "*:admin");
        return new(company, isAdmin);
    }
}
```

`UserClaimsTransformer.cs`:

```csharp
using System.Security.Claims;
using Helm.Core.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Helm.Core.Auth;

public class UserClaimsTransformer(CoreDbContext db, IMemoryCache cache) : IClaimsTransformation
{
    public async Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        if (principal.HasClaim(c => c.Type == HelmClaims.CompanyId)) return principal; // dev tokens
        var email = principal.FindFirstValue(ClaimTypes.Email) ?? principal.FindFirstValue("preferred_username");
        if (email is null) return principal;
        var user = await cache.GetOrCreateAsync($"user:{email}", async e =>
        {
            e.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5);
            return await db.Set<User>().AsNoTracking().SingleOrDefaultAsync(u => u.Email == email);
        });
        if (user is null) return principal;
        var id = new ClaimsIdentity();
        id.AddClaim(new Claim(HelmClaims.CompanyId, user.CompanyId));
        foreach (var r in user.ModuleRoles) id.AddClaim(new Claim(HelmClaims.ModuleRole, r));
        principal.AddIdentity(id);
        return principal;
    }
}
```

`HelmAuthExtensions.cs`:

```csharp
using Helm.Core.MultiCompany;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Helm.Core.Auth;

public static class HelmAuthExtensions
{
    public static IServiceCollection AddHelmAuth(this IServiceCollection services, IConfiguration config)
    {
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(); // dev: dotnet user-jwts config; prod: Entra section (placeholder, infra phase)
        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser().Build());
        services.AddMemoryCache();
        services.AddScoped<IClaimsTransformation, UserClaimsTransformer>();
        services.AddHttpContextAccessor();
        services.AddScoped<ICompanyContext>(sp =>
            HttpCompanyContext.FromPrincipal(
                sp.GetRequiredService<IHttpContextAccessor>().HttpContext?.User
                ?? throw new InvalidOperationException("no http context")));
        return services;
    }

    public static WebApplication UseHelmAuth(this WebApplication app)
    {
        app.UseAuthentication();
        app.UseAuthorization();
        return app;
    }
}
```

In `Program.cs` add `builder.Services.AddHelmAuth(builder.Configuration);` and `app.UseHelmAuth();`; mark `/ping` and `/health` with `.AllowAnonymous()`.

- [ ] **Step 4: Run tests + boot check**

Run: `dotnet test libs/api/Helm.Core.Tests --filter Auth` → PASS.
Run: `dotnet user-jwts create --project apps/api/Helm.Host --claim company_id=doyle --claim module_role=crm:editor` then `dotnet run --project apps/api/Helm.Host` and curl `/ping` (200 anonymous). A protected probe endpoint returns 401 without token — verified properly in Task 11's endpoint tests.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat: JWT auth, authorized-by-default, claims hydration from core.users"
```

---

### Task 7: API conventions — Problem Details + traceId, pagination envelope, health aggregation

**Files:**
- Create: `libs/api/Helm.Core/Api/PagedResult.cs`, `libs/api/Helm.Core/Api/HelmApiExtensions.cs`
- Modify: `apps/api/Helm.Host/Program.cs`
- Test: `libs/api/Helm.Core.Tests/PagedResultTests.cs`

**Interfaces:**
- Produces (`namespace Helm.Core.Api`):
  - `record PagedResult<T>(IReadOnlyList<T> Items, int TotalCount)` — the only list envelope any endpoint returns.
  - `record PageRequest(int Page = 1, int PageSize = 20)` with `Skip => (Page-1)*PageSize`; PageSize clamped 1–200.
  - `services.AddHelmApi()` → `AddProblemDetails` customized to add `traceId`; `app.UseHelmApi()` → `UseExceptionHandler` + `UseStatusCodePages` emitting RFC 7807.
  - Health: modules register `IHealthChecksBuilder` entries via their `Add<Mod>Module`; Host maps `/health` (anonymous).

- [ ] **Step 1: Write the failing test**

```csharp
using FluentAssertions;
using Helm.Core.Api;
using Xunit;

public class PagedResultTests
{
    [Fact]
    public void PageRequest_clamps_and_computes_skip()
    {
        new PageRequest(3, 20).Skip.Should().Be(40);
        new PageRequest(0, 0).Normalized().Should().Be(new PageRequest(1, 1));
        new PageRequest(1, 9999).Normalized().PageSize.Should().Be(200);
    }
}
```

- [ ] **Step 2: Run to verify it fails** — compile error.

- [ ] **Step 3: Implement** — `PagedResult.cs`:

```csharp
namespace Helm.Core.Api;

public record PagedResult<T>(IReadOnlyList<T> Items, int TotalCount);

public record PageRequest(int Page = 1, int PageSize = 20)
{
    public int Skip => (Page - 1) * PageSize;
    public PageRequest Normalized() => new(Math.Max(1, Page), Math.Clamp(PageSize, 1, 200));
}
```

`HelmApiExtensions.cs`:

```csharp
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection;

namespace Helm.Core.Api;

public static class HelmApiExtensions
{
    public static IServiceCollection AddHelmApi(this IServiceCollection services)
    {
        services.AddProblemDetails(o => o.CustomizeProblemDetails = ctx =>
            ctx.ProblemDetails.Extensions["traceId"] = ctx.HttpContext.TraceIdentifier);
        services.AddHealthChecks();
        return services;
    }

    public static WebApplication UseHelmApi(this WebApplication app)
    {
        app.UseExceptionHandler();
        app.UseStatusCodePages();
        app.MapHealthChecks("/health").AllowAnonymous();
        return app;
    }
}
```

Wire both into `Program.cs` (`AddHelmApi()` before build, `UseHelmApi()` after `UseHelmAuth()`).

- [ ] **Step 4: Run to verify it passes** — `dotnet test libs/api/Helm.Core.Tests --filter PagedResult` → PASS. Boot Host, `curl -i localhost:5000/health` → 200 `Healthy`; `curl -i localhost:5000/api/v1/nope` → JSON problem details with `traceId`.

- [ ] **Step 5: Commit** — `git add -A && git commit -m "feat: API conventions — problem details with traceId, paging envelope, /health"`

---

### Task 8: IEventBus — abstraction + InMemory + RabbitMQ transports

**Files:**
- Create: `libs/api/Helm.Core/Messaging/IEventBus.cs`, `libs/api/Helm.Core/Messaging/EventEnvelope.cs`, `libs/api/Helm.Core/Messaging/InMemoryEventBus.cs`, `libs/api/Helm.Core/Messaging/RabbitMqEventBus.cs`, `libs/api/Helm.Core/Messaging/MessagingExtensions.cs`
- Test: `libs/api/Helm.Core.Tests/InMemoryEventBusTests.cs`, `libs/api/Helm.Core.Tests/RabbitMqEventBusTests.cs` (+ `RabbitMqFixture.cs`)

**Interfaces:**
- Produces (`namespace Helm.Core.Messaging`):

```csharp
public interface IDomainEvent { Guid EventId { get; } }   // implemented by every event record
public record EventEnvelope(Guid EventId, string EventType, string PayloadJson);
public interface IEventBus
{
    Task PublishAsync(EventEnvelope envelope, CancellationToken ct = default);
    // handler receives the envelope; returns when handled. Subscribe is called at startup by consumers.
    IDisposable Subscribe(string eventType, Func<EventEnvelope, CancellationToken, Task> handler);
}
```

- Config: `Messaging:Provider` = `InMemory | RabbitMQ | AzureServiceBus`; `Messaging:RabbitMq:Uri`. `AddHelmMessaging(config)` registers the right singleton; `AzureServiceBus` → `throw new NotImplementedException("infra phase — see spec out-of-scope table")`.
- RabbitMQ topology: one topic exchange `helm.events`; routing key = event type; each subscription = durable queue `helm.<eventType>.<consumerId>`; publisher confirms on; JSON UTF-8 body = `PayloadJson`, headers carry `event_id`/`event_type`.

- [ ] **Step 1: Write failing InMemory test**

```csharp
using FluentAssertions;
using Helm.Core.Messaging;
using Xunit;

public class InMemoryEventBusTests
{
    [Fact]
    public async Task Delivers_to_matching_subscriber_only()
    {
        var bus = new InMemoryEventBus();
        var got = new List<string>();
        using var _ = bus.Subscribe("CustomerCreated", (e, _) => { got.Add(e.EventType); return Task.CompletedTask; });
        await bus.PublishAsync(new EventEnvelope(Guid.NewGuid(), "CustomerCreated", "{}"));
        await bus.PublishAsync(new EventEnvelope(Guid.NewGuid(), "OrderPlaced", "{}"));
        got.Should().Equal("CustomerCreated");
    }
}
```

- [ ] **Step 2: Run to verify it fails**, then implement `IEventBus.cs`/`EventEnvelope.cs` exactly as in Interfaces, and `InMemoryEventBus.cs`:

```csharp
using System.Collections.Concurrent;

namespace Helm.Core.Messaging;

public sealed class InMemoryEventBus : IEventBus
{
    private readonly ConcurrentDictionary<string, List<Func<EventEnvelope, CancellationToken, Task>>> _subs = new();

    public async Task PublishAsync(EventEnvelope e, CancellationToken ct = default)
    {
        if (_subs.TryGetValue(e.EventType, out var handlers))
            foreach (var h in handlers.ToArray()) await h(e, ct); // synchronous dispatch — tests only (ADR-004)
    }

    public IDisposable Subscribe(string eventType, Func<EventEnvelope, CancellationToken, Task> handler)
    {
        var list = _subs.GetOrAdd(eventType, _ => []);
        lock (list) list.Add(handler);
        return new Unsub(() => { lock (list) list.Remove(handler); });
    }

    private sealed class Unsub(Action a) : IDisposable { public void Dispose() => a(); }
}
```

- [ ] **Step 3: RabbitMQ failing test** — `RabbitMqFixture` mirrors `PostgresFixture` using `Testcontainers.RabbitMq` (`rabbitmq:4-management`), exposes `string AmqpUri`. Test: publish one envelope, subscriber (separate bus instance, same URI) receives it within 5s; assert envelope round-trips EventId/EventType/PayloadJson.

```csharp
[Collection("rabbitmq")]
public class RabbitMqEventBusTests(RabbitMqFixture mq)
{
    [Fact]
    public async Task Publish_reaches_subscriber_across_instances()
    {
        await using var pub = await RabbitMqEventBus.ConnectAsync(mq.AmqpUri, "test-pub");
        await using var sub = await RabbitMqEventBus.ConnectAsync(mq.AmqpUri, "test-sub");
        var tcs = new TaskCompletionSource<EventEnvelope>();
        using var _ = sub.Subscribe("CustomerCreated", (e, _) => { tcs.TrySetResult(e); return Task.CompletedTask; });
        var sent = new EventEnvelope(Guid.NewGuid(), "CustomerCreated", """{"name":"x"}""");
        await pub.PublishAsync(sent);
        (await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5))).Should().BeEquivalentTo(sent);
    }
}
```

- [ ] **Step 4: Implement `RabbitMqEventBus`** — `RabbitMQ.Client` (v7, async API): static `ConnectAsync(uri, consumerId)` creates connection + channel, declares exchange `helm.events` (topic, durable). `PublishAsync` → `BasicPublishAsync` with routing key = EventType, persistent, publisher confirms. `Subscribe` → declare durable queue `helm.{eventType}.{consumerId}`, bind, `AsyncEventingBasicConsumer`; handler success → ack; exception → `BasicNackAsync(requeue:false)` (DLQ policy is a later hardening step — noted in code comment). Implements `IAsyncDisposable`. ~80 lines; follow the test until green.

- [ ] **Step 5: `MessagingExtensions.AddHelmMessaging(config)`** — switch on `Messaging:Provider` (default `InMemory`); `RabbitMQ` registers a lazy singleton connecting with consumerId = `Environment.MachineName`; `AzureServiceBus` throws `NotImplementedException`. Wire into `Program.cs`.

- [ ] **Step 6: Run** `dotnet test libs/api/Helm.Core.Tests --filter EventBus` → PASS. **Commit:** `feat: IEventBus with InMemory + RabbitMQ transports`

---

### Task 9: Transactional outbox + idempotent consumer base

**Files:**
- Create: `libs/api/Helm.Core/Outbox/OutboxMessage.cs`, `libs/api/Helm.Core/Outbox/ProcessedEvent.cs`, `libs/api/Helm.Core/Outbox/OutboxWriter.cs`, `libs/api/Helm.Core/Outbox/OutboxRelay.cs`, `libs/api/Helm.Core/Outbox/IdempotentConsumer.cs`, `libs/api/Helm.Core/Outbox/OutboxModelBuilder.cs`
- Test: `libs/api/Helm.Core.Tests/OutboxTests.cs`

**Interfaces:**
- Produces (`namespace Helm.Core.Outbox`):
  - `class OutboxMessage { Guid Id; string EventType; string Payload; DateTimeOffset CreatedAt; DateTimeOffset? ProcessedAt; }`
  - `class ProcessedEvent { Guid EventId; DateTimeOffset ProcessedAt; }`
  - `OutboxModelBuilder.AddOutbox(ModelBuilder b)` — maps both tables (`outbox`, `processed_events`) into the calling DbContext's default schema. **Every module DbContext calls this in OnModelCreating.**
  - `OutboxWriter.Enqueue(DbContext db, IDomainEvent evt)` — serializes (System.Text.Json), adds an `OutboxMessage` to the same context (same transaction on SaveChanges). Event type name = `evt.GetType().Name`.
  - `class OutboxRelay<TContext>(IServiceScopeFactory, IEventBus, ILogger) : BackgroundService` — every 1s: open scope, `SELECT … FOR UPDATE SKIP LOCKED LIMIT 20` raw SQL over the context's schema, publish each, set `processed_at`, commit. Registered once per publishing module by `Add<Mod>Module`.
  - `abstract class IdempotentConsumer<TContext, TEvent>` — `Subscribe`s at startup (`IHostedService`), on receipt: begin tx, `INSERT INTO <schema>.processed_events` (PK conflict → skip + ack), call `abstract Task HandleAsync(TEvent evt, TContext db)`, commit.

- [ ] **Step 1: Write the failing tests** (Testcontainers Postgres + InMemory bus; define a `file` test DbContext `OutboxTestDb` with schema `outbox_test` that calls `OutboxModelBuilder.AddOutbox`):

```csharp
[Collection("postgres")]
public class OutboxTests(PostgresFixture pg)
{
    [Fact]
    public async Task Rollback_leaves_no_outbox_row() { /* Enqueue + throw before SaveChanges → table empty */ }

    [Fact]
    public async Task Relay_publishes_and_marks_processed()
    {
        // seed 1 unprocessed row; run one relay iteration (expose internal RunOnceAsync for tests);
        // assert InMemory bus delivered 1 envelope AND processed_at is set
    }

    [Fact]
    public async Task Relay_competing_instances_do_not_double_publish()
    {
        // seed 5 rows; run two RunOnceAsync concurrently on separate scopes;
        // assert bus received exactly 5 envelopes total
    }

    [Fact]
    public async Task IdempotentConsumer_skips_duplicate_event_id() { /* deliver same envelope twice → HandleAsync ran once */ }
}
```

Write these four tests fully (arrange/act/assert) before any implementation; they define the contract. Run → compile errors.

- [ ] **Step 2: Implement the four classes.** Key SQL in `OutboxRelay.RunOnceAsync`:

```sql
SELECT id, event_type, payload FROM {schema}.outbox
WHERE processed_at IS NULL ORDER BY created_at
FOR UPDATE SKIP LOCKED LIMIT 20
```

inside a serializable-default transaction via `db.Database.BeginTransactionAsync()`; after each successful `PublishAsync`, `UPDATE {schema}.outbox SET processed_at = now() WHERE id = @id`; commit once per batch. Schema name comes from `db.Model.GetDefaultSchema()`. `ExecuteSqlRaw` is acceptable here — it is the module's own schema (rule 1 is about *other* modules' schemas).

- [ ] **Step 3: Run to verify all 4 pass** — `dotnet test libs/api/Helm.Core.Tests --filter Outbox` → PASS.

- [ ] **Step 4: Commit** — `feat: transactional outbox relay (SKIP LOCKED) + idempotent consumer base`

---

### Task 10: Hangfire — PG storage, guarded dashboard, purge job

**Files:**
- Create: `libs/api/Helm.Core/Jobs/HelmJobsExtensions.cs`, `libs/api/Helm.Core/Jobs/OutboxPurgeJob.cs`, `libs/api/Helm.Core/Jobs/HangfireAdminFilter.cs`
- Modify: `apps/api/Helm.Host/Program.cs`
- Test: `libs/api/Helm.Core.Tests/OutboxPurgeJobTests.cs`

**Interfaces:**
- Produces: `services.AddHelmJobs(config)` (Hangfire + `Hangfire.PostgreSql` storage, schema `hangfire`) and `app.UseHelmJobs()` (dashboard at `/hangfire` behind `HangfireAdminFilter` requiring authenticated `module_role=*:admin`; registers recurring job `outbox-purge` daily).
- `OutboxPurgeJob.Run(IEnumerable<string> schemas)`: for each module schema, `DELETE FROM {s}.outbox WHERE processed_at < now() - interval '30 days'` + same for `processed_events`. Schema list comes from a `HelmModuleRegistry` static list each `Add<Mod>Module` appends to — also produced here: `Helm.Core/HelmModuleRegistry.cs` with `static void Register(string name, string schema)` + `static IReadOnlyList<(string Name, string Schema)> Modules`.

- [ ] **Step 1: Failing test** — seed an outbox row 31 days old + one fresh into `outbox_test` schema; run `OutboxPurgeJob.Run` with `["outbox_test"]`; assert only the fresh row remains.
- [ ] **Step 2: Implement** (`NpgsqlConnection` direct, parameterized interval; job class is `[AutomaticRetry(Attempts = 3)]`). `HangfireAdminFilter` per ADR-005 example (`user.HasClaim("module_role", "*:admin")`).
- [ ] **Step 3: Test passes**; boot Host with compose infra up: `/hangfire` returns 401/403 without admin token.
- [ ] **Step 4: Commit** — `feat: Hangfire jobs infra + 30-day outbox purge`

---

### Task 11: Helm.Crm — the hand-built exemplar module

**Files:**
- Create: `libs/api/Helm.Crm.Contracts/{Helm.Crm.Contracts.csproj, Dtos/CustomerDto.cs, Events/CustomerCreated.cs, ICrmService.cs, EVENT-CATALOG.md}`
- Create: `libs/api/Helm.Crm/{Helm.Crm.csproj, CrmModule.cs, Api/CrmEndpoints.cs, Domain/Customer.cs, Application/CrmService.cs, Infrastructure/CrmDbContext.cs, Application/README.md, Domain/README.md}` + migration
- Create: `libs/api/Helm.Crm.Tests/{Helm.Crm.Tests.csproj, CrmApiTests.cs, HelmApiFactory.cs}`
- Modify: `Helm.sln`, `apps/api/Helm.Host/Program.cs` (+ project reference Helm.Crm)

**Interfaces:**
- Consumes: everything from Tasks 4–10 (`ModuleDbContext`, `OutboxWriter`, `OutboxModelBuilder`, `PagedResult`, `HelmModuleRegistry`).
- Produces:
  - `Helm.Crm.Contracts`: `record CustomerDto(Guid Id, string Name, string Email)`; `record CustomerCreated(Guid EventId, Guid CustomerId, string CompanyId, string Name) : IDomainEvent`; `interface ICrmService { Task<CustomerDto?> GetCustomer(Guid id, CancellationToken ct); Task<IReadOnlyList<CustomerDto>> GetCustomers(IReadOnlyCollection<Guid> ids, CancellationToken ct); }` (single + **batch** — engineering-rules §2).
  - `Helm.Crm`: `services.AddCrmModule(config)` (DbContext, `ICrmService`→`CrmService`, health check `crm`, `OutboxRelay<CrmDbContext>`, `HelmModuleRegistry.Register("Crm","crm")`), `app.MapCrmEndpoints()` (`POST /api/v1/crm/customers`, `GET /api/v1/crm/customers` paged, `GET /api/v1/crm/customers/{id}`).
  - `Helm.Crm.Tests/HelmApiFactory.cs`: `WebApplicationFactory<Program>` wired to Testcontainers Postgres + `Messaging:Provider=InMemory`, helpers `HttpClient AsCompany(string companyId, params string[] roles)` (mints test JWTs) and `Task<long> CountAsync(string sql)` (scalar query against the test database) — **reused by Task 15 and future module tests** (public class, referenced via project reference).
- `Customer : CompanyOwnedEntity` with `Guid Id, string Name, string Email`; table `crm.customer`.

- [ ] **Step 1: Failing API test** — `CrmApiTests.cs`:

```csharp
public class CrmApiTests(HelmApiFactory f) : IClassFixture<HelmApiFactory>
{
    [Fact]
    public async Task Post_then_list_returns_envelope()
    {
        var client = f.AsCompany("doyle", "crm:editor");
        var res = await client.PostAsJsonAsync("/api/v1/crm/customers", new { name = "Aldo", email = "a@x.com" });
        res.StatusCode.Should().Be(HttpStatusCode.Created);
        var page = await client.GetFromJsonAsync<PagedResult<CustomerDto>>("/api/v1/crm/customers?page=1&pageSize=10");
        page!.TotalCount.Should().Be(1);
        page.Items.Single().Name.Should().Be("Aldo");
    }

    [Fact]
    public async Task Anonymous_gets_401()
    {
        (await f.CreateClient().GetAsync("/api/v1/crm/customers")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Post_writes_outbox_row_in_same_transaction()
    {
        var client = f.AsCompany("doyle", "crm:editor");
        await client.PostAsJsonAsync("/api/v1/crm/customers", new { name = "B", email = "b@x.com" });
        // relay disabled in this fixture: outbox row must still be there, unprocessed
        (await f.CountAsync("SELECT count(*) FROM crm.outbox WHERE processed_at IS NULL")).Should().BeGreaterThan(0);
    }
}
```

`HelmApiFactory` (complete): starts `PostgreSqlContainer`, overrides `ConnectionStrings:Helm`, sets `Messaging:Provider=InMemory`, disables hosted services for relay determinism via config flag `Outbox:RelayEnabled=false` (add this flag to `OutboxRelay` — default true), configures test JWT signing (issuer `helm-tests`, symmetric key) and `AsCompany` mints tokens with `company_id`/`module_role` claims; runs `db.Database.MigrateAsync()` for Core + Crm on startup.

- [ ] **Step 2: Run to verify it fails** (projects don't exist).
- [ ] **Step 3: Create the three projects** (`dotnet new classlib` ×2 + xunit, references: Crm→Core+Crm.Contracts; Crm.Contracts→Core *only for `IDomainEvent`*; Tests→Crm+Host), add to sln + Nx. Implement:
  - `Domain/Customer.cs`: `public class Customer : CompanyOwnedEntity { public Guid Id { get; init; } = Guid.NewGuid(); public required string Name { get; set; } public required string Email { get; set; } }`
  - `Infrastructure/CrmDbContext.cs`: derives `ModuleDbContext<CrmDbContext>`, `HasDefaultSchema("crm")`, maps `customer`, calls `OutboxModelBuilder.AddOutbox(b)`, then `base.OnModelCreating(b)` last.
  - `Application/CrmService.cs`: implements `ICrmService` (`AsNoTracking`, batch = `WHERE id IN`), maps to `CustomerDto`.
  - `Api/CrmEndpoints.cs`: minimal-API group `/api/v1/crm`; POST validates name/email non-empty (400 problem details), creates Customer + `OutboxWriter.Enqueue(db, new CustomerCreated(Guid.NewGuid(), c.Id, db.CurrentCompanyId, c.Name))`, one `SaveChangesAsync`, returns 201 + dto; GET list uses `PageRequest.Normalized()` → `PagedResult`.
  - `CrmModule.cs`: `AddCrmModule` + `MapCrmEndpoints` as in Interfaces. Wire into `Program.cs`.
  - `EVENT-CATALOG.md`: table row `CustomerCreated | published on POST /customers | consumers: Cpq (demo)`.
  - Migration: `dotnet ef migrations add InitCrm --project libs/api/Helm.Crm --startup-project apps/api/Helm.Host --context CrmDbContext` (history table `__ef_migrations` in schema `crm`).
- [ ] **Step 4: Tests pass** — `dotnet test libs/api/Helm.Crm.Tests` → 3 PASS.
- [ ] **Step 5: Commit** — `feat: CRM exemplar module (customer aggregate, outbox publish, paged API)`

---

### Task 12: Helm.ArchTests — build-breaking boundary enforcement

**Files:**
- Create: `libs/api/Helm.ArchTests/{Helm.ArchTests.csproj, ModuleBoundaryTests.cs, ModuleDiscovery.cs}`
- Modify: `Helm.sln`, Nx registration

**Interfaces:**
- Consumes: all `Helm.*` assemblies (project-references Host so everything loads).
- Produces: failing build on any boundary violation. `ModuleDiscovery.ModuleNames` — reflection over loaded `Helm.*` assemblies, modules = assemblies matching `Helm.<Name>` that have a matching `Helm.<Name>.Contracts` **or** are in `HelmModuleRegistry` (registry is source of truth; keep both in sync).

- [ ] **Step 1: Write the tests (they are the deliverable — TDD by nature):**

```csharp
using NetArchTest.Rules;

public class ModuleBoundaryTests
{
    public static IEnumerable<object[]> Modules => ModuleDiscovery.ModuleNames.Select(m => new object[] { m });

    [Theory, MemberData(nameof(Modules))]
    public void Module_does_not_reference_other_module_implementations(string module)
    {
        var others = ModuleDiscovery.ModuleNames.Where(m => m != module).Select(m => $"Helm.{m}").ToArray();
        Types.InAssembly(ModuleDiscovery.Assembly(module))
            .Should().NotHaveDependencyOnAny(others)
            .GetResult().IsSuccessful.Should().BeTrue($"{module} must depend on *.Contracts only");
    }

    [Theory, MemberData(nameof(Modules))]
    public void Contracts_do_not_reference_any_implementation(string module)
    {
        Types.InAssembly(ModuleDiscovery.Assembly($"{module}.Contracts"))
            .Should().NotHaveDependencyOnAny(ModuleDiscovery.ModuleNames.Select(m => $"Helm.{m}").ToArray())
            .GetResult().IsSuccessful.Should().BeTrue();
    }

    [Fact]
    public void Bffs_reference_contracts_only()
    {
        foreach (var bff in new[] { "Internal", "Portal", "Kiosk" })
            Types.InAssembly(ModuleDiscovery.Assembly($"Bff.{bff}"))
                .Should().NotHaveDependencyOnAny(ModuleDiscovery.ModuleNames.Select(m => $"Helm.{m}").ToArray())
                .GetResult().IsSuccessful.Should().BeTrue();  // skip silently until Task 16 creates BFFs? NO — guard with Assembly-exists check
    }
}
```

(`Bffs_reference_contracts_only` uses `ModuleDiscovery.TryAssembly` and only asserts for assemblies that exist, so this task lands before Task 16 without a skipped-test lie.)

- [ ] **Step 2: Run** — `dotnet test libs/api/Helm.ArchTests` → PASS with Crm (1 module × 2 theories). Temporarily add a `Helm.Crm` → `Helm.Core.Tests` style violation? No — instead verify the test bites: add `<ProjectReference>` from a scratch branch `Helm.Crm` → a fake second module and watch it fail, then revert (do this locally, don't commit the violation).
- [ ] **Step 3: Commit** — `test: NetArchTest module-boundary suite (auto-discovers modules)`

---

### Task 13: `helm-module` Nx generator (distilled from CRM)

**Files:**
- Create: `tools/generators/helm-module/{generator.json (or generators.json per Nx version), schema.json, generator.ts, files/**}`
- Modify: `package.json` (workspace generator registration), `nx.json` if needed

**Interfaces:**
- Consumes: the CRM module as the template source.
- Produces: `npx nx g helm-module <Name> --schema <schema_name>` emitting: `Helm.<Name>/` (Api/Application/Domain/Infrastructure + `<Name>Module.cs` + DbContext with outbox, **no entities**), `Helm.<Name>.Contracts/` (folders + EVENT-CATALOG.md stub), `Helm.<Name>.Tests/` (WebApplicationFactory boot smoke test), sln entries (`dotnet sln add` via `execSync`), Host `Program.cs` registration (marker-comment insertion), Nx project registration, initial EF migration instruction printed at the end.

- [ ] **Step 1:** Copy CRM files into `files/` as EJS templates (`__name__`, `<%= name %>`, `<%= schema %>`), **stripping the demo domain**: no Customer/CustomerCreated/CrmService — Api exposes only the module's health-noop group `/api/v1/<%= schema %>` with a `GET /_info` returning `{ module: "<%= name %>" }`; Contracts has empty `Dtos/ Events/ Interfaces/` folders with `.gitkeep` + naming README.
- [ ] **Step 2:** `generator.ts` (~60 lines): `generateFiles` + `names()` casing + insert `builder.Services.Add<%= className %>Module(builder.Configuration);` / `app.Map<%= className %>Endpoints();` at markers `// <helm-modules-services>` / `// <helm-modules-endpoints>` in `Program.cs` (add these marker comments to Program.cs in this task), run `dotnet sln add`, write Nx `project.json` when on the fallback path.
- [ ] **Step 3: Test the generator on a scratch module:** `npx nx g helm-module Scratch --schema scratch` → `dotnet build Helm.sln` green, `dotnet test libs/api/Helm.ArchTests` green (now discovers Scratch), Scratch smoke test passes. Then delete Scratch (`git clean -fd libs/api/Helm.Scratch*` + revert Program.cs/sln hunks).
- [ ] **Step 4: Commit** — `feat: helm-module workspace generator distilled from CRM`

---

### Task 14: Generate the remaining 9 modules

**Files:**
- Create (generated): `libs/api/Helm.{Cpq,ProposalOrder,Design,Planning,Inventory,PreProcessing,Manufacturing,Fulfilment,AfterSales}{,.Contracts,.Tests}`
- Modify: `Helm.sln`, `apps/api/Helm.Host/Program.cs`

**Interfaces:**
- Produces: all 10 modules registered; `HelmModuleRegistry.Modules` has 10 entries; ArchTests cover all automatically.

- [ ] **Step 1:** Run the generator 9 times with exact names/schemas from Global Constraints. After each: `dotnet build Helm.sln` (fail fast on a generator bug rather than 9 broken modules).
- [ ] **Step 2:** Generate EF migrations for each (loop the `dotnet ef migrations add Init<Name>` command with matching `--context`).
- [ ] **Step 3: Verify:** `dotnet test libs/api/Helm.ArchTests` → 10 modules × theories PASS. `dotnet test Helm.sln` → all smoke tests PASS. Boot Host with compose up: `/health` lists 10 healthy entries.
- [ ] **Step 4: Commit** — `feat: generate 9 module skeletons via helm-module generator`

---

### Task 15: Demo slice — CPQ consumer + the three spec integration tests

**Files:**
- Create: `libs/api/Helm.Cpq/Application/CustomerCreatedConsumer.cs`, `libs/api/Helm.Cpq/Domain/CustomerRef.cs`, migration `AddCustomerRef`
- Create: `libs/api/Helm.DemoSlice.Tests/{Helm.DemoSlice.Tests.csproj, OutboxEndToEndTests.cs, CompanyIsolationTests.cs, NoPhantomEventTests.cs}`
- Modify: `libs/api/Helm.Cpq/CpqModule.cs` (register consumer), `Helm.Cpq.csproj` (+ ref `Helm.Crm.Contracts`)

**Interfaces:**
- Consumes: `IdempotentConsumer<CpqDbContext, CustomerCreated>` (Task 9), `CustomerCreated` (Task 11), `HelmApiFactory` (Task 11 — extended here with `RelayEnabled=true` + RabbitMQ container option).
- Produces: `cpq.customer_ref` (`Guid CustomerId` PK, `string CompanyId`, `string Name`) written exactly once per event. This is the proof-of-infrastructure the spec's success criteria 2 demands.

- [ ] **Step 1: Write the three failing tests** (full RabbitMQ + Postgres containers, relay enabled):
  1. `OutboxEndToEndTests`: POST customer → poll ≤10s until `cpq.customer_ref` has 1 row; then re-publish the same envelope directly via `IEventBus` → still 1 row (idempotency).
  2. `CompanyIsolationTests`: seed customers for doyle + north via API with respective tokens; doyle token list → 1 item, by-id across companies → 404; `ntg` + `*:admin` token → 2 items and the log sink captured the `AUDIT` warning (assert via `ILoggerProvider` test sink registered in the factory).
  3. `NoPhantomEventTests`: POST with `email` that trips a validator added for this purpose? **No fake hooks** — instead call the application service directly with a poisoned DbContext wrapper that throws on the second `SaveChanges`? Simplest honest approach: in-test transaction — open `CrmDbContext`, add Customer + `OutboxWriter.Enqueue`, dispose without `SaveChangesAsync` → assert both `crm.customer` and `crm.outbox` are empty. This tests the actual atomicity property (one SaveChanges = one transaction).
- [ ] **Step 2: Implement** `CustomerRef` + consumer:

```csharp
public class CustomerCreatedConsumer(IServiceScopeFactory scopes, IEventBus bus, ILogger<CustomerCreatedConsumer> log)
    : IdempotentConsumer<CpqDbContext, CustomerCreated>(scopes, bus, log)
{
    protected override async Task HandleAsync(CustomerCreated evt, CpqDbContext db) =>
        db.Add(new CustomerRef { CustomerId = evt.CustomerId, CompanyId = evt.CompanyId, Name = evt.Name });
        // SaveChanges + processed_events insert happen in the base class transaction
}
```

Register in `CpqModule.AddCpqModule`. Update `Helm.Crm.Contracts/EVENT-CATALOG.md` consumer column.
- [ ] **Step 3: All three tests pass** — `dotnet test libs/api/Helm.DemoSlice.Tests` → PASS. Also rerun ArchTests (Cpq→Crm.Contracts ref must be legal, Cpq→Crm would fail).
- [ ] **Step 4: Commit** — `feat: demo slice — CPQ consumes CustomerCreated idempotently; isolation + atomicity proven`

---

### Task 16: Three BFF projects + per-BFF OpenAPI

**Files:**
- Create: `libs/api/Helm.Bff.Internal/{Helm.Bff.Internal.csproj, InternalBff.cs, Customers/CustomerListEndpoint.cs}`, `libs/api/Helm.Bff.Portal/{…, PortalBff.cs}`, `libs/api/Helm.Bff.Kiosk/{…, KioskBff.cs}`
- Modify: `apps/api/Helm.Host/Program.cs`, `Helm.sln`
- Test: `libs/api/Helm.Bff.Internal.Tests/InternalBffTests.cs`

**Interfaces:**
- Consumes: `ICrmService` from `Helm.Crm.Contracts` (BFFs reference Contracts ONLY — ArchTests already enforce this from Task 12).
- Produces: `AddInternalBff()/MapInternalBff(app)` etc.; routes `/bff/internal/*`, `/bff/portal/*`, `/bff/kiosk/*`. OpenAPI: three documents via `builder.Services.AddOpenApi("internal")` / `"portal"` / `"kiosk"` (Microsoft.AspNetCore.OpenApi), each BFF endpoint group tagged `.WithGroupName("internal")` etc.; module endpoints get `.WithGroupName("internal")` too (internal audience sees module APIs). Specs served at `/openapi/{group}.json` in Development.
- Internal BFF endpoint: `GET /bff/internal/customers?ids=…` → `ICrmService.GetCustomers` (the batch composition demo).

- [ ] **Step 1: Failing test** — `InternalBffTests` (uses `HelmApiFactory`): authorized GET `/bff/internal/customers?ids=<id>` returns the customer created via CRM API; anonymous → 401; and `GET /openapi/internal.json` (Development) contains path `/bff/internal/customers`.
- [ ] **Step 2: Implement** the three projects (Portal/Kiosk: just `Map<X>Bff` with an empty authorized group + OpenAPI doc registration). Wire into Host.
- [ ] **Step 3: Tests + ArchTests pass.**
- [ ] **Step 4: Commit** — `feat: per-audience BFFs with per-BFF OpenAPI docs`

---

### Task 17: Web libs + web-internal + generated api-client

**Files:**
- Create: `libs/web/shared-ui/` (Nx react lib), `libs/web/api-client/` (Nx js lib + generated `internal/schema.d.ts`), `libs/web/feature-crm/` (react lib), `apps/web-internal/` (react app)
- Create: `tools/scripts/generate-api-client.sh`
- Modify: `nx.json` (module boundary tags), `.eslintrc`/eslint config (boundary rules)

**Interfaces:**
- Consumes: `/openapi/internal.json` from a locally running Host.
- Produces:
  - `@helm/api-client/internal`: `openapi-typescript` types + a tiny typed `fetchJson<T>(path, init)` wrapper (hand-written, stable) — generated file is committed; regeneration must be diff-clean in CI.
  - `@helm/shared-ui`: `<Table>`, `<Button>` minimal components.
  - `@helm/feature-crm`: `CustomerListPage` (lists via GET, creates via POST using the generated types).
  - `apps/web-internal`: Vite React app, route `/` → `CustomerListPage`, auth stub = paste-a-JWT input stored in memory (explicitly marked dev-only).
  - Nx tags: `scope:shared` (shared-ui, api-client), `scope:feature` (feature-*), `scope:app`; eslint `@nx/enforce-module-boundaries`: `scope:feature` may depend only on `scope:shared` — **feature libs cannot import each other**.

- [ ] **Step 1: Generate libs/apps** with `@nx/react` generators (vite bundler, vitest, no router except app). Exact commands in-plan:

```bash
npx nx g @nx/react:library shared-ui --directory libs/web/shared-ui --bundler none --unitTestRunner vitest
npx nx g @nx/js:library api-client --directory libs/web/api-client --bundler none --unitTestRunner vitest
npx nx g @nx/react:library feature-crm --directory libs/web/feature-crm --bundler none --unitTestRunner vitest
npx nx g @nx/react:application web-internal --directory apps/web-internal --bundler vite --unitTestRunner vitest --e2eTestRunner none
```

- [ ] **Step 2: Failing component test** — `feature-crm`: `CustomerListPage` renders rows from a mocked `fetchJson` returning `{ items: [{id, name, email}], totalCount: 1 }` (msw or vi.mock; assert name visible + create form posts).
- [ ] **Step 3:** `generate-api-client.sh`: boots Host (`dotnet run` background, wait `/health`), `npx openapi-typescript http://localhost:5000/openapi/internal.json -o libs/web/api-client/src/internal/schema.d.ts`, kills Host. Run it; implement `fetchJson` + page until tests pass. TS strict everywhere, zero `any` (generated file included).
- [ ] **Step 4:** `npx nx run-many -t lint,test,build --projects=web-internal,feature-crm,shared-ui,api-client` → green. Manual check: compose up + `nx serve api` + `nx serve web-internal`, paste dev JWT, create + list a customer in the browser.
- [ ] **Step 5: Commit** — `feat: web-internal app + feature-crm + generated api-client (internal)`

---

### Task 18: web-portal + web-kiosk shells (kiosk = PWA scaffold)

**Files:**
- Create: `apps/web-portal/`, `apps/web-kiosk/` (React apps), `apps/web-kiosk/public/manifest.webmanifest`, `apps/web-kiosk/src/sw.ts`

**Interfaces:**
- Produces: two more Vite apps rendering a branded placeholder shell; kiosk registers a service worker that precaches the app shell (`vite-plugin-pwa`, `registerType: 'autoUpdate'`, no runtime caching rules yet — offline *sync* is explicitly out of scope per spec).

- [ ] **Step 1:** Generate both apps (same generator command pattern as Task 17).
- [ ] **Step 2:** Add `vite-plugin-pwa` to web-kiosk config + manifest (name "Helm Kiosk", display standalone). Vitest smoke test per app: shell renders app name.
- [ ] **Step 3:** `npx nx run-many -t build --projects=web-portal,web-kiosk` green; `npx vite preview` the kiosk build → Lighthouse/devtools shows SW registered, app loads offline after first visit (manual check, note in commit body).
- [ ] **Step 4: Commit** — `feat: portal + kiosk shells (kiosk as installable PWA)`

---

### Task 19: Playwright smoke test (web-internal)

**Files:**
- Create: `apps/web-internal-e2e/` (`@nx/playwright` project)

**Interfaces:**
- Produces: 1 test — page loads, JWT input visible, customer table renders against a mocked API (`page.route('**/bff/internal/**')` + `**/api/v1/crm/**` fulfilling canned JSON) — no backend needed in CI.

- [ ] **Step 1:** `npx nx g @nx/playwright:configuration --project web-internal-e2e` (or generate the e2e project alongside the app if the generator offers it).
- [ ] **Step 2:** Write the smoke test with route mocking; `npx nx e2e web-internal-e2e` → PASS (installs browsers on first run: `npx playwright install --with-deps chromium`).
- [ ] **Step 3: Commit** — `test: playwright smoke for web-internal`

---

### Task 20: Helm.Host Dockerfile + compose `full` profile

**Files:**
- Create: `apps/api/Helm.Host/Dockerfile`, `apps/web-internal/Dockerfile`, `apps/web-internal/nginx.conf`
- Modify: `docker-compose.yml`

**Interfaces:**
- Produces: `docker compose --profile full up` = whole stack, no SDK needed. The Host image is the same one ACA will run (ADR-010).

- [ ] **Step 1:** Multi-stage Dockerfile (Host):

```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish apps/api/Helm.Host -c Release -o /out

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /out .
EXPOSE 8080
ENTRYPOINT ["dotnet", "Helm.Host.dll"]
```

web-internal: `node:22` build stage (`npx nx build web-internal`) → `nginx:alpine` serving `dist` with SPA fallback (`try_files $uri /index.html;`).
- [ ] **Step 2:** Compose additions — `api` (profile `full`, build context `.`, env: connection string to `postgres`, `Messaging__Provider=RabbitMQ`, `Messaging__RabbitMq__Uri=amqp://helm:helm@rabbitmq:5672`, depends_on both healthy, ports `5000:8080`) and `web-internal` (profile `full`, ports `4200:80`).
- [ ] **Step 3: Verify:** `docker compose --profile full up --build -d --wait` → `curl localhost:5000/health` 200; `curl localhost:4200` serves the app. Tear down.
- [ ] **Step 4: Commit** — `feat: containerized full-stack compose profile`

---

### Task 21: Bicep skeleton

**Files:**
- Create: `infra/main.bicep`, `infra/modules/{container-apps.bicep, postgres.bicep, servicebus.bicep, keyvault.bicep, static-web-apps.bicep}`, `infra/README.md`

**Interfaces:**
- Produces: parameterized-per-environment skeleton that **compiles** (`az bicep build --file infra/main.bicep`). Each module file declares the real resource type with minimal required properties + `// TODO(infra phase)` is NOT allowed — instead each file carries a header comment pointing to the relevant ADR (010/011) and declares honest minimal resources (e.g. postgres flexible server with burstable sku, ACA environment + one container app referencing a parameterized image).

- [ ] **Step 1:** Write `main.bicep` (params: `env string`, `location string = resourceGroup().location`) composing the five modules; write each module with minimal valid resources.
- [ ] **Step 2:** `az bicep build --file infra/main.bicep` → exit 0 (no deployment).
- [ ] **Step 3: Commit** — `feat: bicep IaC skeleton (compiles; deploy is infra-phase work)`

---

### Task 22: CI (GitHub Actions) + pre-commit hooks

**Files:**
- Create: `.github/workflows/ci.yml`, `.husky/pre-commit`
- Modify: `package.json` (husky + lint-staged config)

**Interfaces:**
- Produces: PR/main pipeline: `nx affected -t lint,test,build` (Node + .NET, Testcontainers via runner Docker) + api-client drift check. Pre-commit: lint-staged (eslint --fix + prettier on staged TS, `dotnet format` on staged .cs).

- [ ] **Step 1:** `ci.yml`:

```yaml
name: ci
on:
  pull_request:
  push: { branches: [main] }
jobs:
  affected:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
        with: { fetch-depth: 0 }
      - uses: actions/setup-node@v4
        with: { node-version: 22, cache: npm }
      - uses: actions/setup-dotnet@v4
        with: { dotnet-version: 10.0.x }
      - run: npm ci
      - uses: nrwl/nx-set-shas@v4
      - run: npx nx affected -t lint,test,build --parallel=3
      # SonarCloud: not wired yet — needs org onboarding (spec: out of scope)
  api-client-drift:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-node@v4
        with: { node-version: 22, cache: npm }
      - uses: actions/setup-dotnet@v4
        with: { dotnet-version: 10.0.x }
      - run: npm ci
      - run: bash tools/scripts/generate-api-client.sh
      - run: git diff --exit-code libs/web/api-client/src
```

- [ ] **Step 2: Coverage gate (spec: 60% on Helm.Core only)** — add to `ci.yml` `affected` job after the nx step:

```yaml
      - run: dotnet test libs/api/Helm.Core.Tests /p:CollectCoverage=true /p:Threshold=60 /p:ThresholdType=line /p:Include="[Helm.Core]*"
```

(`coverlet.msbuild` package added to Helm.Core.Tests; empty module skeletons are exempt by scoping the gate to Helm.Core.)

- [ ] **Step 3:** `npx husky init`; `.husky/pre-commit` = `npx lint-staged`; lint-staged config: `"*.{ts,tsx}": ["eslint --fix", "prettier --write"]`, `"*.cs": ["dotnet format --include"]`.
- [ ] **Step 3:** Verify locally: `npx nx affected -t lint,test,build --base=HEAD~1` runs clean; make a whitespace commit to see the hook fire.
- [ ] **Step 4: Commit** — `ci: nx affected pipeline + api-client drift gate + pre-commit hooks`

---

### Task 23: Final verification sweep + README/spec close-out

**Files:**
- Modify: `README.md`, `docs/superpowers/specs/2026-07-06-helm-repo-scaffold-design.md`

**Interfaces:**
- Produces: every spec success criterion demonstrated and recorded.

- [ ] **Step 1: Run the five success criteria end-to-end, fresh clone semantics:**

```bash
git clean -xdn   # review; then git clean -xdf && npm ci
docker compose up -d --wait
dotnet test Helm.sln            # all module smokes + Core + ArchTests + demo slice
npx nx run-many -t lint,test,build
npx nx g helm-module Scratch --schema scratch && dotnet build Helm.sln && git clean -fd libs/api/Helm.Scratch* && git checkout -- .
docker compose --profile full up --build -d --wait && curl -f localhost:5000/health && docker compose down
```

Expected: every command exit 0.
- [ ] **Step 2:** Update README (full command table, module list, links to CLAUDE.md/engineering-rules). Tick the spec's success-criteria list with a one-line "verified <date>" note each. Confirm the Task 2 spike note landed in spec + ADR-009.
- [ ] **Step 3: Commit** — `docs: scaffold complete — success criteria verified`

---

## Task dependency order

0 → 1 → 2 → 3 → 4 → 5 → 6 → 7 → 8 → 9 → 10 → 11 → 12 → 13 → 14 → 15 → 16 → 17 → 18 → 19 → 20 → 21 → 22 → 23.
Strictly sequential except: 18, 19, 21 can run any time after their inputs (17, 17, 3 respectively); 20 needs 17.
