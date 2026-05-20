using System.Collections.Concurrent;
using System.Threading.Channels;

namespace Hdos.NotificationService.API.Sse;

/// <summary>
/// Quản lý các SSE connection đang mở.
/// Mỗi user có thể có nhiều connection (nhiều tab), mỗi connection được
/// định danh bằng một <see cref="Guid"/> riêng.
///
/// Hỗ trợ 3 kiểu gửi:
///   1 → 1   : <see cref="SendToUserAsync"/>   — gửi tới một user cụ thể
///   1 → n   : <see cref="SendToManyAsync"/>   — gửi tới danh sách user
///             <see cref="SendToGroupAsync"/>  — gửi tới một group (tập hợp user đã join)
///   1 → all : <see cref="BroadcastAsync"/>    — gửi tới toàn bộ user đang kết nối
/// </summary>
public sealed class SseConnectionManager
{
    // ── Connection store ──────────────────────────────────────────────────
    // userId (email) → { connectionId → channel }
    //
    // Dùng ConcurrentDictionary vì SseConnectionManager là Singleton và
    // được truy cập từ nhiều thread đồng thời (mỗi HTTP request là 1 thread).
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<Guid, Channel<string>>> _clients = new();

    // ── Group store ───────────────────────────────────────────────────────
    // groupName → { userId → _ }  (dùng ConcurrentDictionary làm HashSet)
    //
    // Ví dụ:
    //   "role:admin"   → { "alice@hdos.dev", "bob@hdos.dev" }
    //   "order:ord-1"  → { "alice@hdos.dev", "support@hdos.dev" }
    //   "tenant:t1"    → { "alice@hdos.dev" }
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, bool>> _groups = new();

    // ── Connection management ─────────────────────────────────────────────

    /// <summary>
    /// Đăng ký một SSE connection mới. Gọi khi browser bắt đầu kết nối.
    /// Trả về connectionId (định danh tab) và channel để SseController đọc event.
    /// </summary>
    public (Guid connectionId, Channel<string> channel) Add(string userId)
    {
        var connections = _clients.GetOrAdd(userId, _ => new ConcurrentDictionary<Guid, Channel<string>>());
        var id      = Guid.NewGuid();
        // UnboundedChannel: producer (pusher) không bao giờ bị block khi ghi.
        // SingleReader = true: chỉ có 1 consumer (HTTP response stream) → .NET dùng code path hiệu quả hơn.
        var channel = Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = true });
        connections[id] = channel;
        return (id, channel);
    }

    /// <summary>
    /// Hủy đăng ký connection. Gọi trong finally của SseController khi client ngắt kết nối.
    /// Nếu user đóng hết tất cả tab, entry userId cũng được xóa để tránh memory leak.
    /// </summary>
    public void Remove(string userId, Guid connectionId)
    {
        if (!_clients.TryGetValue(userId, out var connections)) return;
        connections.TryRemove(connectionId, out _);
        if (connections.IsEmpty) _clients.TryRemove(userId, out _);
    }

    // ── Group management ──────────────────────────────────────────────────

    /// <summary>
    /// Thêm user vào group. Group được tạo tự động nếu chưa tồn tại.
    /// Thường gọi ngay sau khi user connect SSE (trong SseController.Stream).
    /// </summary>
    public void JoinGroup(string userId, string groupName)
    {
        var members = _groups.GetOrAdd(groupName, _ => new ConcurrentDictionary<string, bool>());
        members[userId] = true;
    }

    /// <summary>
    /// Xóa user khỏi group. Nếu group trống sau khi xóa, group cũng bị xóa.
    /// Thường gọi trong finally của SseController khi client disconnect.
    /// </summary>
    public void LeaveGroup(string userId, string groupName)
    {
        if (!_groups.TryGetValue(groupName, out var members)) return;
        members.TryRemove(userId, out _);
        if (members.IsEmpty) _groups.TryRemove(groupName, out _);
    }

    // ── 1 → 1 ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Gửi data tới tất cả connection (tab) của một user cụ thể.
    /// Nếu user offline (không có connection), không làm gì cả — không throw, không queue.
    /// </summary>
    public async Task SendToUserAsync(string userId, string data, CancellationToken ct = default)
    {
        if (!_clients.TryGetValue(userId, out var connections)) return;
        foreach (var (_, channel) in connections)
            await channel.Writer.WriteAsync(data, ct);
    }

    // ── 1 → n ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Gửi data tới danh sách user cụ thể (ad-hoc list).
    /// User nào offline thì bỏ qua.
    /// </summary>
    public async Task SendToManyAsync(IEnumerable<string> userIds, string data, CancellationToken ct = default)
    {
        // Duyệt từng userId và reuse logic SendToUserAsync.
        // Không dùng Task.WhenAll vì channel.Writer.WriteAsync rất nhanh (in-memory),
        // sequential đủ dùng và tránh allocate thêm Task[].
        foreach (var userId in userIds)
            await SendToUserAsync(userId, data, ct);
    }

    /// <summary>
    /// Gửi data tới tất cả thành viên của một group.
    /// Group không tồn tại hoặc không có ai online → bỏ qua.
    /// </summary>
    public async Task SendToGroupAsync(string groupName, string data, CancellationToken ct = default)
    {
        if (!_groups.TryGetValue(groupName, out var members)) return;
        // Keys() snapshot tại thời điểm gọi — an toàn với concurrent add/remove.
        foreach (var userId in members.Keys)
            await SendToUserAsync(userId, data, ct);
    }

    // ── 1 → all ───────────────────────────────────────────────────────────

    /// <summary>
    /// Broadcast data tới tất cả user đang kết nối.
    /// Dùng cho system-wide announcement, maintenance notice, v.v.
    /// </summary>
    public async Task BroadcastAsync(string data, CancellationToken ct = default)
    {
        foreach (var connections in _clients.Values)
            foreach (var (_, channel) in connections)
                await channel.Writer.WriteAsync(data, ct);
    }

    // ── Metrics ───────────────────────────────────────────────────────────

    /// <summary>Tổng số SSE connection đang mở (tất cả user, tất cả tab).</summary>
    public int ConnectionCount => _clients.Values.Sum(c => c.Count);

    /// <summary>Tổng số user đang có ít nhất 1 connection.</summary>
    public int ConnectedUserCount => _clients.Count;
}
