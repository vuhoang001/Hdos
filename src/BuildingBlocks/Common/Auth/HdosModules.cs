namespace Hdos.Common.Auth;

/// <summary>
/// Module slugs được embed vào JWT claim "lic_mod".
/// Service dùng [Authorize(Policy = HdosLicensePolicies.ModuleXxx)] để enforce.
/// </summary>
public static class HdosModules
{
    public const string Orders        = "orders";
    public const string Notifications = "notifications";
    public const string M01           = "m01";
    public const string DataMatching  = "data-matching";
    public const string Forms         = "forms";
    public const string Async         = "async";

    public static readonly IReadOnlyList<string> All =
    [
        Orders, Notifications, M01, DataMatching, Forms, Async,
    ];
}

/// <summary>
/// Policy names cho license module — dùng với [Authorize(Policy = ...)].
/// </summary>
public static class HdosLicensePolicies
{
    public const string ModuleOrders        = "license:orders";
    public const string ModuleNotifications = "license:notifications";
    public const string ModuleM01           = "license:m01";
    public const string ModuleDataMatching  = "license:data-matching";
    public const string ModuleForms         = "license:forms";
    public const string ModuleAsync         = "license:async";
}