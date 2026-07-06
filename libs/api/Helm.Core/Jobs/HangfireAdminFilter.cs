using Hangfire.Dashboard;

namespace Helm.Core.Jobs;

/// <summary>
/// Restricts the Hangfire dashboard (mounted at <c>/hangfire</c>) to authenticated NTG-group
/// admins, per ADR-005. Roles live in core.users and are hydrated into the ClaimsPrincipal
/// after authentication (not carried in the Entra token) — see UserClaimsTransformer.
/// </summary>
public class HangfireAdminFilter : IDashboardAuthorizationFilter
{
    public bool Authorize(DashboardContext context)
    {
        var user = context.GetHttpContext().User;
        return user.Identity?.IsAuthenticated == true
            && user.HasClaim("module_role", "*:admin");
    }
}
