using System.Runtime.InteropServices;
using System.Text;

namespace MonSwitch.Core.Monitors;

/// <summary>
/// Rohe DDC/CI-Schreibzugriffe über den AMD-Grafiktreiber (ADL, atiadlxx.dll) – für Befehle, die die
/// Windows-Schnittstelle nicht senden kann (abweichende Quelladresse). Die Treiber-DLL wird erst beim ersten
/// Bedarf geladen und danach nicht wieder entladen (Treiber-DLLs vertragen FreeLibrary nicht zuverlässig);
/// der ADL-Kontext existiert nur für die Dauer eines Befehls.
/// </summary>
internal static unsafe class AmdDdc
{
    private const int AdlOk = 0;

    // Strukturgrößen und Offsets laut ADL-SDK (adl_structures.h, Windows x64).
    private const int AdapterInfoSize = 1572;
    private const int AdapterIndexOffset = 4;
    private const int AdapterDisplayNameOffset = 536;
    private const int AdapterPresentOffset = 792;
    private const int DisplayInfoSize = 552;
    private const int DisplayLogicalIndexOffset = 0;
    private const int DisplayLogicalAdapterOffset = 8;
    private const int DisplayNameOffset = 20;
    private const int DisplayInfoValueOffset = 548;
    private const int ConnectedAndMapped = 0x3;
    private const int MaxPath = 256;

    private sealed class Api
    {
        public delegate* unmanaged<delegate* unmanaged<int, nint>, int, nint*, int> Create;
        public delegate* unmanaged<nint, int> Destroy;
        public delegate* unmanaged<nint, int*, int> NumberOfAdapters;
        public delegate* unmanaged<nint, byte*, int, int> AdapterInfo;
        public delegate* unmanaged<nint, int, int*, nint*, int, int> DisplayInfo;
        public delegate* unmanaged<nint, int, int, int, int, int, byte*, int*, byte*, int> DdcBlockAccess;
    }

    private static readonly Lazy<Api?> s_api = new(Load);

    public static bool IsAvailable => s_api.Value != null;

    public static bool TryWrite(string gdiDeviceName, string monitorName, ReadOnlySpan<byte> packet, out string? error)
    {
        var api = s_api.Value;
        if (api == null)
        {
            error = "Der LG-Befehl wird derzeit nur mit AMD-Grafikkarten unterstützt.";
            return false;
        }

        nint context;
        if (api.Create(&Allocate, 1, &context) != AdlOk)
        {
            error = "Die Schnittstelle des AMD-Grafiktreibers (ADL) ist nicht verfügbar.";
            return false;
        }
        try
        {
            var targets = FindDisplays(api, context, gdiDeviceName, monitorName);
            if (targets.Count == 0)
            {
                error = "Der Monitor wurde im AMD-Grafiktreiber nicht gefunden.";
                return false;
            }

            bool ok = false;
            fixed (byte* p = packet)
            {
                foreach (var (adapter, display) in targets)
                {
                    for (int attempt = 0; attempt < 2; attempt++)
                    {
                        int received = 0;
                        if (api.DdcBlockAccess(context, adapter, display, 0, 0, packet.Length, p, &received, null) >= AdlOk)
                        {
                            ok = true;
                            break;
                        }
                        Thread.Sleep(50);
                    }
                }
            }
            error = ok ? null : "Der DDC/CI-Befehl über den AMD-Grafiktreiber ist fehlgeschlagen.";
            return ok;
        }
        finally
        {
            api.Destroy(context);
        }
    }

    /// <summary>Ordnet die GDI-Anzeige (\\.\DISPLAYn) dem ADL-Adapter- und Anzeigeindex zu.</summary>
    private static List<(int Adapter, int Display)> FindDisplays(Api api, nint context, string gdiDeviceName, string monitorName)
    {
        var result = new List<(int, int)>();
        int count;
        if (api.NumberOfAdapters(context, &count) != AdlOk || count <= 0)
            return result;

        byte* infos = (byte*)NativeMemory.AllocZeroed((nuint)count, AdapterInfoSize);
        try
        {
            for (int i = 0; i < count; i++)
                *(int*)(infos + i * AdapterInfoSize) = AdapterInfoSize;
            if (api.AdapterInfo(context, infos, count * AdapterInfoSize) != AdlOk)
                return result;

            for (int i = 0; i < count && result.Count == 0; i++)
            {
                byte* info = infos + i * AdapterInfoSize;
                if (*(int*)(info + AdapterPresentOffset) == 0
                    || !string.Equals(ReadString(info + AdapterDisplayNameOffset), gdiDeviceName, StringComparison.OrdinalIgnoreCase))
                    continue;

                int adapter = *(int*)(info + AdapterIndexOffset);
                int displayCount;
                nint displays = 0;
                if (api.DisplayInfo(context, adapter, &displayCount, &displays, 0) != AdlOk || displays == 0)
                    continue;
                try
                {
                    var candidates = new List<(int Display, string Name)>();
                    for (int j = 0; j < displayCount; j++)
                    {
                        byte* d = (byte*)displays + j * DisplayInfoSize;
                        if ((*(int*)(d + DisplayInfoValueOffset) & ConnectedAndMapped) == ConnectedAndMapped
                            && *(int*)(d + DisplayLogicalAdapterOffset) == adapter)
                            candidates.Add((*(int*)(d + DisplayLogicalIndexOffset), ReadString(d + DisplayNameOffset)));
                    }
                    // Mehrere Monitore an einer GDI-Anzeige (Klonmodus): passenden Namen bevorzugen.
                    var named = candidates.Where(c => string.Equals(c.Name, monitorName, StringComparison.OrdinalIgnoreCase)).ToList();
                    foreach (var c in named.Count > 0 ? named : candidates)
                        result.Add((adapter, c.Display));
                }
                finally
                {
                    NativeMemory.Free((void*)displays);
                }
            }
        }
        finally
        {
            NativeMemory.Free(infos);
        }
        return result;
    }

    private static string ReadString(byte* text)
    {
        var span = new ReadOnlySpan<byte>(text, MaxPath);
        int end = span.IndexOf((byte)0);
        return Encoding.ASCII.GetString(end < 0 ? span : span[..end]);
    }

    /// <summary>Speicher-Callback für ADL; Freigabe mit <see cref="NativeMemory.Free"/>.</summary>
    [UnmanagedCallersOnly]
    private static nint Allocate(int size) => (nint)NativeMemory.AllocZeroed((nuint)Math.Max(size, 1));

    private static Api? Load()
    {
        if (!Environment.Is64BitProcess
            || !NativeLibrary.TryLoad("atiadlxx.dll", typeof(AmdDdc).Assembly, DllImportSearchPath.System32, out nint library))
            return null;

        if (NativeLibrary.TryGetExport(library, "ADL2_Main_Control_Create", out nint create)
            && NativeLibrary.TryGetExport(library, "ADL2_Main_Control_Destroy", out nint destroy)
            && NativeLibrary.TryGetExport(library, "ADL2_Adapter_NumberOfAdapters_Get", out nint numberOfAdapters)
            && NativeLibrary.TryGetExport(library, "ADL2_Adapter_AdapterInfo_Get", out nint adapterInfo)
            && NativeLibrary.TryGetExport(library, "ADL2_Display_DisplayInfo_Get", out nint displayInfo)
            && NativeLibrary.TryGetExport(library, "ADL2_Display_DDCBlockAccess_Get", out nint ddcBlockAccess))
        {
            return new Api
            {
                Create = (delegate* unmanaged<delegate* unmanaged<int, nint>, int, nint*, int>)create,
                Destroy = (delegate* unmanaged<nint, int>)destroy,
                NumberOfAdapters = (delegate* unmanaged<nint, int*, int>)numberOfAdapters,
                AdapterInfo = (delegate* unmanaged<nint, byte*, int, int>)adapterInfo,
                DisplayInfo = (delegate* unmanaged<nint, int, int*, nint*, int, int>)displayInfo,
                DdcBlockAccess = (delegate* unmanaged<nint, int, int, int, int, int, byte*, int*, byte*, int>)ddcBlockAccess,
            };
        }

        NativeLibrary.Free(library);
        return null;
    }
}
