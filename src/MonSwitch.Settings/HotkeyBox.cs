using System.ComponentModel;
using System.Runtime.InteropServices;
using MonSwitch.Core.Hotkeys;

namespace MonSwitch.Settings;

/// <summary>
/// Eingabefeld, das eine Tastenkombination aufzeichnet. Kombinationen mit der Windows-Taste fängt Windows
/// teilweise selbst ab – dafür gibt es im Editor zusätzlich Kontrollkästchen für die Modifikatoren.
/// </summary>
internal sealed partial class HotkeyBox : TextBox
{
    private Hotkey _value;

    public HotkeyBox()
    {
        ReadOnly = true;
        ShortcutsEnabled = false;
        Cursor = Cursors.IBeam;
        TextAlign = HorizontalAlignment.Center;
        UpdateText();
    }

    public event EventHandler? ValueChanged;

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Hotkey Value
    {
        get => _value;
        set
        {
            if (_value == value)
                return;
            _value = value;
            UpdateText();
            ValueChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        BackColor = SystemColors.Window; // ReadOnly würde das Feld grau darstellen
    }

    protected override bool IsInputKey(Keys keyData)
    {
        // Tab/Umschalt+Tab bleiben für die Navigation, alles andere wird aufgezeichnet.
        var key = keyData & Keys.KeyCode;
        var mods = keyData & Keys.Modifiers;
        if (key == Keys.Tab && (mods == Keys.None || mods == Keys.Shift))
            return false;
        return true;
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        // Alt-Kombinationen (WM_SYSKEYDOWN) nicht an Menüs/Fenster weitergeben – sonst schließt z. B. Alt+F4 den Dialog.
        const int WM_KEYDOWN = 0x0100, WM_SYSKEYDOWN = 0x0104;
        if (Focused && (msg.Msg == WM_KEYDOWN || msg.Msg == WM_SYSKEYDOWN) && IsInputKey(keyData))
        {
            HandleKeyDown(keyData & Keys.KeyCode, keyData & Keys.Modifiers);
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        e.Handled = true;
        e.SuppressKeyPress = true;
        HandleKeyDown(e.KeyCode, e.Modifiers);
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        e.Handled = true;
        // „Druck“ liefert unter Windows kein KeyDown, nur KeyUp.
        if (e.KeyCode == Keys.PrintScreen)
        {
            Value = new Hotkey(CurrentModifiers(e.Modifiers), (uint)Keys.PrintScreen);
            return;
        }
        if (IsModifier(e.KeyCode) && (ModifierKeys == Keys.None) && !IsWinPressed())
            UpdateText();
    }

    protected override void OnKeyPress(KeyPressEventArgs e) => e.Handled = true;

    protected override void OnLostFocus(EventArgs e)
    {
        base.OnLostFocus(e);
        UpdateText();
    }

    private void HandleKeyDown(Keys key, Keys modifiers)
    {
        var mods = CurrentModifiers(modifiers);
        if (IsModifier(key))
        {
            // Nur Modifikatoren gedrückt: Zwischenstand anzeigen.
            SetDisplay(mods == HotkeyModifiers.None ? "" : KeyNames.FormatModifiers(mods) + KeyNames.Separator + "…");
            return;
        }
        Value = new Hotkey(mods, (uint)key);
        UpdateText();
    }

    private static HotkeyModifiers CurrentModifiers(Keys modifiers)
    {
        var mods = HotkeyModifiers.None;
        if ((modifiers & Keys.Control) != 0) mods |= HotkeyModifiers.Control;
        if ((modifiers & Keys.Alt) != 0) mods |= HotkeyModifiers.Alt;
        if ((modifiers & Keys.Shift) != 0) mods |= HotkeyModifiers.Shift;
        if (IsWinPressed()) mods |= HotkeyModifiers.Win;
        return mods;
    }

    private static bool IsModifier(Keys key) => key is Keys.ShiftKey or Keys.ControlKey or Keys.Menu or Keys.LWin or Keys.RWin
        or Keys.LShiftKey or Keys.RShiftKey or Keys.LControlKey or Keys.RControlKey or Keys.LMenu or Keys.RMenu;

    private static bool IsWinPressed() => (GetKeyState(0x5B) & 0x8000) != 0 || (GetKeyState(0x5C) & 0x8000) != 0;

    private void UpdateText() => SetDisplay(_value.IsEmpty ? "Hier klicken und Tasten drücken" : _value.ToString());

    private void SetDisplay(string text)
    {
        Text = text;
        Select(text.Length, 0); // keine Markierung – es wird ja nichts „eingetippt“
    }

    protected override void OnEnter(EventArgs e)
    {
        base.OnEnter(e);
        // TextBox markiert beim Fokuserhalt alles – danach wieder aufheben.
        BeginInvoke(() => Select(Text.Length, 0));
    }

    [LibraryImport("user32.dll")]
    private static partial short GetKeyState(int nVirtKey);
}
