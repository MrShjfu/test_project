using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Helm.Core.Api;
using Helm.Crm.Contracts.Dtos;

namespace Helm.Crm.Tests;

// IAsyncLifetime.InitializeAsync runs before each [Fact] (xUnit constructs a fresh test class
// instance per test) — used here to truncate crm.customer between tests so the shared
// (IClassFixture) Postgres container's state from one test doesn't leak into the next. The
// container/host themselves are still created once per class (expensive Testcontainers spin-up),
// only the customer rows are reset.
public class CrmApiTests(HelmApiFactory f) : IClassFixture<HelmApiFactory>, IAsyncLifetime
{
    public async Task InitializeAsync() => await f.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

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

    [Fact]
    public async Task Get_missing_customer_returns_404_problem_details_with_trace_id()
    {
        var client = f.AsCompany("doyle", "crm:editor");
        var res = await client.GetAsync($"/api/v1/crm/customers/{Guid.NewGuid()}");
        res.StatusCode.Should().Be(HttpStatusCode.NotFound);
        res.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");

        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        doc.RootElement.TryGetProperty("traceId", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Health_endpoint_is_anonymous()
    {
        (await f.CreateClient().GetAsync("/health")).StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
