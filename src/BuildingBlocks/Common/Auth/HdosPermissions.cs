namespace Hdos.Common.Auth;

/// <summary>
/// Permission strings dùng cho cả policy name lẫn X-User-Permissions value.
/// Format: "{service}:{action}".
/// AuthService admin endpoints dùng [Authorize(Roles="admin")] đọc từ JWT "roles" claim.
/// Service khác dùng [Authorize(Policy = HdosPermissions.Xxx)] đọc từ permission claim (PermissionsMiddleware).
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

    public static readonly IReadOnlyList<string> All =
    [
        OrdersCreate, OrdersRead, OrdersUpdate, OrdersDelete,
        NotificationsRead, NotificationsSend,
        M01Read, M01Write,
        AsyncSubmit,
        UsersManage, RolesManage,
    ];
}
