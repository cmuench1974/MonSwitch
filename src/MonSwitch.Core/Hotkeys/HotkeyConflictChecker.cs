using System.Runtime.InteropServices;
using MonSwitch.Core.Native;

namespace MonSwitch.Core.Hotkeys;

public enum ConflictLevel
{
    /// <summary>Kann nicht übernommen werden (z. B. doppelt in MonSwitch).</summary>
    Blocking,

    /// <summary>Nachweislich belegt (andere Anwendung hat sie registriert bzw. konfiguriert).</summary>
    Conflict,

    /// <summary>Wahrscheinlich belegt (bekannte Windows-/Programm-Tastenkombination, Zeicheneingabe).</summary>
    Warning,
}

public sealed record HotkeyConflict(ConflictLevel Level, string Source, string Description)
{
    public override string ToString() => $"{Source}: {Description}";
}

/// <summary>
/// Prüft eine Tastenkombination auf Konflikte:
/// 1. doppelt in MonSwitch, 2. von einem laufenden Programm global registriert (RegisterHotKey-Test),
/// 3. Verknüpfungen/PowerToys, 4. bekannte Windows-/Programm-/Treiber-Kombinationen,
/// 5. Kollisionen mit der Zeicheneingabe (AltGr, Umschalt, Alt-Menüs).
/// </summary>
public sealed class HotkeyConflictChecker
{
    private const int TestHotkeyId = 0x7FF0;
    private readonly Lazy<List<ExternalShortcut>> _external = new(ExternalShortcutScanner.ScanAll, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>Liest Verknüpfungen und PowerToys-Einstellungen vorab ein (z. B. im Hintergrund beim Öffnen der Einstellungen).</summary>
    public void Preload() => _ = _external.Value;

    /// <param name="hotkey">Zu prüfende Kombination.</param>
    /// <param name="otherBindings">Die übrigen MonSwitch-Belegungen (ohne die gerade bearbeitete).</param>
    /// <param name="testRegistration">
    /// Registrierungstest ausführen. Muss auf einem Thread laufen, dessen Hotkeys MonSwitch nicht selbst belegt –
    /// der Hintergrundprozess gibt seine Hotkeys frei, solange die Einstellungen geöffnet sind.
    /// </param>
    public List<HotkeyConflict> Check(Hotkey hotkey, IEnumerable<(Hotkey Hotkey, string Label)> otherBindings, bool testRegistration = true)
    {
        var result = new List<HotkeyConflict>();
        if (!hotkey.IsValid)
        {
            result.Add(new(ConflictLevel.Blocking, "Eingabe", "Bitte eine Haupttaste wählen (nicht nur Strg/Alt/Umschalt/Win)."));
            return result;
        }

        foreach (var (other, label) in otherBindings)
            if (other == hotkey)
                result.Add(new(ConflictLevel.Blocking, "MonSwitch", $"Bereits für „{label}“ verwendet."));

        if (testRegistration)
        {
            int error = TestRegistration(hotkey);
            if (error == User32.ERROR_HOTKEY_ALREADY_REGISTERED)
                result.Add(new(ConflictLevel.Conflict, "Anderes Programm", "Bereits von einem laufenden Programm (oder Windows selbst) als globale Tastenkombination registriert."));
            else if (error != 0)
                result.Add(new(ConflictLevel.Conflict, "Windows", $"Lässt sich nicht als globale Tastenkombination registrieren (Fehler {error})."));
        }

        foreach (var ext in _external.Value)
            if (ext.Hotkey == hotkey)
                result.Add(new(ConflictLevel.Conflict, ext.Source, ext.Description));

        foreach (var known in KnownShortcuts.Find(hotkey))
        {
            string source = known.Source switch
            {
                ShortcutSource.Windows => "Windows",
                ShortcutSource.Driver => "Grafiktreiber",
                _ => "Programme",
            };
            result.Add(new(ConflictLevel.Warning, source, known.Description));
        }

        AddTypingConflicts(hotkey, result);
        return Deduplicate(result);
    }

    private static int TestRegistration(Hotkey hotkey)
    {
        if (User32.RegisterHotKey(0, TestHotkeyId, (uint)hotkey.Modifiers | User32.MOD_NOREPEAT, hotkey.Key))
        {
            User32.UnregisterHotKey(0, TestHotkeyId);
            return 0;
        }
        return Marshal.GetLastPInvokeError();
    }

    private static void AddTypingConflicts(Hotkey hotkey, List<HotkeyConflict> result)
    {
        var m = hotkey.Modifiers;
        uint vk = hotkey.Key;
        bool isLetterOrDigit = vk is (>= 0x30 and <= 0x39) or (>= 0x41 and <= 0x5A);
        bool isF13To24 = vk is >= 0x7C and <= 0x87;

        if (vk == 0x7B && m != HotkeyModifiers.None)
            result.Add(new(ConflictLevel.Warning, "Windows", "F12 ist laut Microsoft für Debugger reserviert und sollte nicht als globale Tastenkombination verwendet werden."));

        if (m == HotkeyModifiers.None)
        {
            if (!isF13To24)
                result.Add(new(ConflictLevel.Warning, "Tastatureingabe", "Taste ohne Strg/Alt/Umschalt/Win – sie wird systemweit abgefangen und steht keinem anderen Programm mehr zur Verfügung."));
            string? plain = KeyboardLayouts.GetProducedCharacter(vk, shift: false, altGr: false);
            if (plain != null)
                result.Add(new(ConflictLevel.Warning, "Tastatureingabe", $"Erzeugt das Zeichen „{plain}“ – es könnte nicht mehr eingegeben werden."));
            return;
        }

        if (m == HotkeyModifiers.Shift)
        {
            string? ch = KeyboardLayouts.GetProducedCharacter(vk, shift: true, altGr: false);
            if (ch != null)
                result.Add(new(ConflictLevel.Warning, "Tastatureingabe", $"Umschalt + Taste erzeugt das Zeichen „{ch}“ – es könnte nicht mehr eingegeben werden."));
            return;
        }

        if (m == HotkeyModifiers.Alt && isLetterOrDigit)
            result.Add(new(ConflictLevel.Warning, "Programme", "Alt + Buchstabe/Ziffer dient in vielen Programmen als Menü-Zugriffstaste."));

        if ((m & (HotkeyModifiers.Control | HotkeyModifiers.Alt)) == (HotkeyModifiers.Control | HotkeyModifiers.Alt) && (m & HotkeyModifiers.Win) == 0)
        {
            // Strg+Alt entspricht AltGr – auf deutschen Tastaturen z. B. Strg+Alt+Q = @, Strg+Alt+E = €.
            string? ch = KeyboardLayouts.GetProducedCharacter(vk, shift: m.HasFlag(HotkeyModifiers.Shift), altGr: true);
            if (ch != null)
                result.Add(new(ConflictLevel.Warning, "Tastatureingabe", $"Strg + Alt entspricht AltGr und erzeugt hier das Zeichen „{ch}“ – es könnte nicht mehr eingegeben werden."));
        }

        if ((m == HotkeyModifiers.Control || m == (HotkeyModifiers.Control | HotkeyModifiers.Shift)) && vk is >= 0x41 and <= 0x5A
            && !result.Exists(r => r.Source == "Programme"))
        {
            result.Add(new(ConflictLevel.Warning, "Programme", "Strg + Buchstabe ist in vielen Programmen mit eigenen Funktionen belegt."));
        }
    }

    private static List<HotkeyConflict> Deduplicate(List<HotkeyConflict> list)
    {
        var seen = new HashSet<(ConflictLevel, string, string)>();
        var result = new List<HotkeyConflict>(list.Count);
        foreach (var c in list.OrderBy(c => c.Level))
            if (seen.Add((c.Level, c.Source, c.Description)))
                result.Add(c);
        return result;
    }
}

/// <summary>Ermittelt, welches Zeichen eine Tastenkombination auf den installierten Tastaturlayouts erzeugt.</summary>
internal static unsafe class KeyboardLayouts
{
    private const uint ToUnicodeDoNotChangeState = 0x4; // ab Windows 10 1607

    public static string? GetProducedCharacter(uint vk, bool shift, bool altGr)
    {
        int count = User32.GetKeyboardLayoutList(0, null);
        Span<nint> layouts = count > 0 && count <= 32 ? stackalloc nint[count] : stackalloc nint[1];
        fixed (nint* p = layouts)
        {
            if (count <= 0 || count > 32 || User32.GetKeyboardLayoutList(count, p) <= 0)
                layouts[0] = User32.GetKeyboardLayout(0);
        }

        byte* state = stackalloc byte[256];
        new Span<byte>(state, 256).Clear();
        if (shift)
        {
            state[0x10] = 0x80; // VK_SHIFT
            state[0xA0] = 0x80; // VK_LSHIFT
        }
        if (altGr)
        {
            state[0x11] = 0x80; // VK_CONTROL
            state[0x12] = 0x80; // VK_MENU
            state[0xA2] = 0x80; // VK_LCONTROL
            state[0xA5] = 0x80; // VK_RMENU
        }

        char* buffer = stackalloc char[8];
        foreach (nint hkl in layouts)
        {
            if (hkl == 0)
                continue;
            uint scan = User32.MapVirtualKeyEx(vk, User32.MAPVK_VK_TO_VSC, hkl);
            int rc = User32.ToUnicodeEx(vk, scan, state, buffer, 8, ToUnicodeDoNotChangeState, hkl);
            if (rc != 0 && !char.IsControl(buffer[0]) && !char.IsWhiteSpace(buffer[0]))
                return rc > 0 ? new string(buffer, 0, rc) : buffer[0].ToString();
        }
        return null;
    }
}
