using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Web;

namespace Microsoft.Extensions.Hosting;

public static class AuthExtensions
{
    /// <summary>One way to authenticate for the gateway, Order and Inventory. <c>Auth:Mode</c> absent or
    /// <c>Entra</c> → Entra ID bearer validation from the <c>AzureAd</c> section. <c>Auth:Mode=Local</c> →
    /// <see cref="LocalDevAuthenticationHandler"/>, refused in Production. Policies stay in each service.</summary>
    public static TBuilder AddOrderFlowAuthentication<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        var mode = builder.Configuration["Auth:Mode"] ?? "Entra";

        if (string.Equals(mode, "Local", StringComparison.OrdinalIgnoreCase))
        {
            // A switch that cannot be flipped where it matters: Helm sets ASPNETCORE_ENVIRONMENT=Production.
            if (builder.Environment.IsProduction())
            {
                throw new InvalidOperationException("Auth:Mode=Local is refused when ASPNETCORE_ENVIRONMENT is Production.");
            }

            builder.Services
                .AddAuthentication(LocalDevAuthenticationHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, LocalDevAuthenticationHandler>(
                    LocalDevAuthenticationHandler.SchemeName, _ => { });
        }
        else
        {
            // Validates: signature (keys from the tenant's OIDC metadata, cached and rotated for us),
            // issuer (our tenant), audience (AzureAd:ClientId or api://ClientId), lifetime — and rejects
            // tokens that carry neither scp nor roles.
            builder.Services
                .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
                .AddMicrosoftIdentityWebApi(builder.Configuration.GetSection("AzureAd"));

            builder.Services.Configure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, o =>
            {
                // Keep Entra's short claim names ("oid", "scp", "roles") instead of the legacy
                // SOAP-era URIs, and make RequireRole / IsInRole read the "roles" claim.
                o.MapInboundClaims = false;
                o.TokenValidationParameters.RoleClaimType = "roles";
                o.TokenValidationParameters.NameClaimType = "name";
            });
        }

        // The handler behind policy.RequireScope(...). Entra mode registers it already; Local mode
        // needs it, or every scope requirement would silently fail with 403. Safe to call twice.
        builder.Services.AddRequiredScopeAuthorization();

        return builder;
    }
}

/// <summary>Development and CI only. Signs every request in as a principal shaped exactly like an
/// Entra access token (oid, scp, roles), so the SAME authorization policies and ownership checks run
/// locally. Override per request with X-Dev-User / X-Dev-Roles to test ownership and roles.</summary>
public sealed class LocalDevAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "LocalDev";
    public const string DefaultUserId = "11111111-1111-1111-1111-111111111111";

    private static readonly string[] DefaultRoles = ["OrderFlow.Customer", "OrderFlow.Support", "Inventory.Read"];

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var userId = Request.Headers["X-Dev-User"].FirstOrDefault() ?? DefaultUserId;
        var roles = Request.Headers["X-Dev-Roles"].FirstOrDefault()?
                        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    ?? DefaultRoles;

        var claims = new List<Claim>
        {
            new("oid", userId),
            new("name", "Local Developer"),
            new("scp", "Orders.ReadWrite"),
        };
        claims.AddRange(roles.Select(role => new Claim("roles", role)));

        var identity = new ClaimsIdentity(claims, SchemeName, nameType: "name", roleType: "roles");
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
