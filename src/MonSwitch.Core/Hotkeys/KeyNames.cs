using MonSwitch.Core.Native;

namespace MonSwitch.Core.Hotkeys;

/// <summary>Deutsche Tastenbezeichnungen (wie auf deutschen Tastaturen beschriftet).</summary>
public static class KeyNames
{
    public const string Separator = " + ";

    public static string Format(Hotkey hotkey)
    {
        if (hotkey.IsEmpty && hotkey.Modifiers == HotkeyModifiers.None)
            return "(keine)";

        var parts = new List<string>(5);
        AppendModifiers(parts, hotkey.Modifiers);
        if (!hotkey.IsEmpty)
            parts.Add(GetKeyName(hotkey.Key));
        return string.Join(Separator, parts);
    }

    public static string FormatModifiers(HotkeyModifiers modifiers)
    {
        var parts = new List<string>(4);
        AppendModifiers(parts, modifiers);
        return string.Join(Separator, parts);
    }

    private static void AppendModifiers(List<string> parts, HotkeyModifiers m)
    {
        if (m.HasFlag(HotkeyModifiers.Win)) parts.Add("Win");
        if (m.HasFlag(HotkeyModifiers.Control)) parts.Add("Strg");
        if (m.HasFlag(HotkeyModifiers.Alt)) parts.Add("Alt");
        if (m.HasFlag(HotkeyModifiers.Shift)) parts.Add("Umschalt");
    }

    public static string GetKeyName(uint vk)
    {
        switch (vk)
        {
            case >= 0x30 and <= 0x39: return ((char)vk).ToString();
            case >= 0x41 and <= 0x5A: return ((char)vk).ToString();
            case >= 0x60 and <= 0x69: return "Num " + (vk - 0x60);
            case >= 0x70 and <= 0x87: return "F" + (vk - 0x6F);
        }

        string? name = vk switch
        {
            0x03 => "Abbrechen",
            0x08 => "Rücktaste",
            0x09 => "Tab",
            0x0C => "Num 5 (ohne Num)",
            0x0D => "Eingabe",
            0x13 => "Pause",
            0x14 => "Feststelltaste",
            0x1B => "Esc",
            0x20 => "Leertaste",
            0x21 => "Bild auf",
            0x22 => "Bild ab",
            0x23 => "Ende",
            0x24 => "Pos1",
            0x25 => "Pfeil links",
            0x26 => "Pfeil oben",
            0x27 => "Pfeil rechts",
            0x28 => "Pfeil unten",
            0x2C => "Druck",
            0x2D => "Einfg",
            0x2E => "Entf",
            0x2F => "Hilfe",
            0x5D => "Menütaste",
            0x5F => "Standby",
            0x6A => "Num *",
            0x6B => "Num +",
            0x6C => "Num Trennzeichen",
            0x6D => "Num -",
            0x6E => "Num ,",
            0x6F => "Num /",
            0x90 => "Num",
            0x91 => "Rollen",
            0xA6 => "Browser zurück",
            0xA7 => "Browser vor",
            0xA8 => "Browser aktualisieren",
            0xA9 => "Browser stopp",
            0xAA => "Browser suchen",
            0xAB => "Browser Favoriten",
            0xAC => "Browser Startseite",
            0xAD => "Stumm",
            0xAE => "Leiser",
            0xAF => "Lauter",
            0xB0 => "Nächster Titel",
            0xB1 => "Vorheriger Titel",
            0xB2 => "Medien stopp",
            0xB3 => "Wiedergabe/Pause",
            0xB4 => "E-Mail",
            0xB5 => "Medien",
            0xB6 => "Anwendung 1",
            0xB7 => "Anwendung 2",
            _ => null,
        };
        if (name != null)
            return name;

        // OEM-Tasten: Zeichen gemäß aktuellem Tastaturlayout (z. B. Ü, Ö, Ä, ß, +, #, <).
        uint ch = User32.MapVirtualKey(vk, User32.MAPVK_VK_TO_CHAR) & 0x7FFF;
        if (ch > 0x20)
            return char.ToUpperInvariant((char)ch).ToString();

        return $"Taste 0x{vk:X2}";
    }
}
