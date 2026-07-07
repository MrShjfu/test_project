using Helm.Crm.Contracts;
using Helm.Crm.Contracts.Dtos;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Helm.Bff.Internal.Customers;

/// <summary>
/// Composition demo (Task 16): the Internal BFF's read-side calls <see cref="ICrmService"/>'s
/// batch overload — never single-id calls in a loop (ADR-001/004, engineering-rules §2) — to
/// fetch a set of customers by id in one round trip.
/// </summary>
public static class CustomerListEndpoint
{
    public static IEndpointRouteBuilder MapCustomerListEndpoint(this IEndpointRouteBuilder app)
    {
        app.MapGet("/customers", GetCustomers)
            .Produces<IReadOnlyList<CustomerDto>>()
            .ProducesValidationProblem();
        return app;
    }

    private static async Task<IResult> GetCustomers(string? ids, ICrmService crmService, CancellationToken ct)
    {
        var parsedIds = ParseIds(ids, out var error);
        if (error is not null)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["ids"] = [error] });
        }

        var customers = await crmService.GetCustomers(parsedIds!, ct);
        return Results.Ok(customers);
    }

    private static IReadOnlyCollection<Guid>? ParseIds(string? ids, out string? error)
    {
        if (string.IsNullOrWhiteSpace(ids))
        {
            error = "At least one id is required.";
            return null;
        }

        var parts = ids.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            error = "At least one id is required.";
            return null;
        }

        var parsed = new List<Guid>(parts.Length);
        foreach (var part in parts)
        {
            if (!Guid.TryParse(part, out var id))
            {
                error = $"'{part}' is not a valid id.";
                return null;
            }

            parsed.Add(id);
        }

        error = null;
        return parsed;
    }
}
