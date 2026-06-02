using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace Hdos.Common.Auth;

public static class JwtAuthExtensions
{
    /// <summary>
    /// Validate JWT do AuthService phát hành (HS256 với shared secret).
    /// Mọi service đều dùng — cùng Issuer/Audience/Secret từ section "Jwt".
    /// </summary>
    public static IServiceCollection AddHdosJwtAuth(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var opts = configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()
            ?? new JwtOptions();

        if (string.IsNullOrWhiteSpace(opts.Secret) || opts.Secret.Length < 32)
            throw new InvalidOperationException(
                "Jwt:Secret phải >= 32 ký tự. Set qua env Jwt__Secret.");

        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.SectionName));
        services.AddSingleton<IJwtTokenIssuer, JwtTokenIssuer>();

        var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(opts.Secret));

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(o =>
            {
                o.RequireHttpsMetadata = false;
                o.SaveToken            = true;
                o.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer           = true,
                    ValidIssuer              = opts.Issuer,
                    ValidateAudience         = true,
                    ValidAudience            = opts.Audience,
                    ValidateLifetime         = true,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey         = signingKey,
                    ClockSkew                = TimeSpan.FromSeconds(30),
                    RoleClaimType            = "roles",
                    NameClaimType            = "preferred_username",
                };

                // SSE / SignalR: token có thể đến qua ?access_token=
                o.Events = new JwtBearerEvents
                {
                    OnMessageReceived = ctx =>
                    {
                        var token = ctx.Request.Query["access_token"];
                        var path  = ctx.HttpContext.Request.Path;
                        if (!string.IsNullOrEmpty(token) && path.HasValue &&
                            (path.Value!.Contains("/hubs/", StringComparison.OrdinalIgnoreCase) ||
                                path.Value!.Contains("/sse", StringComparison.OrdinalIgnoreCase)))
                            ctx.Token = token;
                        return Task.CompletedTask;
                    }
                };
            });

        services.AddAuthorization();
        return services;
    }

    /// <summary>
    /// Đăng ký policies fine-grained theo HdosPermissions constants.
    /// Permission claim được JwtTokenIssuer nhồi trực tiếp vào JWT khi login.
    /// </summary>
    public static IServiceCollection AddHdosAuthorization(this IServiceCollection services)
    {
        services.AddAuthorization(options =>
        {
            options.AddPolicy(HdosPermissions.OrdersCreate,
                              p => p.RequireClaim("permission", HdosPermissions.OrdersCreate));
            options.AddPolicy(HdosPermissions.OrdersRead,
                              p => p.RequireClaim("permission", HdosPermissions.OrdersRead));
            options.AddPolicy(HdosPermissions.OrdersUpdate,
                              p => p.RequireClaim("permission", HdosPermissions.OrdersUpdate));
            options.AddPolicy(HdosPermissions.OrdersDelete,
                              p => p.RequireClaim("permission", HdosPermissions.OrdersDelete));

            options.AddPolicy(HdosPermissions.NotificationsRead,
                              p => p.RequireClaim("permission", HdosPermissions.NotificationsRead));
            options.AddPolicy(HdosPermissions.NotificationsSend,
                              p => p.RequireClaim("permission", HdosPermissions.NotificationsSend));

            options.AddPolicy(HdosPermissions.M01Read, p => p.RequireClaim("permission", HdosPermissions.M01Read));
            options.AddPolicy(HdosPermissions.M01Write, p => p.RequireClaim("permission", HdosPermissions.M01Write));

            options.AddPolicy(HdosPermissions.AsyncSubmit,
                              p => p.RequireClaim("permission", HdosPermissions.AsyncSubmit));

            options.AddPolicy(HdosPermissions.UsersManage,
                              p => p.RequireClaim("permission", HdosPermissions.UsersManage));
            options.AddPolicy(HdosPermissions.RolesManage,
                              p => p.RequireClaim("permission", HdosPermissions.RolesManage));

            // License module policies — check claim "lic_mod" trong JWT
            options.AddPolicy(HdosLicensePolicies.ModuleOrders,
                              p => p.RequireClaim(LicenseClaimTypes.Module, HdosModules.Orders));
            options.AddPolicy(HdosLicensePolicies.ModuleNotifications,
                              p => p.RequireClaim(LicenseClaimTypes.Module, HdosModules.Notifications));
            options.AddPolicy(HdosLicensePolicies.ModuleM01,
                              p => p.RequireClaim(LicenseClaimTypes.Module, HdosModules.M01));
            options.AddPolicy(HdosLicensePolicies.ModuleDataMatching,
                              p => p.RequireClaim(LicenseClaimTypes.Module, HdosModules.DataMatching));
            options.AddPolicy(HdosLicensePolicies.ModuleForms,
                              p => p.RequireClaim(LicenseClaimTypes.Module, HdosModules.Forms));
            options.AddPolicy(HdosLicensePolicies.ModuleAsync,
                              p => p.RequireClaim(LicenseClaimTypes.Module, HdosModules.Async));
        });
        return services;
    }
}