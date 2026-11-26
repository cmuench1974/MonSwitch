using MonSwitch.Core;
using MonSwitch.Core.Config;
using MonSwitch.Core.Hotkeys;
using MonSwitch.Core.Monitors;

namespace MonSwitch.Settings;

internal sealed class MainForm : Form
{
    private readonly AppConfig _config;
    private readonly HotkeyConflictChecker _checker = new();
    private readonly bool _agentWasRunning;

    private readonly ListView _bindings = CreateList(checkBoxes: true);
    private readonly ListView _monitorList = CreateList(checkBoxes: false);
    private readonly Button _add = Btn("&Hinzufügen…");
    private readonly Button _edit = Btn("&Bearbeiten…");
    private readonly Button _remove = Btn("&Entfernen");
    private readonly Button _run = Btn("&Jetzt ausführen");
    private readonly Button _refresh = Btn("&Neu erkennen");
    private readonly CheckBox _autostart = new() { Text = "Mit &Windows starten", AutoSize = true };
    private readonly CheckBox _notifications = new() { Text = "Fehler als Windows-Benachrichtigung anzeigen", AutoSize = true };
    private readonly Label _status = new() { AutoSize = true, Anchor = AnchorStyles.Left };
    private readonly Button _ok = Btn("OK");
    private readonly Button _cancel = Btn("Abbrechen");
    private readonly Button _apply = Btn("Ü&bernehmen");

    private IReadOnlyList<DetectedMonitor> _monitors = [];
    private CancellationTokenSource? _detectCts;
    private bool _dirty;
    private bool _saved;
    private bool _populating;

    public MainForm(bool agentWasRunning)
    {
        _agentWasRunning = agentWasRunning;
        _config = ConfigStore.Load();

        SuspendLayout();
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        Text = "MonSwitch – Einstellungen";
        Icon = Ui.AppIcon;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(820, 600);
        MinimumSize = new Size(680, 520);
        Padding = new Padding(12);

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1 };
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 55));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 45));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        // Tastenkombinationen
        _bindings.Columns.Add("Tastenkombination", 210);
        _bindings.Columns.Add("Bezeichnung", 130);
        _bindings.Columns.Add("Aktion", 300);
        root.Controls.Add(Group("Tastenkombinationen (Häkchen = aktiv)", _bindings, _add, _edit, _remove, _run), 0, 0);

        // Monitore
        _monitorList.Columns.Add("Monitor", 235);
        _monitorList.Columns.Add("Aktueller Eingang", 140);
        _monitorList.Columns.Add("Unterstützte Eingänge", 265);
        root.Controls.Add(Group("Erkannte Monitore", _monitorList, _refresh), 0, 1);

        // Optionen
        var options = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 6, 0, 0) };
        options.Controls.AddRange([_autostart, _notifications]);
        root.Controls.Add(options, 0, 2);

        _status.MaximumSize = new Size(780, 0);
        _status.ForeColor = Ui.HintColor;
        _status.Margin = new Padding(3, 6, 3, 0);
        root.Controls.Add(_status, 0, 3);

        var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Margin = new Padding(0, 10, 0, 0) };
        buttons.Controls.AddRange([_apply, _cancel, _ok]);
        root.Controls.Add(buttons, 0, 4);

        Controls.Add(root);

        CancelButton = _cancel;
        ResumeLayout(false);
        PerformLayout();

        // Ereignisse
        _add.Click += (_, _) => AddBinding();
        _edit.Click += (_, _) => EditSelected();
        _remove.Click += (_, _) => RemoveSelected();
        _run.Click += async (_, _) => await RunSelectedAsync();
        _refresh.Click += (_, _) => StartDetection();
        _bindings.DoubleClick += (_, _) => EditSelected();
        _bindings.SelectedIndexChanged += (_, _) => UpdateButtons();
        _bindings.ItemChecked += OnBindingChecked;
        _bindings.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Delete) RemoveSelected();
            else if (e.KeyCode == Keys.Enter) EditSelected();
        };
        _autostart.CheckedChanged += (_, _) => MarkDirty();
        _notifications.CheckedChanged += (_, _) => MarkDirty();
        _ok.Click += (_, _) => { if (Save()) { _dirty = false; Close(); } };
        _cancel.Click += (_, _) => Close();
        _apply.Click += (_, _) => Save();

        // Werte laden
        _populating = true;
        try { _autostart.Checked = Autostart.IsEnabled(); } catch (Exception ex) { Log.Error(ex, "Autostart lesen"); }
        _notifications.Checked = _config.ShowErrorNotifications;
        _populating = false;
        PopulateBindings();
        _dirty = false;
        UpdateButtons();
        UpdateStatus();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        Activate();
        // Verknüpfungen und PowerToys-Einstellungen im Hintergrund vorab einlesen.
        _ = Task.Run(_checker.Preload);
        StartDetection();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (_dirty && e.CloseReason == CloseReason.UserClosing)
        {
            var save = new TaskDialogButton("Speichern");
            var discard = new TaskDialogButton("Verwerfen");
            var result = TaskDialog.ShowDialog(this, new TaskDialogPage
            {
                Caption = "MonSwitch",
                Heading = "Änderungen speichern?",
                Text = "Die Einstellungen wurden geändert, aber noch nicht gespeichert.",
                Icon = TaskDialogIcon.Warning,
                Buttons = { save, discard, TaskDialogButton.Cancel },
                DefaultButton = save,
            });
            if (result == save)
            {
                if (!Save())
                    e.Cancel = true;
            }
            else if (result != discard)
            {
                e.Cancel = true;
            }
        }
        if (!e.Cancel)
            _detectCts?.Cancel();
        base.OnFormClosing(e);
    }

    // ------------------------------------------------------------------ Aufbau

    private static ListView CreateList(bool checkBoxes) => new()
    {
        Dock = DockStyle.Fill,
        View = View.Details,
        FullRowSelect = true,
        HideSelection = false,
        MultiSelect = false,
        CheckBoxes = checkBoxes,
        HeaderStyle = ColumnHeaderStyle.Nonclickable,
    };

    private static Button Btn(string text) => new() { Text = text, AutoSize = true, MinimumSize = new Size(100, 0) };

    private static GroupBox Group(string title, Control content, params Button[] buttons)
    {
        var group = new GroupBox { Text = title, Dock = DockStyle.Fill, Padding = new Padding(8) };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var panel = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Dock = DockStyle.Fill, Margin = new Padding(6, 0, 0, 0) };
        foreach (var b in buttons)
        {
            b.Dock = DockStyle.Top;
            panel.Controls.Add(b);
        }
        layout.Controls.Add(content, 0, 0);
        layout.Controls.Add(panel, 1, 0);
        group.Controls.Add(layout);
        return group;
    }

    // Spaltenbreiten sind in logischen Pixeln (96 DPI) angegeben – ListView skaliert sie nicht selbst.
    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        ScaleColumns(DeviceDpi / 96.0);
        _bindings.Resize += (_, _) => FillLastColumn(_bindings);
        _monitorList.Resize += (_, _) => FillLastColumn(_monitorList);
        FillLastColumn(_bindings);
        FillLastColumn(_monitorList);
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        ScaleColumns(e.DeviceDpiNew / (double)e.DeviceDpiOld);
        FillLastColumn(_bindings);
        FillLastColumn(_monitorList);
    }

    private void ScaleColumns(double factor)
    {
        if (Math.Abs(factor - 1.0) < 0.01)
            return;
        foreach (var list in new[] { _bindings, _monitorList })
            foreach (ColumnHeader column in list.Columns)
                column.Width = (int)Math.Round(column.Width * factor);
    }

    private static void FillLastColumn(ListView list)
    {
        if (list.Columns.Count == 0 || !list.IsHandleCreated)
            return;
        int used = 0;
        for (int i = 0; i < list.Columns.Count - 1; i++)
            used += list.Columns[i].Width;
        int width = list.ClientSize.Width - used - 2;
        if (width > 80)
            list.Columns[^1].Width = width;
    }

    // ------------------------------------------------------------------ Tastenkombinationen

    private void PopulateBindings()
    {
        _populating = true;
        try
        {
            string? selectedId = SelectedBinding?.Id;
            _bindings.BeginUpdate();
            _bindings.Items.Clear();
            foreach (var b in _config.Bindings)
            {
                var item = new ListViewItem(b.Hotkey.ToString()) { Tag = b, Checked = b.Enabled };
                item.SubItems.Add(b.Name ?? "");
                item.SubItems.Add(b.DescribeActions());
                _bindings.Items.Add(item);
                if (b.Id == selectedId)
                    item.Selected = true;
            }
            _bindings.EndUpdate();
        }
        finally
        {
            _populating = false;
        }
        UpdateButtons();
    }

    private HotkeyBinding? SelectedBinding => _bindings.SelectedItems.Count > 0 ? _bindings.SelectedItems[0].Tag as HotkeyBinding : null;

    private void UpdateButtons()
    {
        bool selected = SelectedBinding != null;
        _edit.Enabled = selected;
        _remove.Enabled = selected;
        _run.Enabled = selected;
        _apply.Enabled = _dirty;
    }

    private void OnBindingChecked(object? sender, ItemCheckedEventArgs e)
    {
        if (_populating || e.Item.Tag is not HotkeyBinding b || b.Enabled == e.Item.Checked)
            return;
        b.Enabled = e.Item.Checked;
        MarkDirty();
    }

    private List<(Hotkey, string)> OtherBindings(HotkeyBinding? except) =>
        _config.Bindings.Where(b => !ReferenceEquals(b, except)).Select(b => (b.Hotkey, b.DisplayName)).ToList();

    private void AddBinding()
    {
        var binding = new HotkeyBinding();
        using var editor = new BindingEditorForm(binding, isNew: true, OtherBindings(null), MonitorChoice.Build(_monitors, _config), _checker);
        if (editor.ShowDialog(this) != DialogResult.OK)
            return;
        _config.Bindings.Add(editor.Result);
        PopulateBindings();
        SelectBinding(editor.Result);
        MarkDirty();
    }

    private void EditSelected()
    {
        var binding = SelectedBinding;
        if (binding == null)
            return;
        using var editor = new BindingEditorForm(binding, isNew: false, OtherBindings(binding), MonitorChoice.Build(_monitors, _config), _checker);
        if (editor.ShowDialog(this) != DialogResult.OK)
            return;
        int index = _config.Bindings.IndexOf(binding);
        _config.Bindings[index] = editor.Result;
        PopulateBindings();
        SelectBinding(editor.Result);
        MarkDirty();
    }

    private void SelectBinding(HotkeyBinding binding)
    {
        foreach (ListViewItem item in _bindings.Items)
        {
            if (ReferenceEquals(item.Tag, binding))
            {
                item.Selected = true;
                item.Focused = true;
                item.EnsureVisible();
            }
        }
        _bindings.Focus();
    }

    private void RemoveSelected()
    {
        var binding = SelectedBinding;
        if (binding == null)
            return;
        _config.Bindings.Remove(binding);
        PopulateBindings();
        MarkDirty();
    }

    private async Task RunSelectedAsync()
    {
        var binding = SelectedBinding;
        if (binding == null || !Ui.ConfirmSwitch(this, binding))
            return;
        _run.Enabled = false;
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
            UpdateButtons();
        }
    }

    // ------------------------------------------------------------------ Monitore

    private void StartDetection()
    {
        _detectCts?.Cancel();
        var cts = _detectCts = new CancellationTokenSource();
        _refresh.Enabled = false;
        SetStatus("Monitore werden über DDC/CI abgefragt …");

        Task.Run(() =>
        {
            try
            {
                var result = MonitorDiscovery.Detect(readCapabilities: true,
                    progress: list => PostToUi(() => ShowMonitors(list)), cancel: cts.Token);
                PostToUi(() => OnDetectionFinished(result));
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Monitorerkennung");
                PostToUi(() => { _refresh.Enabled = true; SetStatus("Monitorerkennung fehlgeschlagen: " + ex.Message); });
            }
        }, cts.Token);
    }

    private void PostToUi(Action action)
    {
        if (IsDisposed || !IsHandleCreated)
            return;
        try { BeginInvoke(action); } catch (InvalidOperationException) { }
    }

    private void ShowMonitors(IReadOnlyList<DetectedMonitor> monitors)
    {
        _monitors = monitors.ToList();
        _monitorList.BeginUpdate();
        _monitorList.Items.Clear();
        foreach (var m in monitors)
        {
            string current = m.CurrentInput is int c ? InputSources.Format(c)
                : m.DdcAvailable == false ? "DDC/CI nicht verfügbar"
                : "wird gelesen …";
            string supported = m.SupportedInputs is { Count: > 0 } s ? string.Join(", ", s.Select(InputSources.GetName))
                : !m.CapabilitiesRead ? "wird gelesen …"
                : m.DdcAvailable == true ? "keine Angabe (Standardliste wird verwendet)"
                : "–";
            var item = new ListViewItem(m.Label + (m.IsPrimary ? " · Hauptbildschirm" : ""));
            item.SubItems.Add(current);
            item.SubItems.Add(supported);
            _monitorList.Items.Add(item);
        }
        _monitorList.EndUpdate();
    }

    private void OnDetectionFinished(List<DetectedMonitor> monitors)
    {
        ShowMonitors(monitors);
        _refresh.Enabled = true;
        UpdateMonitorCache(monitors);

        int noDdc = monitors.Count(m => m.DdcAvailable == false);
        if (monitors.Count == 0)
            SetStatus("Es wurden keine Monitore gefunden.");
        else if (noDdc > 0)
            SetStatus($"{noDdc} Monitor(e) antworten nicht per DDC/CI. Bitte im Menü des Monitors „DDC/CI“ aktivieren. " +
                      "Über Dockingstationen, KVM-Switches oder DisplayLink-Adapter wird DDC/CI häufig nicht durchgereicht.");
        else
            UpdateStatus();
    }

    /// <summary>Unterstützte Eingänge zwischenspeichern, damit die Auswahl beim nächsten Öffnen sofort verfügbar ist.</summary>
    private void UpdateMonitorCache(List<DetectedMonitor> monitors)
    {
        bool changed = false;
        foreach (var m in monitors.Where(m => m.SupportedInputs is { Count: > 0 }))
        {
            var entry = _config.MonitorCache.FirstOrDefault(c => string.Equals(c.Id, m.Id, StringComparison.OrdinalIgnoreCase));
            if (entry == null)
                _config.MonitorCache.Add(entry = new MonitorCacheEntry { Id = m.Id });
            if (entry.Name != m.Name || entry.Inputs == null || !entry.Inputs.SequenceEqual(m.SupportedInputs!))
            {
                entry.Name = m.Name;
                entry.Inputs = [.. m.SupportedInputs!];
                changed = true;
            }
        }
        if (!changed || !ConfigStore.Exists)
            return;
        try
        {
            // Nur den Cache in der gespeicherten Datei aktualisieren – ungespeicherte Änderungen bleiben unberührt.
            var onDisk = ConfigStore.Load();
            onDisk.MonitorCache = _config.MonitorCache.Select(c => new MonitorCacheEntry { Id = c.Id, Name = c.Name, Inputs = c.Inputs?.ToList() }).ToList();
            ConfigStore.Save(onDisk);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Monitor-Cache speichern");
        }
    }

    // ------------------------------------------------------------------ Speichern / Status

    private void MarkDirty()
    {
        if (_populating)
            return;
        _dirty = true;
        UpdateButtons();
    }

    private bool Save()
    {
        try
        {
            _config.ShowErrorNotifications = _notifications.Checked;
            ConfigStore.Save(_config);
            if (_autostart.Checked != Autostart.IsEnabled())
                Autostart.SetEnabled(_autostart.Checked);

            _saved = true;
            _dirty = false;
            UpdateButtons();
            SetStatus("Gespeichert. " + AgentHint());
            return true;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Speichern");
            Ui.ShowError(this, "Speichern fehlgeschlagen", ex.Message);
            return false;
        }
    }

    private string AgentHint() => _agentWasRunning
        ? "Die Tastenkombinationen sind deaktiviert, solange dieses Fenster geöffnet ist, und werden beim Schließen wieder aktiv."
        : "Der Hintergrundprozess läuft nicht – er wird nach dem Speichern beim Schließen dieses Fensters gestartet.";

    // Erst beim Schließen starten: Ein laufender Agent würde seine eigenen Hotkeys belegen
    // und die Konfliktprüfung in diesem Fenster verfälschen.
    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        base.OnFormClosed(e);
        if (_saved && !_agentWasRunning && !AgentIpc.IsAgentRunning)
        {
            try { Ui.StartAgent(); }
            catch (Exception ex) { Log.Error(ex, "Hintergrundprozess starten"); }
        }
    }

    private void UpdateStatus() => SetStatus(AgentHint());

    private void SetStatus(string text) => _status.Text = text;
}
