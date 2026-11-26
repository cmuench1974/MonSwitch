namespace MonSwitch.Core.Hotkeys;

/// <summary>Modifikatoren mit denselben Werten wie MOD_* von RegisterHotKey.</summary>
[Flags]
public enum HotkeyModifiers : uint
{
    None = 0,
    Alt = 0x1,
    Control = 0x2,
    Shift = 0x4,
    Win = 0x8,
}

/// <summary>Globale Tastenkombination: Modifikatoren + virtueller Tastencode.</summary>
public readonly record struct Hotkey(HotkeyModifiers Modifiers, uint Key)
{
    public static readonly Hotkey Empty = default;

    public bool IsEmpty => Key == 0;

    public bool HasModifiers => Modifiers != HotkeyModifiers.None;

    /// <summary>True, wenn die Kombination registriert werden kann (Haupttaste vorhanden und keine reine Modifikatortaste).</summary>
    public bool IsValid => Key is > 0 and < 0xFF && !IsModifierKey(Key);

    public static bool IsModifierKey(uint vk) => vk is 0x10 or 0x11 or 0x12 or 0x5B or 0x5C or (>= 0xA0 and <= 0xA5);

    public override string ToString() => KeyNames.Format(this);
}
