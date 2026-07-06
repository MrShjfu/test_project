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
