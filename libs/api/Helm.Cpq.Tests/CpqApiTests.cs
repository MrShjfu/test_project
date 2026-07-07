using System.Net;
using System.Text.Json;
using FluentAssertions;
using Helm.Crm.Tests;

namespace Helm.Cpq.Tests;

/// <summary>
/// Boot smoke test for the Cpq module. Reuses <see cref="HelmApiFactory"/> (from
/// Helm.Crm.Tests) to spin up the full Host — which registers every module, including this one —
/// and asserts the module's authorized info endpoint is wired and its auth default holds.
/// The `/_info` endpoint touches no database, so no Cpq-schema migration is needed here;
/// add DB-backed tests once this module has entities and its own migration.
/// </summary>
public class CpqApiTests(HelmApiFactory f) : IClassFixture<HelmApiFactory>
{
    [Fact]
    public async Task Info_endpoint_returns_module_name()
    {
        var client = f.AsCompany("doyle");
        var res = await client.GetAsync("/api/v1/cpq/_info");
        res.StatusCode.Should().Be(HttpStatusCode.OK);

        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("module").GetString().Should().Be("Cpq");
    }

    [Fact]
    public async Task Info_endpoint_requires_authentication()
    {
        (await f.CreateClient().GetAsync("/api/v1/cpq/_info"))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
