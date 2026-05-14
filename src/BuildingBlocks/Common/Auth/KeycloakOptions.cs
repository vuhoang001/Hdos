namespace Hdos.Common.Auth;

public sealed class KeycloakOptions
{
    public const string SectionName = "Keycloak";

    /// <summary>http://keycloak:8080/realms/hdos (Docker) or http://localhost:8180/realms/hdos (local)</summary>
    public string Authority { get; set; } = string.Empty;

    /// <summary>Client ID configured in Keycloak as the resource server audience.</summary>
    public string Audience { get; set; } = "hdos-backend";
}
