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
    public static TBuilder AddOrderFlowAuthentication<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        var mode = builder.Configuration["Auth:Mode"] ?? "Entra";

        if (string.Equals(mode, "Local", StringComparison.OrdinalIgnoreCase))
        {
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
            builder.Services
                .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
                .AddMicrosoftIdentityWebApi(builder.Configuration.GetSection("AzureAd"));

            builder.Services.Configure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, o =>
            {
                o.MapInboundClaims = false;
                o.TokenValidationParameters.RoleClaimType = "roles";
                o.TokenValidationParameters.NameClaimType = "name";
            });
        }

        builder.Services.AddRequiredScopeAuthorization();

        return builder;
    }
}

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
