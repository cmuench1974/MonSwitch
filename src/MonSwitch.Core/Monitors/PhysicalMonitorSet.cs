using System.Runtime.InteropServices;
using MonSwitch.Core.Native;

namespace MonSwitch.Core.Monitors;

/// <summary>
/// Momentaufnahme aller physischen Monitore mit offenen DDC/CI-Handles.
/// Handles werden nur für die Dauer einer Aktion gehalten (using) – keine dauerhaften Ressourcen.
/// </summary>
public sealed unsafe class PhysicalMonitorSet : IDisposable
{
    private readonly List<(nint Array, uint Count)> _allocations = [];
    private readonly List<PhysicalMonitor> _monitors = [];
    private bool _disposed;

    public IReadOnlyList<PhysicalMonitor> Monitors => _monitors;

    private PhysicalMonitorSet() { }

    public static PhysicalMonitorSet Open()
    {
        var set = new PhysicalMonitorSet();
        try
        {
            set.Enumerate();
        }
        catch
        {
            set.Dispose();
            throw;
        }
        return set;
    }

    /// <summary>
    /// Sucht einen Monitor anhand der gespeicherten ID. Fallback: eindeutiger Name
    /// (die Geräte-ID kann sich ändern, wenn der Monitor an einen anderen Anschluss wandert).
    /// </summary>
    public PhysicalMonitor? Find(string? id, string? name)
    {
        if (!string.IsNullOrEmpty(id))
        {
            foreach (var m in _monitors)
                if (string.Equals(m.Id, id, StringComparison.OrdinalIgnoreCase))
                    return m;
        }
        if (!string.IsNullOrEmpty(name))
        {
            PhysicalMonitor? match = null;
            foreach (var m in _monitors)
            {
                if (!string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (match != null)
                    return null; // nicht eindeutig
                match = m;
            }
            return match;
        }
        return null;
    }

    private void Enumerate()
    {
        var targets = DisplayConfig.GetTargetsByGdiName();

        var handles = new List<nint>();
        var gc = GCHandle.Alloc(handles);
        try
        {
            User32.EnumDisplayMonitors(0, 0, &EnumProc, GCHandle.ToIntPtr(gc));
        }
        finally
        {
            gc.Free();
        }

        foreach (nint hMonitor in handles)
        {
            var info = new MONITORINFOEXW { cbSize = (uint)sizeof(MONITORINFOEXW) };
            if (!User32.GetMonitorInfo(hMonitor, &info))
                continue;
            string gdiName = new string(info.szDevice);
            bool primary = (info.dwFlags & User32.MONITORINFOF_PRIMARY) != 0;

            if (!Dxva2.GetNumberOfPhysicalMonitorsFromHMONITOR(hMonitor, out uint count) || count == 0)
                continue;

            var array = (PHYSICAL_MONITOR*)NativeMemory.AllocZeroed(count, (nuint)sizeof(PHYSICAL_MONITOR));
            if (!Dxva2.GetPhysicalMonitorsFromHMONITOR(hMonitor, count, array))
            {
                NativeMemory.Free(array);
                continue;
            }
            _allocations.Add(((nint)array, count));

            targets.TryGetValue(gdiName, out var targetList);
            for (int i = 0; i < count; i++)
            {
                string description = new string(array[i].szPhysicalMonitorDescription);
                DisplayTarget? target = targetList != null && i < targetList.Count ? targetList[i] : null;

                string name = !string.IsNullOrWhiteSpace(target?.FriendlyName) ? target!.FriendlyName
                    : !string.IsNullOrWhiteSpace(description) ? description
                    : "Monitor";
                string id = !string.IsNullOrWhiteSpace(target?.DevicePath) ? target!.DevicePath : $"{gdiName}#{i}";

                _monitors.Add(new PhysicalMonitor(array[i].hPhysicalMonitor, id, name, gdiName, primary));
            }
        }
    }

    [UnmanagedCallersOnly]
    private static int EnumProc(nint hMonitor, nint hdc, RECT* rect, nint data)
    {
        var list = (List<nint>)GCHandle.FromIntPtr(data).Target!;
        list.Add(hMonitor);
        return 1;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        foreach (var m in _monitors)
            m.Invalidate();
        foreach (var (array, count) in _allocations)
        {
            Dxva2.DestroyPhysicalMonitors(count, (PHYSICAL_MONITOR*)array);
            NativeMemory.Free((void*)array);
        }
        _allocations.Clear();
    }
}

/// <summary>Ein physischer Monitor mit DDC/CI-Zugriff. Nur gültig, solange das zugehörige Set offen ist.</summary>
public sealed class PhysicalMonitor
{
    private readonly nint _handle;
    private bool _valid = true; // Handle-Werte beginnen bei 0 – daher eigenes Gültigkeitsflag

    internal PhysicalMonitor(nint handle, string id, string name, string gdiDeviceName, bool isPrimary)
    {
        _handle = handle;
        Id = id;
        Name = name;
        GdiDeviceName = gdiDeviceName;
        IsPrimary = isPrimary;
    }

    /// <summary>Stabile Geräte-ID (Monitor-Device-Path), sonst GDI-Name + Index.</summary>
    public string Id { get; }

    public string Name { get; }

    /// <summary>z. B. \\.\DISPLAY1</summary>
    public string GdiDeviceName { get; }

    public bool IsPrimary { get; }

    public string Label => FormatLabel(Name, GdiDeviceName);

    public static string FormatLabel(string name, string gdiDeviceName)
    {
        string shortName = gdiDeviceName.StartsWith(@"\\.\", StringComparison.Ordinal) ? gdiDeviceName[4..] : gdiDeviceName;
        return string.IsNullOrEmpty(shortName) ? name : $"{name} ({shortName})";
    }

    internal void Invalidate() => _valid = false;

    public bool TryGetVcp(byte code, out uint current, out uint maximum)
    {
        current = maximum = 0;
        if (!_valid)
            return false;
        // DDC/CI ist störanfällig – ein zweiter Versuch behebt die meisten sporadischen Fehler.
        for (int attempt = 0; attempt < 2; attempt++)
        {
            if (Dxva2.GetVCPFeatureAndVCPFeatureReply(_handle, code, out _, out current, out maximum))
                return true;
            Thread.Sleep(50);
        }
        return false;
    }

    public bool TrySetVcp(byte code, uint value)
    {
        if (!_valid)
            return false;
        for (int attempt = 0; attempt < 2; attempt++)
        {
            if (Dxva2.SetVCPFeature(_handle, code, value))
                return true;
            Thread.Sleep(50);
        }
        return false;
    }

    public bool TryGetInput(out int input)
    {
        bool ok = TryGetVcp(InputSources.VcpCode, out uint current, out _);
        input = (int)current;
        return ok;
    }

    public bool TrySetInput(int input) => TrySetVcp(InputSources.VcpCode, (uint)input);

    /// <summary>Liest den Capabilities-String (dauert je nach Monitor 1–3 Sekunden).</summary>
    public unsafe string? TryGetCapabilities()
    {
        if (!_valid)
            return null;
        for (int attempt = 0; attempt < 2; attempt++)
        {
            if (Dxva2.GetCapabilitiesStringLength(_handle, out uint length) && length > 0 && length < 64 * 1024)
            {
                byte[] buffer = new byte[length];
                fixed (byte* p = buffer)
                {
                    if (Dxva2.CapabilitiesRequestAndCapabilitiesReply(_handle, p, length))
                    {
                        int end = Array.IndexOf(buffer, (byte)0);
                        return System.Text.Encoding.ASCII.GetString(buffer, 0, end < 0 ? buffer.Length : end);
                    }
                }
            }
            Thread.Sleep(100);
        }
        return null;
    }

    public override string ToString() => Label;
}

internal sealed record DisplayTarget(string FriendlyName, string DevicePath);

/// <summary>Ermittelt Anzeigename und stabile Geräte-ID je GDI-Anzeige (\\.\DISPLAYn) über QueryDisplayConfig.</summary>
internal static unsafe class DisplayConfig
{
    public static Dictionary<string, List<DisplayTarget>> GetTargetsByGdiName()
    {
        var result = new Dictionary<string, List<DisplayTarget>>(StringComparer.OrdinalIgnoreCase);
        for (int attempt = 0; attempt < 3; attempt++)
        {
            if (User32.GetDisplayConfigBufferSizes(User32.QDC_ONLY_ACTIVE_PATHS, out uint numPaths, out uint numModes) != 0)
                return result;

            var paths = new DISPLAYCONFIG_PATH_INFO[Math.Max(numPaths, 1)];
            var modes = new byte[Math.Max(numModes, 1) * User32.DISPLAYCONFIG_MODE_INFO_SIZE];
            int rc;
            fixed (DISPLAYCONFIG_PATH_INFO* pPaths = paths)
            fixed (byte* pModes = modes)
                rc = User32.QueryDisplayConfig(User32.QDC_ONLY_ACTIVE_PATHS, ref numPaths, pPaths, ref numModes, pModes, 0);

            if (rc == 122) // ERROR_INSUFFICIENT_BUFFER: Konfiguration hat sich zwischenzeitlich geändert
                continue;
            if (rc != 0)
                return result;

            for (int i = 0; i < numPaths; i++)
            {
                ref var path = ref paths[i];

                var source = new DISPLAYCONFIG_SOURCE_DEVICE_NAME();
                source.header.type = User32.DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME;
                source.header.size = (uint)sizeof(DISPLAYCONFIG_SOURCE_DEVICE_NAME);
                source.header.adapterId = path.sourceInfo.adapterId;
                source.header.id = path.sourceInfo.id;
                if (User32.DisplayConfigGetDeviceInfo(&source.header) != 0)
                    continue;

                var target = new DISPLAYCONFIG_TARGET_DEVICE_NAME();
                target.header.type = User32.DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME;
                target.header.size = (uint)sizeof(DISPLAYCONFIG_TARGET_DEVICE_NAME);
                target.header.adapterId = path.targetInfo.adapterId;
                target.header.id = path.targetInfo.id;
                if (User32.DisplayConfigGetDeviceInfo(&target.header) != 0)
                    continue;

                string gdi = new string(source.viewGdiDeviceName);
                var entry = new DisplayTarget(new string(target.monitorFriendlyDeviceName), new string(target.monitorDevicePath));
                if (!result.TryGetValue(gdi, out var list))
                    result[gdi] = list = [];
                list.Add(entry);
            }
            return result;
        }
        return result;
    }
}
