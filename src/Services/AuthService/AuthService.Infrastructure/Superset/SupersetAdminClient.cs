using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Hdos.AuthService.Application.Features.SupersetGuestToken;
using Hdos.AuthService.Application.Options;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Hdos.AuthService.Infrastructure.Superset;

/// <summary>
/// Gọi Superset Admin API để issue guest token cho FE embedded dashboard.
///
/// Flow:
///   1. POST /api/v1/security/login với admin username/password → nhận access_token
///   2. Cache access_token ~30 phút (Superset TTL mặc định 1h, refresh trước hết hạn)
///   3. POST /api/v1/security/guest_token/ với Bearer access_token → trả token cho FE
///
/// Lỗi network/HTTP → ném <see cref="SupersetApiException"/>, Handler convert sang Result.Failure.
/// </summary>
internal sealed class SupersetAdminClient(
    HttpClient http,
    IOptions<SupersetOptions> options,
    IMemoryCache cache,
    ILogger<SupersetAdminClient> logger)
    : ISupersetAdminClient
{
    private const string CacheKey = "superset:admin:access_token";
    private static readonly TimeSpan AdminTokenCacheTtl = TimeSpan.FromMinutes(30);

    public async Task<string> IssueGuestTokenAsync(
        Guid dashboardId,
        string username,
        string firstName,
        string lastName,
        CancellationToken ct)
    {
        var adminToken = await GetAdminTokenAsync(ct);

        var body = new GuestTokenRequest(
            User: new GuestUser(username, firstName, lastName),
            Resources: new[] { new GuestResource("dashboard", dashboardId.ToString()) },
            Rls: Array.Empty<object>());

        using var req = new HttpRequestMessage(HttpMethod.Post, "api/v1/security/guest_token/")
        {
            Content = JsonContent.Create(body),
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

        HttpResponseMessage resp;
        try { resp = await http.SendAsync(req, ct); }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "Superset guest_token network error");
            throw new SupersetApiException("Superset unreachable", ex);
        }

        if (!resp.IsSuccessStatusCode)
        {
            var detail = await SafeReadStringAsync(resp, ct);
            logger.LogWarning("Superset guest_token failed: {Status} {Detail}", resp.StatusCode, detail);

            // 401/403 → admin token có thể expired, invalidate cache để lần sau lấy mới
            if (resp.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden)
                cache.Remove(CacheKey);

            throw new SupersetApiException($"Superset returned {(int)resp.StatusCode}: {detail}");
        }

        var payload = await resp.Content.ReadFromJsonAsync<GuestTokenResponse>(ct)
                      ?? throw new SupersetApiException("Empty guest_token response");
        return payload.Token;
    }

    private async Task<string> GetAdminTokenAsync(CancellationToken ct)
    {
        if (cache.TryGetValue(CacheKey, out string? cached) && !string.IsNullOrEmpty(cached))
            return cached;

        var opts = options.Value;
        if (string.IsNullOrEmpty(opts.AdminPassword))
            throw new SupersetApiException("Superset__AdminPassword chưa được set");

        var body = new LoginRequest(opts.AdminUsername, opts.AdminPassword, "db", false);
        HttpResponseMessage resp;
        try { resp = await http.PostAsJsonAsync("api/v1/security/login", body, ct); }
        catch (HttpRequestException ex)
        {
            throw new SupersetApiException("Superset login unreachable", ex);
        }

        if (!resp.IsSuccessStatusCode)
        {
            var detail = await SafeReadStringAsync(resp, ct);
            throw new SupersetApiException($"Superset login failed {(int)resp.StatusCode}: {detail}");
        }

        var payload = await resp.Content.ReadFromJsonAsync<LoginResponse>(ct)
                      ?? throw new SupersetApiException("Empty login response");

        cache.Set(CacheKey, payload.AccessToken, AdminTokenCacheTtl);
        return payload.AccessToken;
    }

    private static async Task<string> SafeReadStringAsync(HttpResponseMessage resp, CancellationToken ct)
    {
        try { return await resp.Content.ReadAsStringAsync(ct); }
        catch { return "<no body>"; }
    }

    // ── DTO Superset Admin API ──────────────────────────────────────────
    private sealed record LoginRequest(
        [property: JsonPropertyName("username")] string Username,
        [property: JsonPropertyName("password")] string Password,
        [property: JsonPropertyName("provider")] string Provider,
        [property: JsonPropertyName("refresh")] bool Refresh);

    private sealed record LoginResponse(
        [property: JsonPropertyName("access_token")] string AccessToken);

    private sealed record GuestTokenRequest(
        [property: JsonPropertyName("user")] GuestUser User,
        [property: JsonPropertyName("resources")] IReadOnlyCollection<GuestResource> Resources,
        [property: JsonPropertyName("rls")] IReadOnlyCollection<object> Rls);

    private sealed record GuestUser(
        [property: JsonPropertyName("username")] string Username,
        [property: JsonPropertyName("first_name")] string FirstName,
        [property: JsonPropertyName("last_name")] string LastName);

    private sealed record GuestResource(
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("id")] string Id);

    private sealed record GuestTokenResponse(
        [property: JsonPropertyName("token")] string Token);
}
