namespace MonSwitch.Core.Monitors;

/// <summary>
/// Herstellerspezifische Eingangsumschaltung vieler LG-Monitore (u. a. UltraGear): Sie bestätigen den
/// Standardbefehl VCP 0x60, ignorieren ihn aber stillschweigend und schalten nur über VCP 0xF4 mit der
/// DDC/CI-Quelladresse 0x50 um. Windows (dxva2) sendet immer die Quelladresse 0x51 – das Paket wird deshalb
/// roh über den Grafiktreiber geschickt (derzeit AMD, siehe <see cref="AmdDdc"/>).
/// </summary>
public static class LgInput
{
    public const byte VcpCode = 0xF4;
    public const byte SourceAddress = 0x50;

    // MCCS-Standardcode → LG-Code. Andere Werte (z. B. von Hand eingegebene LG-Codes wie 0x90) werden unverändert gesendet.
    private static readonly Dictionary<int, int> FromStandard = new()
    {
        [0x0F] = 0xD0, // DisplayPort 1
        [0x10] = 0xD1, // DisplayPort 2
        [0x11] = 0x90, // HDMI 1
        [0x12] = 0x91, // HDMI 2
        [0x1B] = 0xD2, // USB-C
    };

    /// <summary>Kann der LG-Befehl auf diesem System gesendet werden? (Lädt beim ersten Aufruf den Grafiktreiber.)</summary>
    public static bool IsTransportAvailable => AmdDdc.IsAvailable;

    /// <summary>LG-Monitore tragen die PnP-Herstellerkennung „GSM“ (GoldStar) im Geräte-Pfad, z. B. \\?\DISPLAY#GSM9E9D#….</summary>
    public static bool IsLgMonitor(string? monitorId)
    {
        if (string.IsNullOrEmpty(monitorId))
            return false;
        int index = monitorId.IndexOf("DISPLAY#", StringComparison.OrdinalIgnoreCase);
        return index >= 0 && monitorId.AsSpan(index + 8).StartsWith("GSM", StringComparison.OrdinalIgnoreCase);
    }

    public static int ToLgCode(int input) => FromStandard.TryGetValue(input, out int lg) ? lg : input;

    public static bool TrySet(PhysicalMonitor monitor, int input, out string? error)
    {
        byte[] packet = BuildSetVcpPacket(SourceAddress, VcpCode, (ushort)ToLgCode(input));
        return AmdDdc.TryWrite(monitor.GdiDeviceName, monitor.Name, packet, out error);
    }

    /// <summary>DDC/CI „Set VCP Feature“: Zieladresse 0x6E, Quelle, Länge, Opcode 0x03, Code, Wert (16 Bit), XOR-Prüfsumme.</summary>
    internal static byte[] BuildSetVcpPacket(byte sourceAddress, byte vcpCode, ushort value)
    {
        byte[] packet = [0x6E, sourceAddress, 0x84, 0x03, vcpCode, (byte)(value >> 8), (byte)value, 0];
        byte checksum = 0;
        for (int i = 0; i < packet.Length - 1; i++)
            checksum ^= packet[i];
        packet[^1] = checksum;
        return packet;
    }
}
