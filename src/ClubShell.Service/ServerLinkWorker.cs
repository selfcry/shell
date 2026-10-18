using System.Diagnostics;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using ClubShell.Core;

namespace ClubShell.Service;

/// <summary>
/// Держит WebSocket к серверу клуба и принимает команды. reboot/shutdown выполняет сама служба (она SYSTEM),
/// остальные (endSession, lock, message, sync) уходят окну блокировки через канал.
/// Если сервер не настроен (ServerUrl пуст), ничего не делает.
/// </summary>
public sealed class ServerLinkWorker : BackgroundService
{
    private readonly ShellConfig _config;
    private readonly CommandBus _bus;
    private readonly ILogger<ServerLinkWorker> _log;

    public ServerLinkWorker(ShellConfig config, CommandBus bus, ILogger<ServerLinkWorker> log) => (_config, _bus, _log) = (config, bus, log);

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        if (string.IsNullOrWhiteSpace(_config.ServerUrl) || string.IsNullOrWhiteSpace(_config.PcToken)) return;
        var uri = new Uri(_config.ServerUrl.TrimEnd('/').Replace("https://", "wss://").Replace("http://", "ws://") + "/ws/pc");
        var delay = 1;

        while (!stop.IsCancellationRequested)
        {
            try
            {
                using var ws = new ClientWebSocket();
                ws.Options.SetRequestHeader("X-Pc-Token", _config.PcToken);
                ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
                await ws.ConnectAsync(uri, stop);
                _log.LogInformation("Связь с сервером (WebSocket) установлена");
                delay = 1;
                await ReceiveLoopAsync(ws, stop);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { _log.LogWarning("WebSocket: {Message}", ex.Message); }

            try { await Task.Delay(TimeSpan.FromSeconds(delay), stop); } catch (OperationCanceledException) { break; }
            delay = Math.Min(delay * 2, 30); // переподключение с нарастающей паузой, чтобы не засыпать сервер
        }
    }

    private async Task ReceiveLoopAsync(ClientWebSocket ws, CancellationToken stop)
    {
        var buf = new byte[8192];
        while (ws.State == WebSocketState.Open)
        {
            var sb = new StringBuilder();
            WebSocketReceiveResult r;
            do
            {
                r = await ws.ReceiveAsync(buf, stop);
                if (r.MessageType == WebSocketMessageType.Close) return;
                sb.Append(Encoding.UTF8.GetString(buf, 0, r.Count));
            } while (!r.EndOfMessage && sb.Length < 64 * 1024);

            try { Handle(sb.ToString()); }
            catch (Exception ex) { _log.LogWarning(ex, "Не удалось разобрать команду"); }
        }
    }

    private void Handle(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.GetProperty("type").GetString() != "command") return;
        var name = root.GetProperty("name").GetString() ?? "";
        var text = root.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null;
        var sec = root.TryGetProperty("remainingSec", out var r) && r.TryGetInt32(out var n) ? n : 0;
        _log.LogInformation("Команда сервера: {Name}", name);

        switch (name)
        {
            case "reboot": Shutdown("/r"); break;
            case "shutdown": Shutdown("/s"); break;
            case "endSession" or "lock" or "message" or "sync":
                _bus.Publish(new PipeMessage { Type = "command", Name = name, Text = text, RemainingSec = sec });
                break;
        }
    }

    private void Shutdown(string mode)
    {
        Process.Start(new ProcessStartInfo("shutdown", $"{mode} /t 5 /f") { CreateNoWindow = true, UseShellExecute = false });
    }
}
