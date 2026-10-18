using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace ClubShell.Server;

/// <summary>Открытые WebSocket-соединения ПК. Сервер отправляет через них команды: lock, endSession, reboot, shutdown, message.</summary>
public sealed class PcHub
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private readonly ConcurrentDictionary<long, WebSocket> _sockets = new();

    public bool IsConnected(long pcId) => _sockets.TryGetValue(pcId, out var s) && s.State == WebSocketState.Open;

    /// <summary>Принимает соединение и держит его до закрытия. Старое соединение того же ПК закрывается.</summary>
    public async Task RunAsync(long pcId, WebSocket socket, Action onActivity, CancellationToken ct)
    {
        if (_sockets.TryGetValue(pcId, out var old))
        {
            try { await old.CloseAsync(WebSocketCloseStatus.PolicyViolation, "replaced", CancellationToken.None); } catch { /* уже закрыто */ }
        }
        _sockets[pcId] = socket;
        var buf = new byte[4096];
        try
        {
            while (socket.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                var r = await socket.ReceiveAsync(buf, ct);
                if (r.MessageType == WebSocketMessageType.Close) break;
                onActivity(); // любое сообщение от ПК (ping/status) считается признаком жизни
            }
        }
        catch (Exception) { /* обрыв */ }
        finally
        {
            _sockets.TryRemove(new KeyValuePair<long, WebSocket>(pcId, socket));
            if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
                try { await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None); } catch { /* ignore */ }
        }
    }

    public async Task<bool> SendAsync(long pcId, object message)
    {
        if (!_sockets.TryGetValue(pcId, out var s) || s.State != WebSocketState.Open) return false;
        try
        {
            var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(message, Json));
            await s.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);
            return true;
        }
        catch (Exception) { return false; }
    }
}
