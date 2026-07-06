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
