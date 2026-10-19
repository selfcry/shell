using System.ComponentModel;
using System.Runtime.InteropServices;
using ClubShell.Core;

namespace ClubShell.Service;

/// <summary>
/// Мьютекс «одно окно блокировки» в Global\ — его создаёт служба при старте, до входа гостя.
/// Обычный пользователь не может создавать объекты в Global\ (нет SeCreateGlobalPrivilege), а DACL даёт гостю
/// только ожидание и освобождение: занять имя заранее или закрыть к нему доступ, чтобы окно не запустилось, он не может.
/// </summary>
internal static class LockerMutex
{
    // SYSTEM и администраторы — всё; вошедшие пользователи — SYNCHRONIZE | MUTEX_MODIFY_STATE.
    private const string Sddl = "D:P(A;;GA;;;SY)(A;;GA;;;BA)(A;;0x00100001;;;AU)";

    [StructLayout(LayoutKind.Sequential)]
    private struct SECURITY_ATTRIBUTES { public int nLength; public nint lpSecurityDescriptor; public int bInheritHandle; }

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool ConvertStringSecurityDescriptorToSecurityDescriptor(string sddl, uint revision, out nint sd, out uint size);
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern nint CreateMutex(ref SECURITY_ATTRIBUTES sa, bool initialOwner, string name);
    [DllImport("kernel32.dll")] private static extern nint LocalFree(nint h);

    private static nint _handle; // живёт, пока работает служба

    public static void Create(ILogger log)
    {
        if (!ConvertStringSecurityDescriptorToSecurityDescriptor(Sddl, 1, out var sd, out _))
        {
            log.LogError("Locker mutex: bad SDDL: {Error}", new Win32Exception().Message);
            return;
        }
        try
        {
            var sa = new SECURITY_ATTRIBUTES { nLength = Marshal.SizeOf<SECURITY_ATTRIBUTES>(), lpSecurityDescriptor = sd };
            _handle = CreateMutex(ref sa, false, PipeNames.LockerMutex);
            if (_handle == 0) log.LogError("Locker mutex {Name}: {Error}", PipeNames.LockerMutex, new Win32Exception().Message);
        }
        finally { LocalFree(sd); }
    }
}
