using System.Diagnostics;
using MonSwitch.Core.Config;
using MonSwitch.Core.Hotkeys;
using MonSwitch.Core.Monitors;

namespace MonSwitch.Settings;

/// <summary>Monitor, wie er in den Einstellungen zur Auswahl steht (verbunden oder nur aus der Konfiguration bekannt).</summary>
internal sealed class MonitorChoice
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Label { get; init; }
    public bool Connected { get; init; }
    public bool? DdcAvailable { get; init; }
    public int? CurrentInput { get; init; }
    public required IReadOnlyList<int> Inputs { get; init; }
    public bool InputsFromMonitor { get; init; }

    public static List<MonitorChoice> Build(IReadOnlyList<DetectedMonitor> detected, AppConfig config)
    {
        var result = new List<MonitorChoice>();
        foreach (var d in detected)
        {
            var cached = config.MonitorCache.FirstOrDefault(c => string.Equals(c.Id, d.Id, StringComparison.OrdinalIgnoreCase));
            var inputs = d.SupportedInputs ?? cached?.Inputs;
            result.Add(new MonitorChoice
            {
                Id = d.Id,
                Name = d.Name,
                Label = d.Label,
                Connected = true,
                DdcAvailable = d.DdcAvailable,
                CurrentInput = d.CurrentInput,
                Inputs = inputs is { Count: > 0 } ? inputs : InputSources.Standard,
                InputsFromMonitor = inputs is { Count: > 0 },
            });
        }

        // In der Konfiguration verwendete, aktuell nicht verbundene Monitore weiterhin anbieten.
        foreach (var action in config.Bindings.SelectMany(b => b.Actions))
        {
            if (result.Any(r => string.Equals(r.Id, action.MonitorId, StringComparison.OrdinalIgnoreCase)))
                continue;
            var cached = config.MonitorCache.FirstOrDefault(c => string.Equals(c.Id, action.MonitorId, StringComparison.OrdinalIgnoreCase));
            result.Add(new MonitorChoice
            {
                Id = action.MonitorId,
                Name = action.MonitorName,
                Label = $"{action.MonitorName} (nicht verbunden)",
                Connected = false,
                Inputs = cached?.Inputs is { Count: > 0 } ci ? ci : InputSources.Standard,
                InputsFromMonitor = cached?.Inputs is { Count: > 0 },
            });
        }
        return result;
    }
}

internal static class Ui
{
    public static bool IsDark => Application.IsDarkModeEnabled;

    public static Color OkColor => IsDark ? Color.FromArgb(120, 220, 140) : Color.FromArgb(0, 120, 40);

    public static Color WarningColor => IsDark ? Color.FromArgb(255, 200, 90) : Color.FromArgb(170, 85, 0);

    public static Color ErrorColor => IsDark ? Color.FromArgb(255, 120, 120) : Color.FromArgb(190, 20, 20);

    public static Color HintColor => IsDark ? Color.FromArgb(170, 170, 170) : Color.FromArgb(90, 90, 90);

    public static Icon? AppIcon { get; } = LoadIcon();

    private static Icon? LoadIcon()
    {
        using var stream = typeof(Ui).Assembly.GetManifestResourceStream("MonSwitch.ico");
        return stream == null ? null : new Icon(stream);
    }

    public static void ShowError(IWin32Window? owner, string heading, string text)
    {
        TaskDialog.ShowDialog(owner?.Handle ?? 0, new TaskDialogPage
        {
            Caption = "MonSwitch",
            Heading = heading,
            Text = text,
            Icon = TaskDialogIcon.Error,
            Buttons = { TaskDialogButton.OK },
        });
    }

    /// <summary>
    /// Zeigt gefundene Konflikte an. Die Einstellung wird nur übernommen, wenn der Benutzer dies ausdrücklich bestätigt.
    /// </summary>
    public static bool ConfirmConflicts(IWin32Window owner, Hotkey hotkey, IReadOnlyList<HotkeyConflict> conflicts)
    {
        bool proven = conflicts.Any(c => c.Level == ConflictLevel.Conflict);
        var confirm = new TaskDialogButton("Trotzdem übernehmen");
        var back = new TaskDialogButton("Andere Kombination wählen");

        var page = new TaskDialogPage
        {
            Caption = "MonSwitch – Konflikt mit anderer Tastenkombination",
            Heading = proven
                ? $"„{hotkey}“ wird bereits verwendet"
                : $"„{hotkey}“ ist möglicherweise bereits belegt",
            Text = string.Join("\n", conflicts.Select(c => $"• {c.Source}: {c.Description}"))
                + "\n\nWenn Sie die Kombination trotzdem verwenden, funktioniert entweder die andere Funktion nicht mehr "
                + "oder MonSwitch reagiert nicht auf die Tastenkombination.\n\nMöchten Sie die Tastenkombination trotzdem übernehmen?",
            Icon = TaskDialogIcon.Warning,
            Buttons = { confirm, back },
            DefaultButton = back,
            AllowCancel = true,
        };
        return TaskDialog.ShowDialog(owner, page) == confirm;
    }

    public static bool ConfirmSwitch(IWin32Window owner, HotkeyBinding binding)
    {
        var run = new TaskDialogButton("Jetzt umschalten");
        var page = new TaskDialogPage
        {
            Caption = "MonSwitch",
            Heading = "Eingang jetzt umschalten?",
            Text = binding.DescribeActions().Replace("; ", "\n", StringComparison.Ordinal)
                + "\n\nWird auf den Eingang eines anderen Geräts umgeschaltet, zeigt der Monitor dieses Fenster nicht mehr an. "
                + "Zurück geht es über das Monitormenü oder eine Tastenkombination auf dem anderen Gerät.",
            Icon = TaskDialogIcon.Information,
            Buttons = { run, TaskDialogButton.Cancel },
            DefaultButton = run,
        };
        return TaskDialog.ShowDialog(owner, page) == run;
    }

    public static string FormatConflictSummary(IReadOnlyList<HotkeyConflict> conflicts, int maxLines = 4)
    {
        if (conflicts.Count == 0)
            return "✓ Keine Konflikte gefunden.";
        string head = conflicts[0].Level == ConflictLevel.Blocking
            ? "✗ Nicht möglich:"
            : conflicts.Count == 1 ? "⚠ Möglicher Konflikt:" : $"⚠ {conflicts.Count} mögliche Konflikte:";
        var lines = conflicts.Take(maxLines).Select(c => $"• {c.Source}: {c.Description}").ToList();
        if (conflicts.Count > maxLines)
            lines.Add($"• … und {conflicts.Count - maxLines} weitere");
        return head + "\n" + string.Join("\n", lines);
    }

    public static Color ConflictColor(IReadOnlyList<HotkeyConflict> conflicts) =>
        conflicts.Count == 0 ? OkColor
        : conflicts[0].Level == ConflictLevel.Blocking ? ErrorColor
        : WarningColor;

    public static void StartAgent()
    {
        string path = MonSwitch.Core.Autostart.AgentPath;
        if (File.Exists(path))
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(path) })?.Dispose();
    }
}
