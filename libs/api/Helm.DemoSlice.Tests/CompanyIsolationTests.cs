using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Helm.Crm.Tests;

namespace Helm.DemoSlice.Tests;

/// <summary>
/// Proves ADR-003 multi-company isolation end-to-end through the HTTP surface: a non-admin
/// company only ever sees its own rows (list + by-id), and the one path that legitimately crosses
/// companies (ntg group-admin) both sees everything AND leaves an audit trail. Uses the plain
/// InMemory <see cref="HelmApiFactory"/> — no RabbitMQ/relay needed since nothing here depends on
/// async event delivery, only on synchronous CRUD + the company query filter.
/// </summary>
public class CompanyIsolationTests : IClassFixture<HelmApiFactory>, IAsyncLifetime
{
    private readonly HelmApiFactory _factory;

    public CompanyIsolationTests(HelmApiFactory factory) => _factory = factory;

    public Task InitializeAsync() => _factory.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private record CreateCustomerRequest(string Name, string Email);
    private record CustomerDto(Guid Id, string Name, string Email);
    private record PagedResult<T>(IReadOnlyList<T> Items, int TotalCount);

    [Fact]
    public async Task Non_admin_company_sees_only_its_own_customers()
    {
        var doyleClient = _factory.AsCompany("doyle", "crm:write");
        var northClient = _factory.AsCompany("north", "crm:write");

        var doyleCreate = await doyleClient.PostAsJsonAsync("/api/v1/crm/customers",
            new CreateCustomerRequest("Doyle Customer", "doyle-customer@example.com"));
        doyleCreate.StatusCode.Should().Be(HttpStatusCode.Created);
        var doyleCustomer = await doyleCreate.Content.ReadFromJsonAsync<CustomerDto>();

        var northCreate = await northClient.PostAsJsonAsync("/api/v1/crm/customers",
            new CreateCustomerRequest("North Customer", "north-customer@example.com"));
        northCreate.StatusCode.Should().Be(HttpStatusCode.Created);
        var northCustomer = await northCreate.Content.ReadFromJsonAsync<CustomerDto>();

        // Doyle's list contains exactly its own customer, never north's.
        var doyleList = await doyleClient.GetFromJsonAsync<PagedResult<CustomerDto>>("/api/v1/crm/customers?pageSize=50");
        doyleList!.Items.Should().ContainSingle(c => c.Id == doyleCustomer!.Id);
        doyleList.Items.Should().NotContain(c => c.Id == northCustomer!.Id);

        // Doyle cannot fetch north's customer by id — 404, not a cross-company leak.
        var crossFetch = await doyleClient.GetAsync($"/api/v1/crm/customers/{northCustomer!.Id}");
        crossFetch.StatusCode.Should().Be(HttpStatusCode.NotFound);

        // ntg group-admin (*:admin role) sees both companies' customers.
        var adminClient = _factory.AsCompany("ntg", "*:admin");
        var adminList = await adminClient.GetFromJsonAsync<PagedResult<CustomerDto>>("/api/v1/crm/customers?pageSize=50");
        adminList!.Items.Should().Contain(c => c.Id == doyleCustomer!.Id);
        adminList.Items.Should().Contain(c => c.Id == northCustomer!.Id);
        adminList.TotalCount.Should().Be(2);
    }

    [Fact]
    public async Task Group_admin_cross_company_access_is_audit_logged()
    {
        var doyleClient = _factory.AsCompany("doyle", "crm:write");
        await doyleClient.PostAsJsonAsync("/api/v1/crm/customers",
            new CreateCustomerRequest("Audit Trail Co", "audit@example.com"));

        var adminClient = _factory.AsCompany("ntg", "*:admin");
        (await adminClient.GetAsync("/api/v1/crm/customers?pageSize=50")).StatusCode.Should().Be(HttpStatusCode.OK);

        _factory.LogSink.Messages.Should().Contain(m => m.Contains("AUDIT cross-company access granted"),
            "ModuleDbContext must log an audit warning whenever a group-admin ICompanyContext bypasses the company filter (ADR-003)");
    }
}
