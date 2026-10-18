using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ClubShell.Locker;

/// <summary>
/// Низкоуровневый хук клавиатуры: пока ПК заблокирован, гасит Win, Alt+Tab, Alt+Esc, Ctrl+Esc, Alt+F4.
/// Ctrl+Alt+Del хук перехватить не может — он отключается политиками (scripts/harden.ps1).
/// </summary>
internal sealed class KeyboardHook : IDisposable
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100, WM_SYSKEYDOWN = 0x0104;
    private const int VK_TAB = 0x09, VK_ESCAPE = 0x1B, VK_F4 = 0x73, VK_LWIN = 0x5B, VK_RWIN = 0x5C, VK_CONTROL = 0x11;
    private const int LLKHF_ALTDOWN = 0x20;

    private delegate nint HookProc(int nCode, nint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT { public int vkCode, scanCode, flags, time; public nint dwExtraInfo; }

    [DllImport("user32.dll", SetLastError = true)] private static extern nint SetWindowsHookEx(int id, HookProc proc, nint hMod, uint threadId);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(nint hook);
    [DllImport("user32.dll")] private static extern nint CallNextHookEx(nint hook, int nCode, nint wParam, nint lParam);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int vKey);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern nint GetModuleHandle(string? name);

    private readonly HookProc _proc; // держим ссылку, иначе делегат соберёт GC
    private nint _hook;

    /// <summary>Блокировать ли клавиши (true в режиме блокировки, false во время сессии).</summary>
    public bool Enabled { get; set; }

    public KeyboardHook()
    {
        _proc = Callback;
        using var module = Process.GetCurrentProcess().MainModule!;
        _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(module.ModuleName), 0);
    }

    private nint Callback(int nCode, nint wParam, nint lParam)
    {
        if (nCode >= 0 && Enabled && (wParam == WM_KEYDOWN || wParam == WM_SYSKEYDOWN))
        {
            var k = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            var alt = (k.flags & LLKHF_ALTDOWN) != 0;
            var ctrl = (GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0;

            var block =
                k.vkCode is VK_LWIN or VK_RWIN ||
                (alt && k.vkCode is VK_TAB or VK_ESCAPE or VK_F4) ||
                (ctrl && k.vkCode == VK_ESCAPE);
            if (block) return 1;
        }
        return CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        if (_hook != 0) { UnhookWindowsHookEx(_hook); _hook = 0; }
    }
}
