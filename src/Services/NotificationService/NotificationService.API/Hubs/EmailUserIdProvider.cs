using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.SignalR;

namespace Hdos.NotificationService.API.Hubs;

/// <summary>
/// SignalR mặc định dùng <c>ClaimTypes.NameIdentifier</c> (= userId) làm
/// <c>UserIdentifier</c>. Trong NotificationService, <see cref="Hdos.NotificationService.Domain.Entities.Notification"/>
/// dùng <c>Recipient = email</c> nên ta override để target client theo email,
/// cho phép handler gọi <c>Clients.User(notification.Recipient)</c> trực tiếp.
/// </summary>
public sealed class EmailUserIdProvider : IUserIdProvider
{
    public string? GetUserId(HubConnectionContext connection)
    {
        var user = connection.User;
        return user.FindFirst(JwtRegisteredClaimNames.Email)?.Value
            ?? user.FindFirst(ClaimTypes.Email)?.Value;
    }
}
