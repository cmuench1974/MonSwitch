using System.Text.Json.Serialization;
using MonSwitch.Core.Hotkeys;
using MonSwitch.Core.Monitors;

namespace MonSwitch.Core.Config;

public sealed class AppConfig
{
    public int Version { get; set; } = 1;

    /// <summary>Fehler beim Umschalten bzw. beim Registrieren als Windows-Benachrichtigung anzeigen.</summary>
    public bool ShowErrorNotifications { get; set; } = true;

    public List<HotkeyBinding> Bindings { get; set; } = [];

    /// <summary>Zwischenspeicher der (langsam per DDC/CI gelesenen) unterstützten Eingänge je Monitor.</summary>
    public List<MonitorCacheEntry> MonitorCache { get; set; } = [];

    public AppConfig Clone() => ConfigStore.Deserialize(ConfigStore.Serialize(this));
}

public enum BindingMode
{
    /// <summary>Alle gewählten Monitore auf einen festen Eingang schalten.</summary>
    Set,

    /// <summary>Zwischen zwei Eingängen hin- und herschalten (abhängig vom aktuellen Eingang).</summary>
    Toggle,
}

public sealed class HotkeyBinding
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public bool Enabled { get; set; } = true;

    /// <summary>Optionale Bezeichnung, z. B. „Arbeitslaptop“.</summary>
    public string? Name { get; set; }

    /// <summary>MOD_ALT=1, MOD_CONTROL=2, MOD_SHIFT=4, MOD_WIN=8.</summary>
    public uint Modifiers { get; set; }

    /// <summary>Virtueller Tastencode.</summary>
    public uint Key { get; set; }

    public BindingMode Mode { get; set; } = BindingMode.Set;

    public List<MonitorAction> Actions { get; set; } = [];

    [JsonIgnore]
    public Hotkey Hotkey
    {
        get => new((HotkeyModifiers)Modifiers, Key);
        set
        {
            Modifiers = (uint)value.Modifiers;
            Key = value.Key;
        }
    }

    public string DescribeActions()
    {
        if (Actions.Count == 0)
            return "(keine Monitore gewählt)";
        var parts = new List<string>(Actions.Count);
        foreach (var a in Actions)
        {
            string target = Mode == BindingMode.Toggle && a.InputB is int b
                ? $"{InputSources.GetName(a.InputA)} ⇄ {InputSources.GetName(b)}"
                : InputSources.GetName(a.InputA);
            parts.Add($"{a.MonitorName}: {target}");
        }
        return string.Join("; ", parts);
    }

    [JsonIgnore]
    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? DescribeActions() : Name!;
}

/// <summary>Befehl, mit dem der Eingang eines Monitors umgeschaltet wird.</summary>
public enum SwitchMethod
{
    /// <summary>LG-Monitore: LG-Befehl, sofern der Grafiktreiber ihn senden kann – sonst Standardbefehl.</summary>
    Auto,

    /// <summary>MCCS-Standard: VCP 0x60 über die DDC/CI-Schnittstelle von Windows.</summary>
    Standard,

    /// <summary>LG-spezifisch: VCP 0xF4 mit Quelladresse 0x50 (siehe <see cref="LgInput"/>).</summary>
    Lg,
}

public sealed class MonitorAction
{
    public string MonitorId { get; set; } = "";

    public string MonitorName { get; set; } = "";

    /// <summary>Zieleingang (bzw. erster Eingang beim Umschalten).</summary>
    public int InputA { get; set; }

    /// <summary>Zweiter Eingang – nur im Modus <see cref="BindingMode.Toggle"/>.</summary>
    public int? InputB { get; set; }

    public SwitchMethod Method { get; set; } = SwitchMethod.Auto;
}

public sealed class MonitorCacheEntry
{
    public string Id { get; set; } = "";

    public string Name { get; set; } = "";

    public List<int>? Inputs { get; set; }
}
