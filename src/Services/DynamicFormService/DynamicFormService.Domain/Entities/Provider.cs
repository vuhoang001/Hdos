using Hdos.DynamicFormService.Domain.Enums;
using Hdos.SharedKernel;

namespace Hdos.DynamicFormService.Domain.Entities;

// Provider = một data service bên ngoài đã đăng ký vào DynamicFormService.
// Vai trò: thay vì admin gõ tay URL của DataMatching/Lakehouse vào mỗi DataSource,
// chỉ cần đăng ký 1 lần ở đây, sau đó chọn Operation qua dropdown.
//
// Đổi infra (URL service thay đổi) → sửa BaseUrl ở Provider, mọi screen tự cập nhật.
public sealed class Provider : AggregateRoot<Guid>
{
    public string         Code        { get; private set; } = null!;   // slug, unique. VD: "datamatch", "lakehouse"
    public string         DisplayName { get; private set; } = null!;   // tên hiển thị admin nhìn
    public string         BaseUrl     { get; private set; } = null!;   // path/URL, không trailing "/"
    public ProviderStatus Status      { get; private set; }

    private Provider()
    {
    }

    public static Provider Create(string code, string displayName, string baseUrl)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new ArgumentException("Provider code is required.", nameof(code));
        if (string.IsNullOrWhiteSpace(displayName))
            throw new ArgumentException("Provider displayName is required.", nameof(displayName));
        if (string.IsNullOrWhiteSpace(baseUrl))
            throw new ArgumentException("Provider baseUrl is required.", nameof(baseUrl));

        return new Provider
        {
            Id          = Guid.NewGuid(),
            Code        = code.Trim().ToLowerInvariant(),
            DisplayName = displayName.Trim(),
            BaseUrl     = NormalizeBaseUrl(baseUrl),
            Status      = ProviderStatus.Active
        };
    }

    public void Update(string displayName, string baseUrl)
    {
        if (string.IsNullOrWhiteSpace(displayName))
            throw new ArgumentException("Provider displayName is required.", nameof(displayName));
        if (string.IsNullOrWhiteSpace(baseUrl))
            throw new ArgumentException("Provider baseUrl is required.", nameof(baseUrl));

        DisplayName  = displayName.Trim();
        BaseUrl      = NormalizeBaseUrl(baseUrl);
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public void Activate()
    {
        Status       = ProviderStatus.Active;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public void Deactivate()
    {
        Status       = ProviderStatus.Inactive;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    private static string NormalizeBaseUrl(string url) => url.Trim().TrimEnd('/');
}
