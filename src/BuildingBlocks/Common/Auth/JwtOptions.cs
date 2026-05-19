namespace Hdos.Common.Auth;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    /// <summary>HMAC secret >= 32 ký tự. Bắt buộc set qua env Jwt__Secret.</summary>
    public string Secret { get; set; } = string.Empty;

    /// <summary>Issuer claim. Mặc định "hdos-auth".</summary>
    public string Issuer { get; set; } = "hdos-auth";

    /// <summary>Audience claim. Mặc định "hdos-api".</summary>
    public string Audience { get; set; } = "hdos-api";

    /// <summary>Thời gian sống access token (phút). Mặc định 8h = 480 phút.</summary>
    public int ExpiresMinutes { get; set; } = 480;
}
