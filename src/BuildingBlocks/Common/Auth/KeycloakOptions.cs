namespace Hdos.Common.Auth;

public sealed class KeycloakOptions
{
    public const string SectionName = "Keycloak";

    /// <summary>http://keycloak:8080/realms/hdos (Docker) or http://localhost:8180/realms/hdos (local)</summary>
    public string Authority { get; set; } = string.Empty;

    /// <summary>Client ID configured in Keycloak as the resource server audience.</summary>
    public string Audience { get; set; } = "hdos-backend";

    /// <summary>
    /// Optional: internal URL for OIDC discovery / JWKS (e.g. http://keycloak:8080/realms/hdos/.well-known/openid-configuration).
    /// Set when Authority uses a public HTTPS URL but JWKS must be fetched from the internal Docker hostname
    /// to avoid TLS cert issues inside the cluster.
    /// </summary>
    public string MetadataAddress { get; set; } = string.Empty;
}
