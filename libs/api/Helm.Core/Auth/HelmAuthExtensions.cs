using Helm.Core.MultiCompany;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Helm.Core.Auth;

public static class HelmAuthExtensions
{
    public static IServiceCollection AddHelmAuth(this IServiceCollection services, IConfiguration config)
    {
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(); // dev: dotnet user-jwts config; prod: Entra section (placeholder, infra phase)
        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser().Build());
        services.AddMemoryCache();
        services.AddScoped<IClaimsTransformation, UserClaimsTransformer>();
        services.AddHttpContextAccessor();
        services.AddScoped<ICompanyContext>(sp =>
            HttpCompanyContext.FromPrincipal(
                sp.GetRequiredService<IHttpContextAccessor>().HttpContext?.User
                ?? throw new InvalidOperationException("no http context")));
        return services;
    }

    public static WebApplication UseHelmAuth(this WebApplication app)
    {
        app.UseAuthentication();
        app.UseAuthorization();
        return app;
    }
}
