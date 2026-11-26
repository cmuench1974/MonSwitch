using MonSwitch.Core;
using MonSwitch.Core.Config;
using MonSwitch.Core.Hotkeys;
using MonSwitch.Core.Monitors;

namespace MonSwitch.Settings;

/// <summary>Dialog zum Anlegen/Bearbeiten einer Tastenkombination und der zugehörigen Monitoraktionen.</summary>
internal sealed class BindingEditorForm : Form
{
    private readonly HotkeyBinding _original;
    private readonly bool _isNew;
    private readonly IReadOnlyList<(Hotkey Hotkey, string Label)> _otherBindings;
    private readonly HotkeyConflictChecker _checker;
    private readonly List<MonitorRow> _rows = [];

    private readonly TextBox _name = new() { Dock = DockStyle.Fill, PlaceholderText = "optional, z. B. „Laptop“" };
    private readonly HotkeyBox _hotkey = new() { Width = 260 };
    private readonly CheckBox _ctrl = new() { Text = "Strg", AutoSize = true };
    private readonly CheckBox _alt = new() { Text = "Alt", AutoSize = true };
    private readonly CheckBox _shift = new() { Text = "Umschalt", AutoSize = true };
    private readonly CheckBox _win = new() { Text = "Win", AutoSize = true };
    private readonly Label _conflicts = new() { AutoSize = true, Padding = new Padding(0, 4, 0, 4) };
    private readonly RadioButton _modeSet = new() { Text = "Auf einen festen Eingang umschalten", AutoSize = true };
    private readonly RadioButton _modeToggle = new() { Text = "Zwischen zwei Eingängen hin- und herschalten", AutoSize = true };
    private readonly TableLayoutPanel _monitorTable = new() { AutoSize = true, ColumnCount = 4, Dock = DockStyle.Top };
    private readonly Button _test = new() { Text = "Jetzt testen", AutoSize = true, MinimumSize = new Size(110, 0) };

    private bool _updatingModifiers;
    private List<HotkeyConflict> _currentConflicts = [];

    public HotkeyBinding Result { get; private set; }

    public BindingEditorForm(HotkeyBinding binding, bool isNew, IReadOnlyList<(Hotkey, string)> otherBindings,
        IReadOnlyList<MonitorChoice> monitors, HotkeyConflictChecker checker)
    {
        _original = binding;
        _isNew = isNew;
        _otherBindings = otherBindings;
        _checker = checker;
        Result = binding;

        SuspendLayout();
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        Text = isNew ? "Tastenkombination hinzufügen" : "Tastenkombination bearbeiten";
        Icon = Ui.AppIcon;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(12);

        var root = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Dock = DockStyle.Fill };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, ContentWidth));

        // Bezeichnung
        root.Controls.Add(FieldLabel("&Bezeichnung:"), 0, 0);
        root.Controls.Add(_name, 1, 0);

        // Tastenkombination
        root.Controls.Add(FieldLabel("&Tastenkombination:"), 0, 1);
        var hotkeyRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
        var clear = new Button { Text = "Leeren", AutoSize = true };
        clear.Click += (_, _) => { _hotkey.Value = Hotkey.Empty; _hotkey.Focus(); };
        hotkeyRow.Controls.Add(_hotkey);
        hotkeyRow.Controls.Add(clear);
        root.Controls.Add(hotkeyRow, 1, 1);

        var modifierRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
        modifierRow.Controls.AddRange([_ctrl, _alt, _shift, _win]);
        root.Controls.Add(new Label { Text = "Modifikatoren:", AutoSize = true, Anchor = AnchorStyles.Left, ForeColor = Ui.HintColor }, 0, 2);
        root.Controls.Add(modifierRow, 1, 2);

        _conflicts.MaximumSize = new Size(ContentWidth, 0);
        root.Controls.Add(_conflicts, 1, 3);

        // Modus
        root.Controls.Add(FieldLabel("Aktion:"), 0, 4);
        var modeRow = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = Padding.Empty };
        modeRow.Controls.AddRange([_modeSet, _modeToggle]);
        root.Controls.Add(modeRow, 1, 4);

        // Monitore
        var monitorsLabel = FieldLabel("Monitore:");
        monitorsLabel.Anchor = AnchorStyles.Left | AnchorStyles.Top;
        monitorsLabel.Margin = new Padding(3, 8, 3, 3);
        root.Controls.Add(monitorsLabel, 0, 5);
        _monitorTable.Margin = new Padding(0, 6, 0, 0);
        _monitorTable.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _monitorTable.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 175));
        _monitorTable.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _monitorTable.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 175));
        BuildMonitorRows(binding, monitors);
        root.Controls.Add(_monitorTable, 1, 5);

        var hint = new Label
        {
            Text = "Eingänge, die der Monitor nicht meldet, können als Code eingegeben werden (z. B. 0x1B).\n"
                 + "Hinweis: Solange die Einstellungen geöffnet sind, sind die Tastenkombinationen deaktiviert.",
            AutoSize = true,
            MaximumSize = new Size(ContentWidth, 0),
            ForeColor = Ui.HintColor,
            Margin = new Padding(3, 8, 3, 3),
        };
        root.Controls.Add(hint, 1, 6);

        // Schaltflächen
        var ok = new Button { Text = "OK", AutoSize = true, MinimumSize = new Size(90, 0) };
        var cancel = new Button { Text = "Abbrechen", AutoSize = true, MinimumSize = new Size(90, 0), DialogResult = DialogResult.Cancel };
        ok.Click += (_, _) => OnOk();
        _test.Click += async (_, _) => await OnTestAsync();
        var buttons = new TableLayoutPanel { AutoSize = true, ColumnCount = 4, Dock = DockStyle.Fill, Margin = new Padding(0, 12, 0, 0) };
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        buttons.Controls.Add(_test, 0, 0);
        buttons.Controls.Add(ok, 2, 0);
        buttons.Controls.Add(cancel, 3, 0);
        root.Controls.Add(buttons, 0, 7);
        root.SetColumnSpan(buttons, 2);

        Controls.Add(root);
        AcceptButton = ok;
        CancelButton = cancel;

        // Werte übernehmen
        _name.Text = binding.Name ?? "";
        _hotkey.Value = binding.Hotkey;
        SyncModifierBoxes();
        _modeToggle.Checked = binding.Mode == BindingMode.Toggle;
        _modeSet.Checked = !_modeToggle.Checked;
        UpdateModeVisibility();

        _hotkey.ValueChanged += (_, _) => { SyncModifierBoxes(); UpdateConflicts(); };
        foreach (var box in new[] { _ctrl, _alt, _shift, _win })
            box.CheckedChanged += (_, _) => OnModifierBoxChanged();
        _modeSet.CheckedChanged += (_, _) => UpdateModeVisibility();

        ResumeLayout(false);
        PerformLayout();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        UpdateConflicts();
        _hotkey.Focus();
    }

    /// <summary>Breite der rechten Spalte in logischen Pixeln (96 DPI).</summary>
    private const int ContentWidth = 540;

    private static Label FieldLabel(string text) => new() { Text = text, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 6, 12, 3) };

    // ------------------------------------------------------------------ Monitorzeilen

    private sealed class MonitorRow
    {
        public required MonitorChoice Monitor { get; init; }
        public required CheckBox Include { get; init; }
        public required ComboBox InputA { get; init; }
        public required Label LabelB { get; init; }
        public required ComboBox InputB { get; init; }
    }

    // Je Monitor zwei Zeilen:  [x] Name (DISPLAYn) · aktuell: …
    //                              Eingang: [▼]   Zweiter Eingang: [▼]
    private void BuildMonitorRows(HotkeyBinding binding, IReadOnlyList<MonitorChoice> monitors)
    {
        if (monitors.Count == 0)
        {
            var none = new Label { Text = "Keine Monitore erkannt.", AutoSize = true, ForeColor = Ui.ErrorColor, Margin = new Padding(3, 6, 3, 3) };
            _monitorTable.Controls.Add(none, 0, 0);
            _monitorTable.SetColumnSpan(none, 4);
            return;
        }

        int row = 0;
        foreach (var m in monitors)
        {
            var action = binding.Actions.FirstOrDefault(a => string.Equals(a.MonitorId, m.Id, StringComparison.OrdinalIgnoreCase));
            string label = m.Label;
            if (m.Connected && m.DdcAvailable == false)
                label += " – DDC/CI nicht verfügbar";
            else if (m.Connected && m.CurrentInput is int cur)
                label += $"  ·  aktuell: {InputSources.GetName(cur)}";

            var include = new CheckBox
            {
                Text = label,
                AutoSize = true,
                Checked = action != null || (_isNew && m.Connected && m.DdcAvailable != false && binding.Actions.Count == 0),
                Margin = new Padding(3, row == 0 ? 5 : 12, 3, 2),
            };
            var inputA = CreateInputCombo(m);
            var inputB = CreateInputCombo(m);

            // Vorbelegung: A = anderer Eingang als der aktuelle (Ziel), B = aktueller Eingang (dieser PC).
            int? current = m.CurrentInput is int c ? m.Inputs.FirstOrDefault(i => InputSources.Matches(c, i), c) : null;
            int defaultA = m.Inputs.FirstOrDefault(i => current == null || i != current, m.Inputs.Count > 0 ? m.Inputs[0] : 0x11);
            int defaultB = current ?? m.Inputs.FirstOrDefault(i => i != defaultA, defaultA);
            SetComboValue(inputA, action?.InputA ?? defaultA);
            SetComboValue(inputB, action?.InputB ?? defaultB);

            include.CheckedChanged += (_, _) => { inputA.Enabled = inputB.Enabled = include.Checked; };
            inputA.Enabled = inputB.Enabled = include.Checked;

            var labelA = new Label { Text = "Eingang:", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(22, 3, 3, 3) };
            var labelB = new Label { Text = "Zweiter Eingang:", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(12, 3, 3, 3) };

            _monitorTable.Controls.Add(include, 0, row);
            _monitorTable.SetColumnSpan(include, 4);
            row++;
            _monitorTable.Controls.Add(labelA, 0, row);
            _monitorTable.Controls.Add(inputA, 1, row);
            _monitorTable.Controls.Add(labelB, 2, row);
            _monitorTable.Controls.Add(inputB, 3, row);
            row++;
            if (!m.InputsFromMonitor)
            {
                var info = new Label
                {
                    Text = "Der Monitor meldet keine Eingänge – Auswahl aus der Standardliste.",
                    AutoSize = true,
                    ForeColor = Ui.HintColor,
                    Margin = new Padding(22, 0, 3, 3),
                };
                _monitorTable.Controls.Add(info, 0, row);
                _monitorTable.SetColumnSpan(info, 4);
                row++;
            }
            _rows.Add(new MonitorRow { Monitor = m, Include = include, InputA = inputA, LabelB = labelB, InputB = inputB });
        }
    }

    /// <summary>Ignoriert das Mausrad, solange die Liste zugeklappt ist (sonst ändert Scrollen beim Überfahren versehentlich den Eingang).</summary>
    private sealed class InputComboBox : ComboBox
    {
        protected override void OnMouseWheel(MouseEventArgs e)
        {
            if (!DroppedDown && e is HandledMouseEventArgs handled)
                handled.Handled = true;
            else
                base.OnMouseWheel(e);
        }
    }

    private static ComboBox CreateInputCombo(MonitorChoice monitor)
    {
        var combo = new InputComboBox { DropDownStyle = ComboBoxStyle.DropDown, Dock = DockStyle.Fill, IntegralHeight = false, MaxDropDownItems = 12 };
        foreach (int code in monitor.Inputs)
            combo.Items.Add(InputSources.Format(code));
        return combo;
    }

    private static void SetComboValue(ComboBox combo, int code)
    {
        string text = InputSources.Format(code);
        int index = combo.Items.IndexOf(text);
        if (index < 0)
            index = combo.Items.Add(text);
        combo.SelectedIndex = index;
    }

    private void UpdateModeVisibility()
    {
        bool toggle = _modeToggle.Checked;
        foreach (var r in _rows)
            r.LabelB.Visible = r.InputB.Visible = toggle;
    }

    // ------------------------------------------------------------------ Tastenkombination

    private void SyncModifierBoxes()
    {
        _updatingModifiers = true;
        var m = _hotkey.Value.Modifiers;
        _ctrl.Checked = m.HasFlag(HotkeyModifiers.Control);
        _alt.Checked = m.HasFlag(HotkeyModifiers.Alt);
        _shift.Checked = m.HasFlag(HotkeyModifiers.Shift);
        _win.Checked = m.HasFlag(HotkeyModifiers.Win);
        _updatingModifiers = false;
    }

    private void OnModifierBoxChanged()
    {
        if (_updatingModifiers)
            return;
        var m = HotkeyModifiers.None;
        if (_ctrl.Checked) m |= HotkeyModifiers.Control;
        if (_alt.Checked) m |= HotkeyModifiers.Alt;
        if (_shift.Checked) m |= HotkeyModifiers.Shift;
        if (_win.Checked) m |= HotkeyModifiers.Win;
        _hotkey.Value = _hotkey.Value with { Modifiers = m };
    }

    private void UpdateConflicts()
    {
        var hk = _hotkey.Value;
        if (hk.IsEmpty)
        {
            _currentConflicts = [];
            _conflicts.Text = "Bitte in das Feld klicken und die gewünschte Tastenkombination drücken.";
            _conflicts.ForeColor = Ui.HintColor;
            return;
        }
        Cursor = Cursors.WaitCursor;
        try
        {
            _currentConflicts = _checker.Check(hk, _otherBindings);
        }
        finally
        {
            Cursor = Cursors.Default;
        }
        _conflicts.Text = Ui.FormatConflictSummary(_currentConflicts);
        _conflicts.ForeColor = Ui.ConflictColor(_currentConflicts);
    }

    // ------------------------------------------------------------------ Übernehmen / Testen

    /// <summary>Erzeugt die Belegung aus den Eingaben; null bei ungültigen Eingaben (Fehlermeldung wurde angezeigt).</summary>
    private HotkeyBinding? BuildBinding(bool requireHotkey)
    {
        var hk = _hotkey.Value;
        if (requireHotkey && !hk.IsValid)
        {
            Ui.ShowError(this, "Keine gültige Tastenkombination", "Bitte in das Feld „Tastenkombination“ klicken und die gewünschte Kombination drücken (z. B. Strg + Alt + 1).");
            _hotkey.Focus();
            return null;
        }

        bool toggle = _modeToggle.Checked;
        var actions = new List<MonitorAction>();
        foreach (var r in _rows.Where(r => r.Include.Checked))
        {
            if (!InputSources.TryParse(r.InputA.Text, out int a))
            {
                Ui.ShowError(this, "Ungültiger Eingang", $"„{r.InputA.Text}“ ist kein gültiger Eingang für {r.Monitor.Name}.\nBeispiele: HDMI 1, 0x11, 17.");
                r.InputA.Focus();
                return null;
            }
            int? b = null;
            if (toggle)
            {
                if (!InputSources.TryParse(r.InputB.Text, out int parsedB))
                {
                    Ui.ShowError(this, "Ungültiger Eingang", $"„{r.InputB.Text}“ ist kein gültiger Eingang für {r.Monitor.Name}.");
                    r.InputB.Focus();
                    return null;
                }
                if (parsedB == a)
                {
                    Ui.ShowError(this, "Gleiche Eingänge", $"Für {r.Monitor.Name} sind beide Eingänge identisch – zum Umschalten werden zwei verschiedene Eingänge benötigt.");
                    r.InputB.Focus();
                    return null;
                }
                b = parsedB;
            }
            actions.Add(new MonitorAction { MonitorId = r.Monitor.Id, MonitorName = r.Monitor.Name, InputA = a, InputB = b });
        }

        if (actions.Count == 0)
        {
            Ui.ShowError(this, "Kein Monitor gewählt", "Bitte mindestens einen Monitor auswählen, dessen Eingang umgeschaltet werden soll.");
            return null;
        }

        return new HotkeyBinding
        {
            Id = _original.Id,
            Enabled = _original.Enabled,
            Name = string.IsNullOrWhiteSpace(_name.Text) ? null : _name.Text.Trim(),
            Hotkey = hk,
            Mode = toggle ? BindingMode.Toggle : BindingMode.Set,
            Actions = actions,
        };
    }

    private void OnOk()
    {
        var binding = BuildBinding(requireHotkey: true);
        if (binding == null)
            return;

        // Konfliktprüfung bei jeder neu festgelegten bzw. geänderten Tastenkombination.
        bool hotkeyChanged = _isNew || binding.Hotkey != _original.Hotkey;
        if (hotkeyChanged)
        {
            var conflicts = _checker.Check(binding.Hotkey, _otherBindings);
            _currentConflicts = conflicts;
            _conflicts.Text = Ui.FormatConflictSummary(conflicts);
            _conflicts.ForeColor = Ui.ConflictColor(conflicts);

            var blocking = conflicts.Where(c => c.Level == ConflictLevel.Blocking).ToList();
            if (blocking.Count > 0)
            {
                Ui.ShowError(this, $"„{binding.Hotkey}“ kann nicht verwendet werden", string.Join("\n", blocking.Select(c => $"• {c.Description}")));
                _hotkey.Focus();
                return;
            }
            if (conflicts.Count > 0 && !Ui.ConfirmConflicts(this, binding.Hotkey, conflicts))
            {
                _hotkey.Focus();
                return;
            }
            if (conflicts.Count > 0)
                Log.Info($"Tastenkombination {binding.Hotkey} trotz {conflicts.Count} Konflikt(en) bestätigt: {string.Join(" | ", conflicts)}");
        }

        Result = binding;
        DialogResult = DialogResult.OK;
        Close();
    }

    private async Task OnTestAsync()
    {
        var binding = BuildBinding(requireHotkey: false);
        if (binding == null || !Ui.ConfirmSwitch(this, binding))
            return;

        _test.Enabled = false;
        _test.Text = "Schalte um…";
        try
        {
            var result = await Task.Run(() => SwitchExecutor.Execute(binding));
            if (!result.Success)
                Ui.ShowError(this, "Umschalten fehlgeschlagen", string.Join("\n", result.Errors));
        }
        catch (Exception ex)
        {
            Ui.ShowError(this, "Umschalten fehlgeschlagen", ex.Message);
        }
        finally
        {
            _test.Text = "Jetzt testen";
            _test.Enabled = true;
        }
    }
}
