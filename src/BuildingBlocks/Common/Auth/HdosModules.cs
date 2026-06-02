namespace Hdos.Common.Auth;

/// <summary>
/// Slug định danh từng module trong hệ thống HDOS.
/// Các slug này được nhúng vào JWT claim <c>lic_mod</c> bởi <see cref="JwtTokenIssuer"/>
/// và được dùng làm giá trị so khớp trong <see cref="HdosLicensePolicies"/>.
/// </summary>
/// <remarks>
/// Khi thêm module mới:
/// <list type="number">
///   <item>Thêm constant vào class này và vào <see cref="All"/>.</item>
///   <item>Thêm policy constant tương ứng vào <see cref="HdosLicensePolicies"/>.</item>
///   <item>Đăng ký policy trong <c>JwtAuthExtensions.AddHdosAuthorization()</c>.</item>
/// </list>
/// Không cần migration — module slug chỉ là string trong cột <c>ModulesCsv</c>.
/// </remarks>
public static class HdosModules
{
    /// <summary>Module quản lý đơn hàng — <c>OrderService</c>.</summary>
    public const string Orders = "orders";

    /// <summary>Module thông báo — <c>NotificationService</c>.</summary>
    public const string Notifications = "notifications";

    /// <summary>Module nghiệp vụ bệnh viện M01 — <c>M01Service</c>.</summary>
    public const string M01 = "m01";

    /// <summary>Module khớp dữ liệu — <c>DataMatchingService</c>.</summary>
    public const string DataMatching = "data-matching";

    /// <summary>Module form động — <c>DynamicFormService</c>.</summary>
    public const string Forms = "forms";

    /// <summary>Module gửi request bất đồng bộ — <c>AsyncGateway</c>.</summary>
    public const string Async = "async";

    /// <summary>Tất cả module hiện có. Dùng khi gán license <c>enterprise</c>.</summary>
    public static readonly IReadOnlyList<string> All =
    [
        Orders, Notifications, M01, DataMatching, Forms, Async,
    ];
}

/// <summary>
/// Tên policy ASP.NET Core Authorization dùng để bảo vệ endpoint theo module license.
/// Mỗi policy kiểm tra JWT claim <c>lic_mod</c> có chứa slug tương ứng không.
/// </summary>
/// <remarks>
/// Cách dùng tại controller:
/// <code>
/// [Authorize(Policy = HdosLicensePolicies.ModuleM01)]
/// public IActionResult GetPatients() { ... }
/// </code>
/// Các policy được đăng ký trong <c>JwtAuthExtensions.AddHdosAuthorization()</c>.
/// </remarks>
public static class HdosLicensePolicies
{
    /// <summary>Yêu cầu module <c>orders</c> — xem <see cref="HdosModules.Orders"/>.</summary>
    public const string ModuleOrders = "license:orders";

    /// <summary>Yêu cầu module <c>notifications</c> — xem <see cref="HdosModules.Notifications"/>.</summary>
    public const string ModuleNotifications = "license:notifications";

    /// <summary>Yêu cầu module <c>m01</c> — xem <see cref="HdosModules.M01"/>.</summary>
    public const string ModuleM01 = "license:m01";

    /// <summary>Yêu cầu module <c>data-matching</c> — xem <see cref="HdosModules.DataMatching"/>.</summary>
    public const string ModuleDataMatching = "license:data-matching";

    /// <summary>Yêu cầu module <c>forms</c> — xem <see cref="HdosModules.Forms"/>.</summary>
    public const string ModuleForms = "license:forms";

    /// <summary>Yêu cầu module <c>async</c> — xem <see cref="HdosModules.Async"/>.</summary>
    public const string ModuleAsync = "license:async";
}
