using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using ClubShell.Core;

namespace ClubShell.Locker;

/// <summary>
/// Команды сервера прямо в окно, без службы. Нужно только для тестового запуска (CLUBSHELL_TEST=1 с CLUBSHELL_SERVER_URL
/// и CLUBSHELL_PC_TOKEN): в рабочей установке WebSocket держит служба и передаёт команды окну через канал.
/// reboot и shutdown здесь не выполняются, чтобы не перезагрузить тестовую машину случайно.
/// </summary>
internal sealed class ServerCommandLink : IDisposable
{
    private readonly Uri _uri;
    private readonly string _token;
    private readonly Action<PipeMessage> _onCommand;
    private readonly CancellationTokenSource _cts = new();

    public ServerCommandLink(string serverUrl, string token, Action<PipeMessage> onCommand)
    {
        _uri = new Uri(serverUrl.TrimEnd('/').Replace("https://", "wss://").Replace("http://", "ws://") + "/ws/pc");
        (_token, _onCommand) = (token, onCommand);
    }

    public void Start() => _ = Task.Run(() => RunAsync(_cts.Token));

    private async Task RunAsync(CancellationToken ct)
    {
        var delay = 1;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var ws = new ClientWebSocket();
                ws.Options.SetRequestHeader("X-Pc-Token", _token);
                await ws.ConnectAsync(_uri, ct);
                delay = 1;
                var buf = new byte[8192];
                while (ws.State == WebSocketState.Open)
                {
                    var sb = new StringBuilder();
                    WebSocketReceiveResult r;
                    do
                    {
                        r = await ws.ReceiveAsync(buf, ct);
                        if (r.MessageType == WebSocketMessageType.Close) goto reconnect;
                        sb.Append(Encoding.UTF8.GetString(buf, 0, r.Count));
                    } while (!r.EndOfMessage && sb.Length < 64 * 1024);
                    Handle(sb.ToString());
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception) { /* сервера нет или связь оборвалась: пробуем снова */ }
            reconnect:
            try { await Task.Delay(TimeSpan.FromSeconds(delay), ct); } catch (OperationCanceledException) { break; }
            delay = Math.Min(delay * 2, 15);
        }
    }

    private void Handle(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.GetProperty("type").GetString() != "command") return;
            var name = root.GetProperty("name").GetString() ?? "";
            if (name is "reboot" or "shutdown") return;
            _onCommand(new PipeMessage
            {
                Type = "command",
                Name = name,
                Text = root.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null,
                RemainingSec = root.TryGetProperty("remainingSec", out var s) && s.TryGetInt32(out var n) ? n : 0,
            });
        }
        catch (Exception) { /* мусор от сервера игнорируем */ }
    }

    public void Dispose() => _cts.Cancel();
}
