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
    private readonly ConcurrentDictionary<WebSocket, SemaphoreSlim> _sendLocks = new(); // WebSocket не допускает двух одновременных SendAsync

    public bool IsConnected(long pcId) => _sockets.TryGetValue(pcId, out var s) && s.State == WebSocketState.Open;

    /// <summary>Принимает соединение и держит его до закрытия. Старое соединение того же ПК закрывается.</summary>
    public async Task RunAsync(long pcId, WebSocket socket, Action onActivity, CancellationToken ct)
    {
        // Сначала регистрируем новое соединение, потом обрываем старое: вежливое закрытие полуоткрытого сокета
        // может ждать до таймаута TCP, а команды всё это время должны идти уже в новое.
        var old = _sockets.TryGetValue(pcId, out var o) ? o : null;
        _sockets[pcId] = socket;
        _sendLocks[socket] = new SemaphoreSlim(1, 1);
        old?.Abort();
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
            if (_sendLocks.TryRemove(socket, out var l)) l.Dispose();
            if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
                try { await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None); } catch { /* ignore */ }
        }
    }

    /// <summary>Разорвать соединение ПК (например, после смены его токена).</summary>
    public async Task DisconnectAsync(long pcId)
    {
        if (_sockets.TryRemove(pcId, out var s)) s.Abort();
        await Task.CompletedTask;
    }

    public async Task<bool> SendAsync(long pcId, object message)
    {
        if (!_sockets.TryGetValue(pcId, out var s) || s.State != WebSocketState.Open) return false;
        if (!_sendLocks.TryGetValue(s, out var gate)) return false;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5)); // медленный ПК не задерживает остальных
        try
        {
            await gate.WaitAsync(cts.Token);
            try
            {
                var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(message, Json));
                await s.SendAsync(bytes, WebSocketMessageType.Text, true, cts.Token);
                return true;
            }
            finally { gate.Release(); }
        }
        catch (Exception) { return false; }
    }
}
