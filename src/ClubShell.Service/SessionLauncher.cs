using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace ClubShell.Service;

/// <summary>Запуск процесса в интерактивной сессии пользователя из службы (она работает в сессии 0, без экрана).</summary>
internal static class SessionLauncher
{
    private const uint NoSession = 0xFFFFFFFF;
    private const int TokenPrimary = 1, SecurityImpersonation = 2;
    private const uint TokenAllAccess = 0xF01FF, CreateUnicodeEnvironment = 0x400, CreateNoWindow = 0x08000000;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct STARTUPINFO
    {
        public int cb; public string? lpReserved, lpDesktop, lpTitle;
        public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
        public short wShowWindow, cbReserved2; public nint lpReserved2, hStdInput, hStdOutput, hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_INFORMATION { public nint hProcess, hThread; public int dwProcessId, dwThreadId; }

    [DllImport("kernel32.dll")] private static extern uint WTSGetActiveConsoleSessionId();
    [DllImport("wtsapi32.dll", SetLastError = true)] private static extern bool WTSQueryUserToken(uint sessionId, out nint token);
    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool DuplicateTokenEx(nint existing, uint access, nint attrs, int impersonation, int tokenType, out nint newToken);
    [DllImport("userenv.dll", SetLastError = true)] private static extern bool CreateEnvironmentBlock(out nint env, nint token, bool inherit);
    [DllImport("userenv.dll", SetLastError = true)] private static extern bool DestroyEnvironmentBlock(nint env);
    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CreateProcessAsUser(nint token, string? app, string cmd, nint pa, nint ta, bool inherit,
        uint flags, nint env, string? dir, ref STARTUPINFO si, out PROCESS_INFORMATION pi);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(nint h);

    [DllImport("kernel32.dll")] private static extern uint WaitForSingleObject(SafeProcessHandle h, uint ms);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern SafeProcessHandle OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool QueryFullProcessImageName(SafeProcessHandle process, uint flags, char[] name, ref int size);
    [DllImport("wtsapi32.dll", SetLastError = true)] private static extern bool WTSLogoffSession(nint server, uint sessionId, bool wait);
    private const uint WaitTimeout = 0x102, Synchronize = 0x00100000, ProcessQueryLimitedInformation = 0x1000;

    /// <summary>Сессия, которая сейчас на мониторе (консоль); null — никто не вошёл.</summary>
    public static uint? ActiveSession()
    {
        var session = WTSGetActiveConsoleSessionId();
        return session == NoSession ? null : session;
    }

    /// <summary>В сессии кто-то вошёл (а не экран входа Windows).</summary>
    public static bool HasUser(uint session)
    {
        if (!WTSQueryUserToken(session, out var token)) return false;
        CloseHandle(token);
        return true;
    }

    public static bool IsProcessAlive(SafeProcessHandle h) => WaitForSingleObject(h, 0) == WaitTimeout;

    /// <summary>Открыть процесс для слежения (жив ли) и чтения пути; null — нет доступа или процесс уже завершился.</summary>
    public static SafeProcessHandle? Open(int pid)
    {
        var h = OpenProcess(Synchronize | ProcessQueryLimitedInformation, false, pid);
        if (h.IsInvalid) { h.Dispose(); return null; }
        return h;
    }

    public static string? ImagePath(SafeProcessHandle h)
    {
        var buf = new char[1024];
        var size = buf.Length;
        return QueryFullProcessImageName(h, 0, buf, ref size) ? new string(buf, 0, size) : null;
    }

    public static bool SamePath(string? a, string b) =>
        a is not null && string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);

    /// <summary>Завершить сеанс Windows гостя: закрываются все его процессы (в том числе приостановленное или подменённое окно).</summary>
    public static bool Logoff(uint session, out string? error)
    {
        error = WTSLogoffSession(0, session, false) ? null : Win32("WTSLogoffSession");
        return error is null;
    }

    /// <summary>Запуск в активной сессии. Handle процесса остаётся у вызывающего: по нему страж следит именно за своим окном.</summary>
    public static bool StartInActiveSession(string exePath, out SafeProcessHandle? process, out int pid, out uint session, out string? error)
    {
        error = null; process = null; pid = 0;
        session = WTSGetActiveConsoleSessionId();
        if (!File.Exists(exePath)) { error = $"file not found: {exePath}"; return false; }
        if (session == NoSession) { error = "no active console session"; return false; }

        if (!WTSQueryUserToken(session, out var userToken)) { error = Win32("WTSQueryUserToken"); return false; }
        nint primary = 0, env = 0;
        try
        {
            if (!DuplicateTokenEx(userToken, TokenAllAccess, 0, SecurityImpersonation, TokenPrimary, out primary))
            { error = Win32("DuplicateTokenEx"); return false; }
            if (!CreateEnvironmentBlock(out env, primary, false)) env = 0;

            var si = new STARTUPINFO { cb = Marshal.SizeOf<STARTUPINFO>(), lpDesktop = @"winsta0\default" };
            var ok = CreateProcessAsUser(primary, null, $"\"{exePath}\"", 0, 0, false,
                CreateUnicodeEnvironment | CreateNoWindow, env, Path.GetDirectoryName(exePath), ref si, out var pi);
            if (!ok) { error = Win32("CreateProcessAsUser"); return false; }
            CloseHandle(pi.hThread);
            process = new SafeProcessHandle(pi.hProcess, ownsHandle: true);
            pid = pi.dwProcessId;
            return true;
        }
        finally
        {
            if (env != 0) DestroyEnvironmentBlock(env);
            if (primary != 0) CloseHandle(primary);
            CloseHandle(userToken);
        }
    }

    private static string Win32(string api) => $"{api}: {new Win32Exception(Marshal.GetLastWin32Error()).Message}";
}
