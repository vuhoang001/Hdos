namespace Hdos.Common.Auth;

public sealed class KeycloakOptions
{
    public const string SectionName = "Keycloak";

    /// <summary>Internal URL dùng cho JWT validation (Docker: http://keycloak:8080/realms/hdos, local: http://localhost:8080/realms/hdos)</summary>
    public string Authority { get; set; } = string.Empty;

    /// <summary>
    /// URL browser có thể truy cập được, dùng cho Swagger OAuth2 token endpoint.
    /// Luôn là http://localhost:8080/realms/hdos vì Keycloak map port 8080 ra host.
    /// KHÔNG override trong docker-compose — giữ nguyên giá trị từ appsettings.json.
    /// </summary>
    public string PublicAuthority { get; set; } = string.Empty;

    /// <summary>Client ID configured in Keycloak as the resource server audience.</summary>
    public string Audience { get; set; } = "hdos-backend";

    /// <summary>Public client ID used for direct-access (password) grant — e.g. Swagger login.</summary>
    public string ClientId { get; set; } = "hdos-frontend";

    /// <summary>
    /// Optional: internal URL for OIDC discovery / JWKS (e.g. http://keycloak:8080/realms/hdos/.well-known/openid-configuration).
    /// Set when Authority uses a public HTTPS URL but JWKS must be fetched from the internal Docker hostname
    /// to avoid TLS cert issues inside the cluster.
    /// </summary>
    public string MetadataAddress { get; set; } = string.Empty;
}
