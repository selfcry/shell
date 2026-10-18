using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

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

    public static bool IsRunningInActiveSession(string processName)
    {
        var session = WTSGetActiveConsoleSessionId();
        if (session == NoSession) return true; // никто не вошёл — запускать пока некуда
        foreach (var p in Process.GetProcessesByName(processName))
        {
            using (p) { if ((uint)p.SessionId == session) return true; }
        }
        return false;
    }

    public static bool StartInActiveSession(string exePath, out string? error)
    {
        error = null;
        if (!File.Exists(exePath)) { error = $"file not found: {exePath}"; return false; }

        var session = WTSGetActiveConsoleSessionId();
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
            CloseHandle(pi.hProcess); CloseHandle(pi.hThread);
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
