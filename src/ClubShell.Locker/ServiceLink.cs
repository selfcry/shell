using System.Collections.Concurrent;
using System.ComponentModel;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using ClubShell.Core;

namespace ClubShell.Locker;

/// <summary>
/// Клиент канала к службе. Сам переподключается, если служба перезапустилась.
/// События приходят из фонового потока — подписчик сам переходит в поток интерфейса.
/// На боевом ПК окно разговаривает только с настоящей службой: сервер канала должен работать в сессии 0
/// (туда попадают только службы) из ClubShell.Service.exe рядом с папкой окна — иначе это подделка гостя.
/// </summary>
internal sealed class ServiceLink : IDisposable
{
    private readonly CancellationTokenSource _cts = new();
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly ConcurrentDictionary<int, TaskCompletionSource<PipeMessage>> _pending = new();
    private StreamWriter? _writer;
    private int _nextId;

    public event Action<PipeMessage>? MessageReceived;

    /// <summary>Канал подключился (окно сразу сообщает службе своё состояние).</summary>
    public event Action? Connected;

    /// <summary>Время последнего сообщения от службы (UTC). Окно использует его как «служба жива».</summary>
    public DateTime LastMessageUtc { get; private set; } = DateTime.UtcNow;

    /// <summary>Последняя причина, по которой сервер канала отвергнут (для журнала).</summary>
    public string? RejectReason { get; private set; }

    public void Start() => _ = Task.Run(() => RunAsync(_cts.Token));

    private async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await using var pipe = new NamedPipeClientStream(".", PipeNames.Control, PipeDirection.InOut, PipeOptions.Asynchronous);
                await pipe.ConnectAsync(2000, ct);

                if (DevFlags.IsProductionPc && !IsGenuineService(pipe, out var why))
                {
                    if (why != RejectReason) CrashLog.Write(new InvalidOperationException("Pipe server rejected: " + why));
                    RejectReason = why;
                    throw new IOException(why);
                }

                var reader = new PipeLineReader(pipe, PipeNames.MaxServerLine);
                _writer = new StreamWriter(pipe) { AutoFlush = true };
                Connected?.Invoke();

                while (await reader.ReadLineAsync(ct) is { } line)
                {
                    var msg = PipeMessage.FromLine(line);
                    if (msg is null) continue;
                    LastMessageUtc = DateTime.UtcNow;
                    if (msg.Type == "response")
                    {
                        if (_pending.TryRemove(msg.Id, out var tcs)) tcs.TrySetResult(msg);
                        continue;
                    }
                    MessageReceived?.Invoke(msg);
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) when (ex is IOException or TimeoutException or UnauthorizedAccessException or InvalidDataException)
            {
                // службы нет или она перезапускается — пробуем снова
            }
            finally
            {
                _writer = null;
                foreach (var id in _pending.Keys)
                    if (_pending.TryRemove(id, out var tcs)) tcs.TrySetException(new HttpRequestException("Служба не отвечает"));
            }

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

    /// <summary>
    /// Запрос службе (вход, старт сеанса, каталог…) и ожидание ответа с тем же id.
    /// Нет службы или ответа — HttpRequestException, как при обрыве связи с сервером.
    /// </summary>
    public async Task<PipeMessage> RequestAsync(PipeMessage request, TimeSpan timeout)
    {
        if (_writer is null) throw new HttpRequestException("Служба не отвечает");
        request.Type = "request";
        request.Id = Interlocked.Increment(ref _nextId);
        var tcs = new TaskCompletionSource<PipeMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[request.Id] = tcs;
        try
        {
            await SendAsync(request);
            return await tcs.Task.WaitAsync(timeout);
        }
        catch (TimeoutException) { throw new HttpRequestException("Служба не ответила вовремя"); }
        finally { _pending.TryRemove(request.Id, out _); }
    }

    public void Dispose() => _cts.Cancel();

    // ---------- проверка сервера канала ----------

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetNamedPipeServerProcessId(Microsoft.Win32.SafeHandles.SafePipeHandle pipe, out uint processId);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ProcessIdToSessionId(uint processId, out uint sessionId);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint OpenProcess(uint access, bool inherit, uint processId);
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool QueryFullProcessImageName(nint process, uint flags, char[] name, ref int size);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(nint h);
    private const uint ProcessQueryLimitedInformation = 0x1000;

    private static readonly string ExpectedServicePath =
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "ClubShell.Service.exe"));

    /// <summary>
    /// Сервер канала — служба в сессии 0 (гость не может запустить там процесс) из ожидаемого файла.
    /// Учётную запись (LocalSystem) гость проверить не может: токен процесса SYSTEM ему не открыть, поэтому опора — сессия 0 и путь.
    /// </summary>
    private static bool IsGenuineService(NamedPipeClientStream pipe, out string why)
    {
        if (!GetNamedPipeServerProcessId(pipe.SafePipeHandle, out var pid)) { why = "GetNamedPipeServerProcessId: " + new Win32Exception().Message; return false; }
        if (!ProcessIdToSessionId(pid, out var session)) { why = $"ProcessIdToSessionId({pid}): " + new Win32Exception().Message; return false; }
        if (session != 0) { why = $"pipe server pid {pid} runs in session {session}, not in the service session"; return false; }

        var h = OpenProcess(ProcessQueryLimitedInformation, false, pid);
        if (h == 0) { why = ""; return true; } // путь службы не прочитать (зависит от версии Windows) — сессии 0 достаточно
        try
        {
            var buf = new char[1024];
            var size = buf.Length;
            if (!QueryFullProcessImageName(h, 0, buf, ref size)) { why = ""; return true; }
            var path = new string(buf, 0, size);
            if (!string.Equals(Path.GetFullPath(path), ExpectedServicePath, StringComparison.OrdinalIgnoreCase))
            {
                why = $"pipe server pid {pid} is {path}, expected {ExpectedServicePath}";
                return false;
            }
            why = "";
            return true;
        }
        finally { CloseHandle(h); }
    }
}
