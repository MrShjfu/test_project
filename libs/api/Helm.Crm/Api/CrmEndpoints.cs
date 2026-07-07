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
    public static IEndpointRouteBuilder MapCrmCustomerEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/crm");

        group.MapPost("/customers", CreateCustomer);
        group.MapGet("/customers", ListCustomers);
        group.MapGet("/customers/{id:guid}", GetCustomer);

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
