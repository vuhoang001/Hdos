using System.Collections.Concurrent;
using System.Threading.Channels;

namespace Hdos.NotificationService.API.Sse;

/// <summary>
/// Quản lý các SSE connection đang mở.
/// Mỗi user có thể có nhiều connection (nhiều tab), mỗi connection được
/// định danh bằng một <see cref="Guid"/> riêng.
/// </summary>
public sealed class SseConnectionManager
{
    // userId (email) → { connectionId → channel }
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<Guid, Channel<string>>> _clients = new();

    public (Guid connectionId, Channel<string> channel) Add(string userId)
    {
        var connections = _clients.GetOrAdd(userId, _ => new ConcurrentDictionary<Guid, Channel<string>>());
        var id      = Guid.NewGuid();
        var channel = Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = true });
        connections[id] = channel;
        return (id, channel);
    }

    public void Remove(string userId, Guid connectionId)
    {
        if (!_clients.TryGetValue(userId, out var connections)) return;
        connections.TryRemove(connectionId, out _);
        if (connections.IsEmpty) _clients.TryRemove(userId, out _);
    }

    public async Task SendToUserAsync(string userId, string data, CancellationToken ct = default)
    {
        if (!_clients.TryGetValue(userId, out var connections)) return;
        foreach (var (_, channel) in connections)
            await channel.Writer.WriteAsync(data, ct);
    }

    public async Task BroadcastAsync(string data, CancellationToken ct = default)
    {
        foreach (var connections in _clients.Values)
            foreach (var (_, channel) in connections)
                await channel.Writer.WriteAsync(data, ct);
    }

    public int ConnectionCount => _clients.Values.Sum(c => c.Count);
}
