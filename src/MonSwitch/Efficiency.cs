namespace MonSwitch.Agent;

/// <summary>Maßnahmen, damit der Hintergrundprozess möglichst wenig Ressourcen belegt.</summary>
internal static unsafe class Efficiency
{
    private const int ProcessMemoryPriority = 0;
    private const int ProcessPowerThrottling = 4;
    private const uint PowerThrottlingExecutionSpeed = 0x1;
    private const uint MemoryPriorityLow = 2;

    /// <summary>
    /// EcoQoS („Effizienzmodus“ im Task-Manager): Windows plant den Prozess auf sparsamen Kernen bzw. niedrigem Takt ein.
    /// Die Prozesspriorität bleibt normal, damit Tastenkombinationen auch unter Last sofort reagieren.
    /// Niedrige Speicherpriorität: Seiten dieses Prozesses werden bei Speicherdruck zuerst ausgelagert.
    /// </summary>
    public static void Apply()
    {
        nint process = Win32.GetCurrentProcess();

        var throttling = new PROCESS_POWER_THROTTLING_STATE
        {
            Version = 1,
            ControlMask = PowerThrottlingExecutionSpeed,
            StateMask = PowerThrottlingExecutionSpeed,
        };
        Win32.SetProcessInformation(process, ProcessPowerThrottling, &throttling, (uint)sizeof(PROCESS_POWER_THROTTLING_STATE));

        uint memoryPriority = MemoryPriorityLow;
        Win32.SetProcessInformation(process, ProcessMemoryPriority, &memoryPriority, sizeof(uint));
    }

    /// <summary>Räumt den verwalteten Heap auf und gibt das Working Set an Windows zurück.</summary>
    public static void Trim()
    {
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        Win32.SetProcessWorkingSetSize(Win32.GetCurrentProcess(), -1, -1);
    }

    /// <summary>
    /// Kontextmenüs des Tray-Symbols im dunklen Modus darstellen (uxtheme-Ordinals 135/136, seit Windows 10 1903 stabil).
    /// Fehlt die Funktion, bleibt einfach das helle Menü.
    /// </summary>
    public static void EnableDarkMenus()
    {
        if (Environment.OSVersion.Version.Build < 18362)
            return;
        nint uxtheme = Win32.LoadLibraryEx("uxtheme.dll", 0, 0x800 /* LOAD_LIBRARY_SEARCH_SYSTEM32 */);
        if (uxtheme == 0)
            return;
        var setPreferredAppMode = (delegate* unmanaged<int, int>)Win32.GetProcAddress(uxtheme, 135);
        if (setPreferredAppMode != null)
            setPreferredAppMode(1); // AllowDark: folgt der Windows-Einstellung
        FlushMenuThemes(uxtheme);
    }

    public static void FlushMenuThemes()
    {
        if (Environment.OSVersion.Version.Build < 18362)
            return;
        nint uxtheme = Win32.LoadLibraryEx("uxtheme.dll", 0, 0x800);
        if (uxtheme != 0)
            FlushMenuThemes(uxtheme);
    }

    private static void FlushMenuThemes(nint uxtheme)
    {
        var flush = (delegate* unmanaged<void>)Win32.GetProcAddress(uxtheme, 136);
        if (flush != null)
            flush();
    }
}
