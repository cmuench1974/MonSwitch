using System.Globalization;

namespace MonSwitch.Core.Monitors;

/// <summary>
/// Liest Werte aus dem MCCS-Capabilities-String, z. B.
/// <c>(prot(monitor)type(lcd)model(U2723QE)vcp(02 04 10 12 60(0F 11 12 1B) D6(01 04))mccs_ver(2.1))</c>.
/// </summary>
public static class CapabilitiesParser
{
    /// <summary>Liefert die erlaubten Werte eines VCP-Codes oder null, wenn der Code nicht (mit Werteliste) enthalten ist.</summary>
    public static List<int>? GetVcpValues(string? capabilities, byte vcpCode)
    {
        if (string.IsNullOrEmpty(capabilities))
            return null;

        int start = FindSection(capabilities, "vcp");
        if (start < 0)
            return null;

        int depth = 1;
        int i = start;
        string? lastToken = null;
        List<int>? result = null;
        bool collecting = false;

        while (i < capabilities.Length && depth > 0)
        {
            char c = capabilities[i];
            if (c == '(')
            {
                depth++;
                collecting = depth == 2 && lastToken != null && TryParseHex(lastToken, out int code) && code == vcpCode;
                if (collecting)
                    result = [];
                i++;
            }
            else if (c == ')')
            {
                if (collecting && depth == 2)
                    return result;
                depth--;
                lastToken = null;
                i++;
            }
            else if (Uri.IsHexDigit(c))
            {
                int j = i;
                while (j < capabilities.Length && Uri.IsHexDigit(capabilities[j])) j++;
                string token = capabilities[i..j];
                if (collecting)
                {
                    // Manche Monitore schreiben Werte ohne Leerzeichen: "0F1112".
                    for (int k = 0; k + 1 < token.Length; k += 2)
                        if (TryParseHex(token.Substring(k, 2), out int v) && !result!.Contains(v))
                            result.Add(v);
                }
                else if (depth == 1)
                {
                    // Auf Ebene 1 kann ebenfalls zusammengeschrieben sein – relevant ist das letzte Byte vor "(".
                    lastToken = token.Length > 2 ? token[^2..] : token;
                }
                i = j;
            }
            else
            {
                i++;
            }
        }
        return null;
    }

    /// <summary>Liefert den Inhalt von model(...), falls vorhanden.</summary>
    public static string? GetModel(string? capabilities)
    {
        if (string.IsNullOrEmpty(capabilities))
            return null;
        int start = FindSection(capabilities, "model");
        if (start < 0)
            return null;
        int end = capabilities.IndexOf(')', start);
        if (end <= start)
            return null;
        string model = capabilities[start..end].Trim();
        return model.Length > 0 ? model : null;
    }

    /// <summary>Index direkt hinter "name(" – nur wenn "name" ein eigenständiger Bezeichner ist (nicht "vcpname").</summary>
    private static int FindSection(string caps, string name)
    {
        int from = 0;
        while (true)
        {
            int idx = caps.IndexOf(name + "(", from, StringComparison.OrdinalIgnoreCase);
            if (idx < 0)
                return -1;
            if (idx == 0 || !char.IsLetterOrDigit(caps[idx - 1]) && caps[idx - 1] != '_')
                return idx + name.Length + 1;
            from = idx + 1;
        }
    }

    private static bool TryParseHex(string s, out int value) =>
        int.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
}
