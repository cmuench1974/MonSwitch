using System.Text.Json;
using MonSwitch.Core.Config;
using MonSwitch.Core.Hotkeys;
using MonSwitch.Core.Monitors;

namespace MonSwitch.Core.Tests;

public class CapabilitiesParserTests
{
    private const string LgCaps = "(prot(monitor)type(lcd)model(WK95U)cmds(01 02 03 0C E3 F3)vcp(02 04 05 08 10 12 14(05 08 0B ) 16 18 1A 52 60(11 12 0F 10 ) AC AE B2 B6 C0 C6 C8 C9 D6(01 04) DF 62)mccs_ver(2.1)mswhql(1))";

    [Fact]
    public void ReadsInputSourcesFromRealCapabilities()
    {
        Assert.Equal([0x11, 0x12, 0x0F, 0x10], CapabilitiesParser.GetVcpValues(LgCaps, 0x60));
    }

    [Fact]
    public void ReadsOtherNestedCode() => Assert.Equal([0x05, 0x08, 0x0B], CapabilitiesParser.GetVcpValues(LgCaps, 0x14));

    [Fact]
    public void ReturnsNullWhenCodeHasNoValueList() => Assert.Null(CapabilitiesParser.GetVcpValues(LgCaps, 0x10));

    [Fact]
    public void HandlesValuesWithoutSpaces() =>
        Assert.Equal([0x0F, 0x11, 0x12], CapabilitiesParser.GetVcpValues("vcp(10 12 60(0F1112) 62)", 0x60));

    [Fact]
    public void IgnoresVcpNameSection() =>
        Assert.Equal([0x11], CapabilitiesParser.GetVcpValues("(vcpname(60(Eingang)) vcp(60(11)))", 0x60));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("(prot(monitor))")]
    public void ReturnsNullForMissingData(string? caps) => Assert.Null(CapabilitiesParser.GetVcpValues(caps, 0x60));

    [Fact]
    public void ReadsModel() => Assert.Equal("WK95U", CapabilitiesParser.GetModel(LgCaps));
}

public class InputSourcesTests
{
    [Theory]
    [InlineData("HDMI 1 (0x11)", 0x11)]
    [InlineData("0x1B", 0x1B)]
    [InlineData("1Bh", 0x1B)]
    [InlineData("15", 15)]
    [InlineData("displayport 1", 0x0F)]
    [InlineData("Eingang 0x90 (0x90)", 0x90)]
    public void ParsesInput(string text, int expected)
    {
        Assert.True(InputSources.TryParse(text, out int code));
        Assert.Equal(expected, code);
    }

    [Theory]
    [InlineData("")]
    [InlineData("HDMI 9")]
    [InlineData("0x")]
    [InlineData("0")]
    public void RejectsInvalidInput(string text) => Assert.False(InputSources.TryParse(text, out _));

    [Fact]
    public void FormatRoundTrips()
    {
        foreach (int code in InputSources.Standard)
        {
            Assert.True(InputSources.TryParse(InputSources.Format(code), out int parsed));
            Assert.Equal(code, parsed);
        }
    }

    [Fact]
    public void MatchesIgnoresHighByte()
    {
        Assert.True(InputSources.Matches(0x0F, 0x0F));
        Assert.True(InputSources.Matches(0x010F, 0x0F));
        Assert.False(InputSources.Matches(0x11, 0x0F));
    }
}

public class HotkeyTests
{
    [Fact]
    public void FormatsInGermanOrder() =>
        Assert.Equal("Win + Strg + Alt + Umschalt + 1", new Hotkey(HotkeyModifiers.Control | HotkeyModifiers.Alt | HotkeyModifiers.Shift | HotkeyModifiers.Win, '1').ToString());

    [Theory]
    [InlineData(0x70u, "F1")]
    [InlineData(0x87u, "F24")]
    [InlineData(0x25u, "Pfeil links")]
    [InlineData(0x2Eu, "Entf")]
    [InlineData(0x63u, "Num 3")]
    public void NamesKeys(uint vk, string expected) => Assert.Equal(expected, KeyNames.GetKeyName(vk));

    [Fact]
    public void ModifierOnlyIsInvalid()
    {
        Assert.False(new Hotkey(HotkeyModifiers.Control, 0x11).IsValid);
        Assert.False(Hotkey.Empty.IsValid);
        Assert.True(new Hotkey(HotkeyModifiers.Control, 'A').IsValid);
    }
}

public class ConflictSourceTests
{
    [Fact]
    public void KnowsWindowsShortcuts()
    {
        Assert.Contains(KnownShortcuts.Find(new Hotkey(HotkeyModifiers.Win, 'L')), s => s.Source == ShortcutSource.Windows);
        Assert.Contains(KnownShortcuts.Find(new Hotkey(HotkeyModifiers.Win | HotkeyModifiers.Shift, 0x25)), s => s.Description.Contains("Monitor"));
        Assert.Empty(KnownShortcuts.Find(new Hotkey(HotkeyModifiers.Control | HotkeyModifiers.Alt, '1')));
    }

    [Fact]
    public void NoDuplicateKnownShortcutsWithinSameSource()
    {
        var duplicates = KnownShortcuts.All
            .GroupBy(s => (s.Modifiers, s.Key, s.Source))
            .Where(g => g.Count() > 1 && g.Select(s => s.Description).Distinct().Count() < g.Count())
            .ToList();
        Assert.Empty(duplicates);
    }

    [Fact]
    public void ParsesShellLinkHeaderHotkey()
    {
        var header = new byte[0x4C];
        header[0] = 0x4C;
        byte[] clsid = [0x01, 0x14, 0x02, 0x00, 0x00, 0x00, 0x00, 0x00, 0xC0, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x46];
        clsid.CopyTo(header, 4);
        header[0x40] = (byte)'M';
        header[0x41] = 0x02 | 0x04; // Strg + Alt

        Assert.True(ExternalShortcutScanner.TryParseLinkHeader(header, out var hk));
        Assert.Equal(new Hotkey(HotkeyModifiers.Control | HotkeyModifiers.Alt, 'M'), hk);

        header[0x40] = 0;
        Assert.False(ExternalShortcutScanner.TryParseLinkHeader(header, out _));
    }

    [Fact]
    public void RejectsNonLinkFiles() => Assert.False(ExternalShortcutScanner.TryParseLinkHeader(new byte[0x4C], out _));

    [Fact]
    public void ReadsPowerToysShortcuts()
    {
        const string json = """
            {
              "properties": {
                "ActivationShortcut": { "value": { "win": true, "ctrl": false, "alt": false, "shift": true, "code": 67, "key": "C" } }
              },
              "remapShortcuts": { "global": [ { "originalKeys": "162;164;77", "newRemapKeys": "65" } ] }
            }
            """;
        using var doc = JsonDocument.Parse(json);
        var result = new List<ExternalShortcut>();
        ExternalShortcutScanner.WalkPowerToysJson(doc.RootElement, "ColorPicker", "", result);

        Assert.Contains(result, r => r.Hotkey == new Hotkey(HotkeyModifiers.Win | HotkeyModifiers.Shift, 'C'));
        Assert.Contains(result, r => r.Hotkey == new Hotkey(HotkeyModifiers.Control | HotkeyModifiers.Alt, 'M') && r.Description.Contains("Keyboard Manager"));
    }

    [Fact]
    public void BlocksDuplicateWithinMonSwitch()
    {
        var checker = new HotkeyConflictChecker();
        var hk = new Hotkey(HotkeyModifiers.Control | HotkeyModifiers.Alt, '1');
        var conflicts = checker.Check(hk, [(hk, "Laptop")], testRegistration: false);
        Assert.Contains(conflicts, c => c.Level == ConflictLevel.Blocking);
        Assert.Equal(ConflictLevel.Blocking, conflicts[0].Level);
    }

    [Fact]
    public void WarnsAboutKeyWithoutModifier()
    {
        var conflicts = new HotkeyConflictChecker().Check(new Hotkey(HotkeyModifiers.None, 'A'), [], testRegistration: false);
        Assert.Contains(conflicts, c => c.Source == "Tastatureingabe");
    }
}

public class ConfigTests
{
    [Fact]
    public void RoundTripsConfiguration()
    {
        var config = new AppConfig
        {
            ShowErrorNotifications = false,
            Bindings =
            [
                new HotkeyBinding
                {
                    Name = "Laptop",
                    Hotkey = new Hotkey(HotkeyModifiers.Control | HotkeyModifiers.Alt, '2'),
                    Mode = BindingMode.Toggle,
                    Actions = [new MonitorAction { MonitorId = @"\\?\DISPLAY#GSM9E9D", MonitorName = "LG", InputA = 0x0F, InputB = 0x11 }],
                },
            ],
            MonitorCache = [new MonitorCacheEntry { Id = "x", Name = "LG", Inputs = [0x0F, 0x11] }],
        };

        string json = ConfigStore.Serialize(config);
        Assert.Contains("\"mode\": \"Toggle\"", json);
        Assert.DoesNotContain("\"hotkey\"", json, StringComparison.OrdinalIgnoreCase);

        var copy = ConfigStore.Deserialize(json);
        Assert.False(copy.ShowErrorNotifications);
        var b = Assert.Single(copy.Bindings);
        Assert.Equal(config.Bindings[0].Hotkey, b.Hotkey);
        Assert.Equal(BindingMode.Toggle, b.Mode);
        Assert.Equal(0x11, b.Actions[0].InputB);
        Assert.Equal([0x0F, 0x11], copy.MonitorCache[0].Inputs);
    }

    [Fact]
    public void ToleratesMissingCollections()
    {
        var config = ConfigStore.Deserialize("""{ "bindings": [ { "key": 49, "modifiers": 3, "actions": null } ] }""");
        Assert.Empty(config.MonitorCache);
        Assert.Empty(config.Bindings[0].Actions);
    }

    [Fact]
    public void DescribesToggleAction()
    {
        var b = new HotkeyBinding
        {
            Mode = BindingMode.Toggle,
            Actions = [new MonitorAction { MonitorName = "LG", InputA = 0x0F, InputB = 0x11 }],
        };
        Assert.Equal("LG: DisplayPort 1 ⇄ HDMI 1", b.DescribeActions());
    }

    [Fact]
    public void RoundTripsSwitchMethodAndDefaultsToAuto()
    {
        var config = new AppConfig
        {
            Bindings = [new HotkeyBinding { Key = 'M', Actions = [new MonitorAction { MonitorName = "LG", InputA = 0x12, Method = SwitchMethod.Standard }] }],
        };
        string json = ConfigStore.Serialize(config);
        Assert.Contains("\"method\": \"Standard\"", json);
        Assert.DoesNotContain("displayName", json, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(SwitchMethod.Standard, ConfigStore.Deserialize(json).Bindings[0].Actions[0].Method);

        // Konfigurationen ohne „method“ (Version 1.0) verwenden die automatische Auswahl.
        var old = ConfigStore.Deserialize("""{ "bindings": [ { "key": 77, "actions": [ { "monitorName": "LG", "inputA": 15 } ] } ] }""");
        Assert.Equal(SwitchMethod.Auto, old.Bindings[0].Actions[0].Method);
    }
}

public class LgInputTests
{
    [Theory]
    [InlineData(@"\\?\DISPLAY#GSM9E9D#7&3658f4a&0&UID264#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}", true)]
    [InlineData(@"\\?\DISPLAY#gsm5B7F#5&1a2b3c&0&UID4352#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}", true)]
    [InlineData(@"\\?\DISPLAY#DELA1B2#5&1a2b3c&0&UID4352#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}", false)]
    [InlineData(@"\\.\DISPLAY1#0", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void RecognizesLgMonitors(string? id, bool expected) => Assert.Equal(expected, LgInput.IsLgMonitor(id));

    [Theory]
    [InlineData(0x0F, 0xD0)] // DisplayPort 1
    [InlineData(0x10, 0xD1)] // DisplayPort 2
    [InlineData(0x11, 0x90)] // HDMI 1
    [InlineData(0x12, 0x91)] // HDMI 2
    [InlineData(0x1B, 0xD2)] // USB-C
    [InlineData(0x90, 0x90)] // von Hand eingegebener LG-Code bleibt unverändert
    public void MapsStandardCodesToLgCodes(int input, int expected) => Assert.Equal(expected, LgInput.ToLgCode(input));

    [Fact]
    public void BuildsSetVcpPacketWithChecksum() =>
        // Auf dem Testmonitor (LG ULTRAGEAR+, AMD RX 6900 XT) verifiziert: schaltet auf DisplayPort 1.
        Assert.Equal([0x6E, 0x50, 0x84, 0x03, 0xF4, 0x00, 0xD0, 0x9D], LgInput.BuildSetVcpPacket(0x50, 0xF4, 0x00D0));

    [Fact]
    public void ChecksumCoversAllBytesIncludingDestination() =>
        // Quelladresse 0x51 = normaler Host; Prüfsumme = XOR über alle Bytes einschließlich 0x6E.
        Assert.Equal([0x6E, 0x51, 0x84, 0x03, 0x10, 0x00, 0x32, 0x9A], LgInput.BuildSetVcpPacket(0x51, 0x10, 50));
}
