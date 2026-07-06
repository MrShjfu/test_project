using System.Security.Claims;
using Helm.Core.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Helm.Core.Auth;

public class UserClaimsTransformer(CoreDbContext db, IMemoryCache cache) : IClaimsTransformation
{
    public async Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        if (principal.HasClaim(c => c.Type == HelmClaims.CompanyId)) return principal; // dev tokens
        var email = principal.FindFirstValue(ClaimTypes.Email) ?? principal.FindFirstValue("preferred_username");
        if (email is null) return principal;
        var user = await cache.GetOrCreateAsync($"user:{email}", async e =>
        {
            e.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5);
            return await db.Set<User>().AsNoTracking().SingleOrDefaultAsync(u => u.Email == email);
        });
        if (user is null) return principal;
        var id = new ClaimsIdentity();
        id.AddClaim(new Claim(HelmClaims.CompanyId, user.CompanyId));
        foreach (var r in user.ModuleRoles) id.AddClaim(new Claim(HelmClaims.ModuleRole, r));
        principal.AddIdentity(id);
        return principal;
    }
}
