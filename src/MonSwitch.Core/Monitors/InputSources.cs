using System.Globalization;

namespace MonSwitch.Core.Monitors;

/// <summary>Werte für VCP-Code 0x60 (Input Source) gemäß VESA MCCS.</summary>
public static class InputSources
{
    public const byte VcpCode = 0x60;

    private static readonly Dictionary<int, string> Names = new()
    {
        [0x01] = "VGA 1",
        [0x02] = "VGA 2",
        [0x03] = "DVI 1",
        [0x04] = "DVI 2",
        [0x05] = "Composite 1",
        [0x06] = "Composite 2",
        [0x07] = "S-Video 1",
        [0x08] = "S-Video 2",
        [0x09] = "Tuner 1",
        [0x0A] = "Tuner 2",
        [0x0B] = "Tuner 3",
        [0x0C] = "Component 1",
        [0x0D] = "Component 2",
        [0x0E] = "Component 3",
        [0x0F] = "DisplayPort 1",
        [0x10] = "DisplayPort 2",
        [0x11] = "HDMI 1",
        [0x12] = "HDMI 2",
        [0x1B] = "USB-C",
    };

    /// <summary>Standardliste, falls der Monitor keine Capabilities liefert.</summary>
    public static IReadOnlyList<int> Standard { get; } = [0x0F, 0x10, 0x11, 0x12, 0x1B, 0x03, 0x04, 0x01, 0x02];

    public static string GetName(int code) =>
        Names.TryGetValue(code & 0xFF, out var name) && code <= 0xFF ? name : $"Eingang 0x{code:X2}";

    /// <summary>Anzeige wie „HDMI 1 (0x11)“ – wird von <see cref="TryParse"/> wieder erkannt.</summary>
    public static string Format(int code) => $"{GetName(code)} (0x{code:X2})";

    /// <summary>
    /// Akzeptiert „HDMI 1 (0x11)“, „0x11“, „11h“, „17“ oder einen bekannten Namen wie „HDMI 1“.
    /// </summary>
    public static bool TryParse(string? text, out int code)
    {
        code = 0;
        if (string.IsNullOrWhiteSpace(text))
            return false;
        text = text.Trim();

        int hexStart = text.IndexOf("0x", StringComparison.OrdinalIgnoreCase);
        if (hexStart >= 0)
        {
            int i = hexStart + 2, j = i;
            while (j < text.Length && Uri.IsHexDigit(text[j])) j++;
            return j > i && int.TryParse(text.AsSpan(i, j - i), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out code) && IsInRange(code);
        }

        if (text.EndsWith('h') || text.EndsWith('H'))
            return int.TryParse(text.AsSpan(0, text.Length - 1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out code) && IsInRange(code);

        if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out code))
            return IsInRange(code);

        foreach (var (value, name) in Names)
        {
            if (string.Equals(name, text, StringComparison.OrdinalIgnoreCase))
            {
                code = value;
                return true;
            }
        }
        return false;
    }

    private static bool IsInRange(int code) => code is > 0 and <= 0xFFFF;

    /// <summary>Vergleicht einen gelesenen Wert mit einem Sollwert (manche Monitore setzen zusätzliche Bits im High-Byte).</summary>
    public static bool Matches(int current, int code) => current == code || (code <= 0xFF && (current & 0xFF) == code);
}
