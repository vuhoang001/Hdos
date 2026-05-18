using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace Hdos.Common.Auth;

public static class JwtAuthExtensions
{
    /// <summary>
    /// Validates Keycloak-issued JWTs using JWKS discovery (asymmetric keys).
    /// Authority is read from Keycloak:Authority in configuration.
    /// Call this from every service that needs to authenticate requests.
    /// </summary>
    public static IServiceCollection AddHdosJwtAuth(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var opts = configuration.GetSection(KeycloakOptions.SectionName).Get<KeycloakOptions>()
                   ?? new KeycloakOptions();

        services.Configure<KeycloakOptions>(configuration.GetSection(KeycloakOptions.SectionName));

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(o =>
            {
                o.Authority = opts.Authority;
                o.Audience = opts.Audience;
                // Allow HTTP in dev/Docker environments (no TLS internally)
                o.RequireHttpsMetadata = false;
                // When Authority is a public HTTPS URL but services run inside Docker,
                // use the internal Keycloak URL for JWKS discovery to avoid cert issues.
                if (!string.IsNullOrWhiteSpace(opts.MetadataAddress))
                    o.MetadataAddress = opts.MetadataAddress;
                o.SaveToken = true;
                o.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer           = !string.IsNullOrWhiteSpace(opts.Authority),
                    ValidateAudience         = !string.IsNullOrWhiteSpace(opts.Audience),
                    ValidateLifetime         = true,
                    ClockSkew                = TimeSpan.FromSeconds(30),
                    // Keycloak puts roles in "roles" claim (after realm-roles mapper is configured)
                    RoleClaimType            = "roles",
                    NameClaimType            = "preferred_username",
                };

                // SignalR WebSocket: token arrives via ?access_token= query string
                o.Events = new JwtBearerEvents
                {
                    OnMessageReceived = ctx =>
                    {
                        var token = ctx.Request.Query["access_token"];
                        var path  = ctx.HttpContext.Request.Path;
                        if (!string.IsNullOrEmpty(token) && path.HasValue &&
                            path.Value!.Contains("/hubs/", StringComparison.OrdinalIgnoreCase))
                            ctx.Token = token;
                        return Task.CompletedTask;
                    }
                };
            });

        services.AddAuthorization();
        return services;
    }

    /// <summary>
    /// Registers fine-grained permission policies keyed by HdosPermissions constants.
    /// Policies resolve permission claims injected by PermissionsMiddleware from the
    /// X-User-Permissions header that nginx forwards from AuthService /auth/validate.
    /// Call this from every service that uses [Authorize(Policy = HdosPermissions.Xxx)].
    /// </summary>
    public static IServiceCollection AddHdosAuthorization(this IServiceCollection services)
    {
        services.AddAuthorization(options =>
        {
            options.AddPolicy(HdosPermissions.OrdersCreate,      p => p.RequireClaim("permission", HdosPermissions.OrdersCreate));
            options.AddPolicy(HdosPermissions.OrdersRead,        p => p.RequireClaim("permission", HdosPermissions.OrdersRead));
            options.AddPolicy(HdosPermissions.OrdersUpdate,      p => p.RequireClaim("permission", HdosPermissions.OrdersUpdate));
            options.AddPolicy(HdosPermissions.OrdersDelete,      p => p.RequireClaim("permission", HdosPermissions.OrdersDelete));

            options.AddPolicy(HdosPermissions.NotificationsRead, p => p.RequireClaim("permission", HdosPermissions.NotificationsRead));
            options.AddPolicy(HdosPermissions.NotificationsSend, p => p.RequireClaim("permission", HdosPermissions.NotificationsSend));

            options.AddPolicy(HdosPermissions.M01Read,           p => p.RequireClaim("permission", HdosPermissions.M01Read));
            options.AddPolicy(HdosPermissions.M01Write,          p => p.RequireClaim("permission", HdosPermissions.M01Write));

            options.AddPolicy(HdosPermissions.AsyncSubmit,       p => p.RequireClaim("permission", HdosPermissions.AsyncSubmit));

            options.AddPolicy(HdosPermissions.UsersManage,       p => p.RequireClaim("permission", HdosPermissions.UsersManage));
            options.AddPolicy(HdosPermissions.RolesManage,       p => p.RequireClaim("permission", HdosPermissions.RolesManage));
        });
        return services;
    }
}
