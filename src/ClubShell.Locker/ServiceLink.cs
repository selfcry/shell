using System.IO.Pipes;
using ClubShell.Core;

namespace ClubShell.Locker;

/// <summary>
/// Клиент канала к службе. Сам переподключается, если служба перезапустилась.
/// События приходят из фонового потока — подписчик сам переходит в поток интерфейса.
/// </summary>
internal sealed class ServiceLink : IDisposable
{
    private readonly CancellationTokenSource _cts = new();
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private StreamWriter? _writer;

    public event Action<PipeMessage>? MessageReceived;

    /// <summary>Время последнего сообщения от службы (UTC). Окно использует его как «служба жива».</summary>
    public DateTime LastMessageUtc { get; private set; } = DateTime.UtcNow;

    public void Start() => _ = Task.Run(() => RunAsync(_cts.Token));

    private async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await using var pipe = new NamedPipeClientStream(".", PipeNames.Control, PipeDirection.InOut, PipeOptions.Asynchronous);
                await pipe.ConnectAsync(2000, ct);

                using var reader = new StreamReader(pipe);
                _writer = new StreamWriter(pipe) { AutoFlush = true };

                while (await reader.ReadLineAsync(ct) is { } line)
                {
                    var msg = PipeMessage.FromLine(line);
                    if (msg is null) continue;
                    LastMessageUtc = DateTime.UtcNow;
                    MessageReceived?.Invoke(msg);
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) when (ex is IOException or TimeoutException or UnauthorizedAccessException)
            {
                // службы нет или она перезапускается — пробуем снова
            }
            finally { _writer = null; }

            try { await Task.Delay(1000, ct); } catch (OperationCanceledException) { break; }
        }
    }

    public async Task SendAsync(PipeMessage message)
    {
        var writer = _writer;
        if (writer is null) return;
        await _writeGate.WaitAsync();
        try { await writer.WriteLineAsync(message.ToLine()); }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException) { /* канал оборвался, RunAsync переподключится */ }
        finally { _writeGate.Release(); }
    }

    public void Dispose() => _cts.Cancel();
}
