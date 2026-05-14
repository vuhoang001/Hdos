namespace Hdos.Common.Auth;

/// <summary>
/// Canonical permission strings used as both policy names and X-User-Permissions values.
/// Format: "{service}:{action}"
/// AuthService admin endpoints use [Authorize(Roles="admin")] directly from Keycloak JWT.
/// All other service endpoints use [Authorize(Policy = HdosPermissions.Xxx)].
/// </summary>
public static class HdosPermissions
{
    public const string OrdersCreate  = "orders:create";
    public const string OrdersRead    = "orders:read";
    public const string OrdersUpdate  = "orders:update";
    public const string OrdersDelete  = "orders:delete";

    public const string NotificationsRead = "notifications:read";
    public const string NotificationsSend = "notifications:send";

    public const string M01Read  = "m01:read";
    public const string M01Write = "m01:write";

    public const string AsyncSubmit = "async:submit";

    public const string UsersManage = "users:manage";
    public const string RolesManage = "roles:manage";
}
