using Helm.Core.Api;
using Helm.Core.Outbox;
using Helm.Crm.Contracts.Dtos;
using Helm.Crm.Contracts.Events;
using Helm.Crm.Domain;
using Helm.Crm.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Helm.Crm.Api;

public static class CrmEndpoints
{
    // Matches Helm.Bff.Internal.InternalBff.OpenApiDocumentName. Duplicated as a literal rather
    // than referenced because Helm.Crm must not depend on Helm.Bff.Internal (ADR-001/002 module
    // isolation runs the other way: BFFs depend on module Contracts, never the reverse).
    private const string InternalBffGroupName = "internal";

    public static IEndpointRouteBuilder MapCrmCustomerEndpoints(this IEndpointRouteBuilder app)
    {
        // Disclosed change (Task 16): tagged "internal" so the Internal Platform OpenAPI document
        // discloses module APIs to NTG-internal staff tooling, per the per-BFF-OpenAPI decision.
        var group = app.MapGroup("/api/v1/crm").WithGroupName(InternalBffGroupName);

        // .Produces<T>() calls below are OpenAPI-metadata-only (no behavior change): minimal API
        // handlers returning IResult can't have their response body inferred, so without these the
        // generated internal.json (and therefore libs/web/api-client's schema.d.ts) has untyped
        // response content — Task 17 depends on typed response schemas here.
        group.MapPost("/customers", CreateCustomer)
            .Produces<CustomerDto>(StatusCodes.Status201Created)
            .ProducesValidationProblem();
        group.MapGet("/customers", ListCustomers)
            .Produces<PagedResult<CustomerDto>>();
        group.MapGet("/customers/{id:guid}", GetCustomer)
            .Produces<CustomerDto>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }

    private static async Task<IResult> CreateCustomer(CreateCustomerRequest request, CrmDbContext db, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.Email))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["name"] = string.IsNullOrWhiteSpace(request.Name) ? ["Name is required."] : [],
                ["email"] = string.IsNullOrWhiteSpace(request.Email) ? ["Email is required."] : [],
            }.Where(kv => kv.Value.Length > 0).ToDictionary(kv => kv.Key, kv => kv.Value));
        }

        var customer = new Customer { Name = request.Name, Email = request.Email };
        db.Add(customer);
        OutboxWriter.Enqueue(db, new CustomerCreated(Guid.NewGuid(), customer.Id, db.CurrentCompanyId, customer.Name));

        await db.SaveChangesAsync(ct);

        var dto = new CustomerDto(customer.Id, customer.Name, customer.Email);
        return Results.Created($"/api/v1/crm/customers/{customer.Id}", dto);
    }

    private static async Task<IResult> ListCustomers([AsParameters] PageRequest pageRequest, CrmDbContext db, CancellationToken ct)
    {
        var page = pageRequest.Normalized();

        var query = db.Set<Customer>().AsNoTracking().OrderBy(c => c.Name).ThenBy(c => c.Id);

        var totalCount = await query.CountAsync(ct);
        var items = await query.Skip(page.Skip).Take(page.PageSize)
            .Select(c => new CustomerDto(c.Id, c.Name, c.Email))
            .ToListAsync(ct);

        return Results.Ok(new PagedResult<CustomerDto>(items, totalCount));
    }

    private static async Task<IResult> GetCustomer(Guid id, CrmDbContext db, CancellationToken ct)
    {
        var customer = await db.Set<Customer>().AsNoTracking().SingleOrDefaultAsync(c => c.Id == id, ct);
        if (customer is null)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Customer not found",
                detail: $"No customer with id '{id}' was found.");
        }

        return Results.Ok(new CustomerDto(customer.Id, customer.Name, customer.Email));
    }

    private record CreateCustomerRequest(string Name, string Email);
}
