namespace Hdos.Common.Auth;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Secret { get; set; } = string.Empty;
    public string Issuer { get; set; } = "Hdos.Auth";
    public string Audience { get; set; } = "Hdos.Services";
    public int ExpiresMinutes { get; set; } = 60;
}
