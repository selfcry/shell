using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;
using ClubShell.Core;

namespace ClubShell.Service;

/// <summary>
/// Автообновление клиента (служба + оболочка). Раскладка: C:\Program Files\ClubShell\versions\&lt;версия&gt;\,
/// служба запускается из папки своей версии. Порядок:
///  1) раз в 15 минут спросить сервер клуба, какая версия нужна этому ПК (сервер решает: автообновление включено,
///     тестовые ПК — первыми);
///  2) только когда на ПК нет сеанса — скачать пакет, сверить SHA-256 и подпись ECDSA (UpdateVerifier);
///  3) распаковать в новую папку версии, переключить службу на неё (sc config binPath) и перезапуститься;
///  4) новая версия три минуты работает без падений — обновление подтверждено; если она падает при запуске
///     больше трёх раз — служба возвращается на прошлую версию.
/// </summary>
public sealed class UpdateWorker : BackgroundService
{
    private const string ServiceName = "ClubShellService";
    private static readonly TimeSpan CheckEvery = TimeSpan.FromMinutes(15);
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(10) };
    private const long MaxPackage = 400L * 1024 * 1024;

    private readonly IClubApi _api;
    private readonly ShellConfig _config;
    private readonly LinkState _link;
    private readonly ILogger<UpdateWorker> _log;

    public UpdateWorker(IClubApi api, ShellConfig config, LinkState link, ILogger<UpdateWorker> log) => (_api, _config, _link, _log) = (api, config, link, log);

    private sealed record State(string Version, string FromExe, int Attempts);

    private static string StateFile => Path.Combine(ShellConfig.Dir, "update.json");
    private static string DownloadDir => Path.Combine(ShellConfig.Dir, "updates");
    private static string CurrentExe => Path.Combine(AppContext.BaseDirectory, "ClubShell.Service.exe");

    /// <summary>Корень установки: родитель папки versions, а для старой «плоской» установки — сама папка программы.</summary>
    private static string InstallRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar));
        return dir.Parent is { Name: "versions" } versions && versions.Parent is { } root ? root.FullName : dir.FullName;
    }

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        if (!OperatingSystem.IsWindows()) return;
        var current = UpdateVerifier.CurrentVersion;
        _log.LogInformation("Client version {Version}, install root {Root}", current, InstallRoot());
        await ConfirmOrRollbackAsync(current, stop);

        try { await Task.Delay(TimeSpan.FromMinutes(2), stop); } catch (OperationCanceledException) { return; }
        using var timer = new PeriodicTimer(CheckEvery);
        do
        {
            try { await CheckAsync(current, stop); }
            catch (Exception ex) when (!stop.IsCancellationRequested) { _log.LogWarning("Update check failed: {Message}", ex.Message); }
        }
        while (await timer.WaitForNextTickAsync(stop));
    }

    /// <summary>Мы только что обновились: считаем запуски, через 3 минуты подтверждаем, при падениях откатываемся.</summary>
    private async Task ConfirmOrRollbackAsync(string current, CancellationToken stop)
    {
        var state = ReadState();
        if (state is null || state.Version != current) return;
        state = state with { Attempts = state.Attempts + 1 };
        WriteState(state);
        if (state.Attempts > 3 && File.Exists(state.FromExe))
        {
            _log.LogError("Version {Version} keeps failing, rolling back to {From}", current, state.FromExe);
            File.Delete(StateFile);
            if (SetBinPath(state.FromExe)) Restart();
            return;
        }
        try { await Task.Delay(TimeSpan.FromMinutes(3), stop); } catch (OperationCanceledException) { return; }
        File.Delete(StateFile);
        _log.LogWarning("Update to {Version} confirmed", current);
        CleanupOldVersions(current, Path.GetDirectoryName(state.FromExe));
    }

    private async Task CheckAsync(string current, CancellationToken stop)
    {
        if (string.IsNullOrWhiteSpace(_config.ServerUrl) || string.IsNullOrWhiteSpace(_config.PcToken)) return;
        if (_link.ServerSessionActive == true || _link.LockerSessionActive) return; // гость играет — не мешаем
        if (File.Exists(ShellConfig.MaintenanceFlag)) return;                       // админ обслуживает ПК

        var m = await _api.GetUpdateAsync(current);
        if (m is null || !UpdateVerifier.IsNewer(m.Version, current)) return;
        if (!UpdateVerifier.SignatureValid(m)) { _log.LogError("Update {Version}: bad signature, ignored", m.Version); return; }
        if (!Uri.TryCreate(m.Url, UriKind.Absolute, out var url) || url.Scheme != "https") { _log.LogError("Update {Version}: url must be https", m.Version); return; }
        // пакет раздаёт сервер клуба (репозиторий приватный); токен ПК уходит только на свой сервер, не на чужие адреса
        var serverHost = Uri.TryCreate(_config.ServerUrl, UriKind.Absolute, out var su) ? su.Host : null;
        var token = string.Equals(url.Host, serverHost, StringComparison.OrdinalIgnoreCase) ? _config.PcToken : null;

        _log.LogWarning("Downloading update {Version}", m.Version);
        Directory.CreateDirectory(DownloadDir);
        var zip = Path.Combine(DownloadDir, $"client-{m.Version}.zip");
        await DownloadAsync(url, token, zip, stop);
        var hash = UpdateVerifier.Sha256Of(zip);
        if (!hash.Equals(m.Sha256, StringComparison.OrdinalIgnoreCase)) { File.Delete(zip); _log.LogError("Update {Version}: sha256 mismatch", m.Version); return; }

        // за время скачивания гость мог начать сеанс
        if (_link.ServerSessionActive == true || _link.LockerSessionActive) { _log.LogInformation("Session started, update postponed"); return; }

        var versions = Path.Combine(InstallRoot(), "versions");
        var target = Path.Combine(versions, m.Version);
        var staging = target + ".new";
        if (Directory.Exists(staging)) Directory.Delete(staging, true);
        Directory.CreateDirectory(staging);
        ZipFile.ExtractToDirectory(zip, staging); // .NET отклоняет записи с путями за пределы папки
        if (!File.Exists(Path.Combine(staging, "ClubShell.Service.exe")) || !File.Exists(Path.Combine(staging, "locker", "ClubShell.Locker.exe")))
        {
            Directory.Delete(staging, true);
            _log.LogError("Update {Version}: package is incomplete", m.Version);
            return;
        }
        if (Directory.Exists(target)) Directory.Delete(target, true);
        Directory.Move(staging, target);
        File.Delete(zip);

        WriteState(new State(m.Version, CurrentExe, 0));
        if (!SetBinPath(Path.Combine(target, "ClubShell.Service.exe"))) { File.Delete(StateFile); return; }
        _log.LogWarning("Switching to {Version}", m.Version);
        foreach (var p in Process.GetProcessesByName("ClubShell.Locker")) try { p.Kill(); } catch (Exception) { /* уже закрыто */ }
        Restart();
    }

    private static async Task DownloadAsync(Uri url, string? pcToken, string file, CancellationToken stop)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        if (pcToken is not null) req.Headers.Add("X-Pc-Token", pcToken);
        using var resp = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, stop);
        resp.EnsureSuccessStatusCode();
        if (resp.Content.Headers.ContentLength > MaxPackage) throw new IOException("package too large");
        await using var src = await resp.Content.ReadAsStreamAsync(stop);
        await using var dst = File.Create(file);
        var buf = new byte[81920];
        long total = 0;
        int n;
        while ((n = await src.ReadAsync(buf, stop)) > 0)
        {
            total += n;
            if (total > MaxPackage) throw new IOException("package too large");
            await dst.WriteAsync(buf.AsMemory(0, n), stop);
        }
    }

    private bool SetBinPath(string exe)
    {
        var psi = new ProcessStartInfo("sc.exe", $"config {ServiceName} binPath= \"\\\"{exe}\\\"\"") { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true };
        using var p = Process.Start(psi)!;
        p.WaitForExit(15000);
        if (p.ExitCode == 0) return true;
        _log.LogError("sc config failed ({Code}): {Out}", p.ExitCode, p.StandardOutput.ReadToEnd());
        return false;
    }

    /// <summary>
    /// Перезапуск из новой папки. Служба настроена «перезапускать при сбое» (install.ps1, в том числе при выходе с кодом ошибки),
    /// а на случай, если SCM сочтёт выход штатным, отдельный процесс через несколько секунд сам запустит службу.
    /// </summary>
    private static void Restart()
    {
        try
        {
            Process.Start(new ProcessStartInfo("cmd.exe", $"/c ping 127.0.0.1 -n 6 >nul & sc start {ServiceName}") { CreateNoWindow = true, UseShellExecute = false });
        }
        catch (Exception) { /* останется перезапуск по сбою */ }
        Environment.ExitCode = 3;
        Environment.Exit(3);
    }

    /// <summary>Оставляем текущую и прошлую версии (для отката), остальные удаляем.</summary>
    private void CleanupOldVersions(string current, string? previousDir)
    {
        var versions = Path.Combine(InstallRoot(), "versions");
        if (!Directory.Exists(versions)) return;
        foreach (var d in Directory.GetDirectories(versions))
        {
            var name = Path.GetFileName(d);
            if (name == current || string.Equals(Path.GetFullPath(d), previousDir is null ? null : Path.GetFullPath(previousDir), StringComparison.OrdinalIgnoreCase)) continue;
            try { Directory.Delete(d, true); } catch (Exception ex) { _log.LogInformation("Cannot remove old version {Dir}: {Message}", d, ex.Message); }
        }
    }

    private static State? ReadState()
    {
        try { return File.Exists(StateFile) ? JsonSerializer.Deserialize<State>(File.ReadAllText(StateFile)) : null; }
        catch (Exception) { return null; }
    }

    private static void WriteState(State s) => File.WriteAllText(StateFile, JsonSerializer.Serialize(s));
}
