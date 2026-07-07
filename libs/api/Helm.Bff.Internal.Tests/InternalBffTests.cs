using System.Net;
using System.Net.Http.Json;
using Helm.Crm.Contracts.Dtos;
using Helm.Crm.Tests;
using FluentAssertions;

namespace Helm.Bff.Internal.Tests;

// Same reset pattern as CrmApiTests: truncate between [Fact]s sharing the IClassFixture instance.
public class InternalBffTests(HelmApiFactory f) : IClassFixture<HelmApiFactory>, IAsyncLifetime
{
    public async Task InitializeAsync() => await f.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Authorized_get_returns_customer_created_via_crm_api()
    {
        var client = f.AsCompany("doyle", "crm:editor");

        var created = await client.PostAsJsonAsync("/api/v1/crm/customers", new { name = "Aldo", email = "a@x.com" });
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var customer = await created.Content.ReadFromJsonAsync<CustomerDto>();

        var res = await client.GetAsync($"/bff/internal/customers?ids={customer!.Id}");
        res.StatusCode.Should().Be(HttpStatusCode.OK);

        var customers = await res.Content.ReadFromJsonAsync<List<CustomerDto>>();
        customers.Should().ContainSingle(c => c.Id == customer.Id && c.Name == "Aldo");
    }

    [Fact]
    public async Task Anonymous_gets_401()
    {
        var res = await f.CreateClient().GetAsync($"/bff/internal/customers?ids={Guid.NewGuid()}");
        res.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Malformed_ids_returns_400_problem_details()
    {
        var client = f.AsCompany("doyle", "crm:editor");
        var res = await client.GetAsync("/bff/internal/customers?ids=not-a-guid");
        res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Missing_ids_returns_400_problem_details()
    {
        var client = f.AsCompany("doyle", "crm:editor");
        var res = await client.GetAsync("/bff/internal/customers");
        res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Internal_openapi_document_contains_bff_and_module_paths()
    {
        var client = f.AsCompany("doyle", "crm:editor");
        var res = await client.GetAsync("/openapi/internal.json");
        res.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await res.Content.ReadAsStringAsync();
        body.Should().Contain("/bff/internal/customers");
        body.Should().Contain("/api/v1/crm");
    }

    [Fact]
    public async Task Internal_openapi_document_is_reachable_anonymously()
    {
        var res = await f.CreateClient().GetAsync("/openapi/internal.json");
        res.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
