using System.Buffers.Binary;
using System.Text.Json;

namespace MonSwitch.Core.Hotkeys;

/// <summary>Tastenkombination, die eine andere Anwendung in ihrer Konfiguration belegt.</summary>
public sealed record ExternalShortcut(Hotkey Hotkey, string Source, string Description);

/// <summary>
/// Findet Tastenkombinationen, die per RegisterHotKey nicht sichtbar sind:
/// Verknüpfungen (.lnk) im Startmenü/auf dem Desktop sowie PowerToys-Einstellungen.
/// </summary>
public static class ExternalShortcutScanner
{
    private const int MaxLinkFiles = 5000;

    public static List<ExternalShortcut> ScanAll()
    {
        var result = new List<ExternalShortcut>();
        try { result.AddRange(ScanShellLinks()); } catch (Exception ex) { Log.Error(ex, "Scan der Verknüpfungen"); }
        try { result.AddRange(ScanPowerToys()); } catch (Exception ex) { Log.Error(ex, "Scan der PowerToys-Einstellungen"); }
        return result;
    }

    // ------------------------------------------------------------------ Verknüpfungen (.lnk)

    public static IEnumerable<ExternalShortcut> ScanShellLinks()
    {
        // Explorer wertet Tastenkombinationen nur für Verknüpfungen im Startmenü und auf dem Desktop aus.
        var folders = new (Environment.SpecialFolder Folder, string Label)[]
        {
            (Environment.SpecialFolder.StartMenu, "Startmenü"),
            (Environment.SpecialFolder.CommonStartMenu, "Startmenü (alle Benutzer)"),
            (Environment.SpecialFolder.DesktopDirectory, "Desktop"),
            (Environment.SpecialFolder.CommonDesktopDirectory, "Desktop (alle Benutzer)"),
        };
        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, MaxRecursionDepth = 8 };
        var header = new byte[0x4C];
        int count = 0;

        foreach (var (folder, label) in folders)
        {
            string root = Environment.GetFolderPath(folder);
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
                continue;

            foreach (string file in Directory.EnumerateFiles(root, "*.lnk", options))
            {
                if (++count > MaxLinkFiles)
                    yield break;
                if (TryReadLinkHotkey(file, header, out var hotkey))
                    yield return new ExternalShortcut(hotkey, "Verknüpfung", $"„{Path.GetFileNameWithoutExtension(file)}“ ({label}) startet mit dieser Tastenkombination");
            }
        }
    }

    private static bool TryReadLinkHotkey(string path, byte[] buffer, out Hotkey hotkey)
    {
        hotkey = default;
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1, FileOptions.None);
            int read = 0;
            while (read < buffer.Length)
            {
                int n = fs.Read(buffer, read, buffer.Length - read);
                if (n == 0)
                    return false;
                read += n;
            }
            return TryParseLinkHeader(buffer, out hotkey);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static ReadOnlySpan<byte> LinkClsid => [0x01, 0x14, 0x02, 0x00, 0x00, 0x00, 0x00, 0x00, 0xC0, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x46];

    /// <summary>Wertet den ShellLinkHeader (MS-SHLLINK) aus: Feld HotKey an Offset 0x40.</summary>
    internal static bool TryParseLinkHeader(ReadOnlySpan<byte> header, out Hotkey hotkey)
    {
        hotkey = default;
        if (header.Length < 0x4C || BinaryPrimitives.ReadUInt32LittleEndian(header) != 0x4C || !header.Slice(4, 16).SequenceEqual(LinkClsid))
            return false;

        byte vk = header[0x40];
        byte flags = header[0x41];
        if (vk == 0)
            return false;

        var mods = HotkeyModifiers.None;
        if ((flags & 0x01) != 0) mods |= HotkeyModifiers.Shift;   // HOTKEYF_SHIFT
        if ((flags & 0x02) != 0) mods |= HotkeyModifiers.Control; // HOTKEYF_CONTROL
        if ((flags & 0x04) != 0) mods |= HotkeyModifiers.Alt;     // HOTKEYF_ALT
        hotkey = new Hotkey(mods, vk);
        return true;
    }

    // ------------------------------------------------------------------ PowerToys

    public static List<ExternalShortcut> ScanPowerToys()
    {
        var result = new List<ExternalShortcut>();
        string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "PowerToys");
        if (!Directory.Exists(root))
            return result;

        var enabled = ReadPowerToysEnabledModules(Path.Combine(root, "settings.json"));
        var options = new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 1, IgnoreInaccessible = true };

        foreach (string dir in Directory.EnumerateDirectories(root))
        {
            string module = Path.GetFileName(dir);
            if (enabled.TryGetValue(Normalize(module), out bool isEnabled) && !isEnabled)
                continue;

            foreach (string file in Directory.EnumerateFiles(dir, "*.json", options))
            {
                try
                {
                    if (new FileInfo(file).Length > 512 * 1024)
                        continue;
                    using var doc = JsonDocument.Parse(File.ReadAllBytes(file), new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
                    WalkPowerToysJson(doc.RootElement, module, "", result);
                }
                catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
                {
                }
            }
        }
        return result;
    }

    private static Dictionary<string, bool> ReadPowerToysEnabledModules(string path)
    {
        var map = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (!File.Exists(path))
                return map;
            using var doc = JsonDocument.Parse(File.ReadAllBytes(path));
            if (doc.RootElement.TryGetProperty("enabled", out var enabled) && enabled.ValueKind == JsonValueKind.Object)
                foreach (var p in enabled.EnumerateObject())
                    if (p.Value.ValueKind is JsonValueKind.True or JsonValueKind.False)
                        map[Normalize(p.Name)] = p.Value.GetBoolean();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
        }
        return map;
    }

    private static string Normalize(string name) => name.Replace(" ", "", StringComparison.Ordinal).Replace("_", "", StringComparison.Ordinal);

    internal static void WalkPowerToysJson(JsonElement element, string module, string path, List<ExternalShortcut> result)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                if (TryReadPowerToysHotkey(element, out var hk))
                {
                    result.Add(new ExternalShortcut(hk, "PowerToys", $"PowerToys – {module}{FormatPath(path)}"));
                    return;
                }
                if (element.TryGetProperty("originalKeys", out var keys) && keys.ValueKind == JsonValueKind.String
                    && TryParseKeyList(keys.GetString(), out var remap))
                {
                    bool appSpecific = element.TryGetProperty("targetApp", out var app) && app.ValueKind == JsonValueKind.String && app.GetString() is { Length: > 0 };
                    string scope = appSpecific ? $" (nur in {app.GetString()})" : "";
                    result.Add(new ExternalShortcut(remap, "PowerToys", $"PowerToys – Keyboard Manager: neu zugeordnete Tastenkombination{scope}"));
                    return;
                }
                foreach (var p in element.EnumerateObject())
                    WalkPowerToysJson(p.Value, module, p.Name, result);
                break;

            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                    WalkPowerToysJson(item, module, path, result);
                break;
        }
    }

    private static string FormatPath(string path) => string.IsNullOrEmpty(path) || path.Equals("value", StringComparison.OrdinalIgnoreCase) ? "" : $" ({path})";

    /// <summary>PowerToys-Format: { "win": true, "ctrl": false, "alt": false, "shift": true, "code": 67 }.</summary>
    private static bool TryReadPowerToysHotkey(JsonElement obj, out Hotkey hotkey)
    {
        hotkey = default;
        if (!obj.TryGetProperty("code", out var code) || code.ValueKind != JsonValueKind.Number || !code.TryGetUInt32(out uint vk) || vk == 0 || vk > 0xFE)
            return false;

        var mods = HotkeyModifiers.None;
        bool any = false;
        if (Flag(obj, "win", ref any)) mods |= HotkeyModifiers.Win;
        if (Flag(obj, "ctrl", ref any)) mods |= HotkeyModifiers.Control;
        if (Flag(obj, "alt", ref any)) mods |= HotkeyModifiers.Alt;
        if (Flag(obj, "shift", ref any)) mods |= HotkeyModifiers.Shift;
        if (!any)
            return false;
        hotkey = new Hotkey(mods, vk);
        return true;

        static bool Flag(JsonElement o, string name, ref bool any)
        {
            if (!o.TryGetProperty(name, out var v) || v.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                return false;
            any = true;
            return v.GetBoolean();
        }
    }

    /// <summary>Keyboard-Manager-Format: "91;160;67" (virtuelle Tastencodes, durch ; getrennt).</summary>
    internal static bool TryParseKeyList(string? list, out Hotkey hotkey)
    {
        hotkey = default;
        if (string.IsNullOrWhiteSpace(list))
            return false;
        var mods = HotkeyModifiers.None;
        uint key = 0;
        foreach (string part in list.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!uint.TryParse(part, out uint vk))
                return false;
            switch (vk)
            {
                case 0x5B or 0x5C or 0x104: mods |= HotkeyModifiers.Win; break;
                case 0x11 or 0xA2 or 0xA3: mods |= HotkeyModifiers.Control; break;
                case 0x12 or 0xA4 or 0xA5: mods |= HotkeyModifiers.Alt; break;
                case 0x10 or 0xA0 or 0xA1: mods |= HotkeyModifiers.Shift; break;
                default: key = vk; break;
            }
        }
        if (key == 0 || key > 0xFE)
            return false;
        hotkey = new Hotkey(mods, key);
        return true;
    }
}
