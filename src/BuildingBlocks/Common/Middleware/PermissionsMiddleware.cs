using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace Hdos.Common.Middleware;

/// <summary>
/// Reads the X-User-Permissions header injected by nginx (from AuthService /auth/validate)
/// and adds each permission as a "permission" claim on the current user identity.
/// Must run AFTER UseAuthentication() and BEFORE UseAuthorization().
/// </summary>
public sealed class PermissionsMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (context.User.Identity?.IsAuthenticated == true)
        {
            var header = context.Request.Headers["X-User-Permissions"].ToString();
            if (!string.IsNullOrWhiteSpace(header))
            {
                var identity = context.User.Identity as ClaimsIdentity;
                foreach (var permission in header.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    identity?.AddClaim(new Claim("permission", permission));
            }
        }
        await next(context);
    }
}
