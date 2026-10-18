using System.Diagnostics;
using System.Net.NetworkInformation;
using NAudio.CoreAudioApi;

namespace ClubShell.Locker;

/// <summary>Громкость и выключение звука через Windows Core Audio (устройство вывода по умолчанию).</summary>
internal sealed class AudioControl : IDisposable
{
    private readonly MMDeviceEnumerator _enumerator = new();

    private MMDevice? Device()
    {
        try { return _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia); }
        catch (Exception ex) { Debug.WriteLine($"[ClubShell] no audio device: {ex.Message}"); return null; }
    }

    public (int Volume, bool Muted) Get()
    {
        using var d = Device();
        if (d is null) return (0, true);
        var v = d.AudioEndpointVolume;
        return ((int)Math.Round(v.MasterVolumeLevelScalar * 100), v.Mute);
    }

    public void SetVolume(int percent)
    {
        using var d = Device();
        if (d is not null) d.AudioEndpointVolume.MasterVolumeLevelScalar = Math.Clamp(percent, 0, 100) / 100f;
    }

    public void SetMute(bool muted)
    {
        using var d = Device();
        if (d is not null) d.AudioEndpointVolume.Mute = muted;
    }

    public void Dispose() => _enumerator.Dispose();
}

internal static class NetworkStatus
{
    /// <summary>Первый работающий не-loopback адаптер: имя и скорость канала.</summary>
    public static (bool Online, string? Name, int? SpeedMbps) Read()
    {
        try
        {
            var nic = NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up
                            && n.NetworkInterfaceType != NetworkInterfaceType.Loopback
                            && n.NetworkInterfaceType != NetworkInterfaceType.Tunnel)
                .OrderByDescending(n => n.NetworkInterfaceType == NetworkInterfaceType.Ethernet)
                .FirstOrDefault();
            if (nic is null) return (false, null, null);
            var kind = nic.NetworkInterfaceType == NetworkInterfaceType.Ethernet ? "Ethernet" : "Wi-Fi";
            return (true, kind, nic.Speed > 0 ? (int)(nic.Speed / 1_000_000) : null);
        }
        catch { return (false, null, null); }
    }
}
