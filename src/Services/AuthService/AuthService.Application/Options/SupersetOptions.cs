namespace Hdos.AuthService.Application.Options;

/// <summary>
/// Config Superset integration. Bind từ section "Superset" trong appsettings/env.
/// Set qua env vars: Superset__BaseUrl, Superset__AdminUsername, Superset__AdminPassword,
/// Superset__PublicUrl.
/// </summary>
public sealed class SupersetOptions
{
    public const string SectionName = "Superset";

    /// <summary>URL nội bộ Superset (http://superset:8088/ trong docker network).</summary>
    public string BaseUrl { get; set; } = "http://superset:8088/";

    /// <summary>Username admin Superset (để gọi /api/v1/security/login).</summary>
    public string AdminUsername { get; set; } = "admin";

    /// <summary>Password admin Superset. PROD: set qua env Superset__AdminPassword.</summary>
    public string AdminPassword { get; set; } = string.Empty;

    /// <summary>URL public của Superset (FE redirect đến). VD: https://hdos.example.com/superset/</summary>
    public string PublicUrl { get; set; } = "https://localhost:8443/superset/";
}
