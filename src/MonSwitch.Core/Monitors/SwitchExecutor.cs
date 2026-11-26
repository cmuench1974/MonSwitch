using System.Globalization;
using MonSwitch.Core.Config;

namespace MonSwitch.Core.Monitors;

public sealed record SwitchResult(bool Success, IReadOnlyList<string> Errors, IReadOnlyList<string> Done);

/// <summary>Führt die Monitoraktionen einer Tastenkombination per DDC/CI aus.</summary>
public static class SwitchExecutor
{
    /// <summary>Alle DDC/CI-Zugriffe eines Prozesses laufen seriell (der I²C-Bus verträgt keine parallelen Befehle).</summary>
    public static readonly Lock DdcLock = new();

    public static SwitchResult Execute(HotkeyBinding binding)
    {
        var errors = new List<string>();
        var done = new List<string>();

        lock (DdcLock)
        {
            var lastSet = LastInputStore.Load();
            bool lastSetChanged = false;

            using var set = PhysicalMonitorSet.Open();
            foreach (var action in binding.Actions)
            {
                var monitor = set.Find(action.MonitorId, action.MonitorName);
                if (monitor == null)
                {
                    errors.Add($"Monitor „{action.MonitorName}“ wurde nicht gefunden.");
                    continue;
                }

                bool lg = UseLgCommand(action.Method, monitor);
                int target = action.InputA;
                if (binding.Mode == BindingMode.Toggle && action.InputB != null)
                {
                    int? remembered = lastSet.TryGetValue(monitor.Id, out int r) ? r : null;
                    // Beim LG-Befehl ist der gemerkte Eingang maßgeblich – das Lesen spart man sich dann.
                    int? read = lg && remembered != null ? null
                        : monitor.TryGetInput(out int current) ? current : null;
                    target = ChooseTarget(action, binding.Mode, lg, read, remembered);
                }

                string? lgError = null;
                bool ok = lg ? LgInput.TrySet(monitor, target, out lgError) : monitor.TrySetInput(target);
                if (ok)
                {
                    lastSet[monitor.Id] = target;
                    lastSetChanged = true;
                    done.Add($"{monitor.Name}: {InputSources.GetName(target)}{(lg ? " (LG-Befehl)" : "")}");
                }
                else
                {
                    errors.Add(lg
                        ? $"„{monitor.Name}“: {lgError}"
                        : $"„{monitor.Name}“ hat den Befehl nicht angenommen (DDC/CI im Monitormenü aktiviert?).");
                }
            }

            if (lastSetChanged)
                LastInputStore.Save(lastSet);
        }

        foreach (var e in errors)
            Log.Error($"Umschalten ({binding.Hotkey}): {e}");
        return new SwitchResult(errors.Count == 0, errors, done);
    }

    /// <summary>
    /// Zieleingang einer Aktion. Beim Hin- und Herschalten entscheidet der aktuelle Eingang: Standardmonitore melden
    /// ihn zuverlässig über VCP 0x60. Monitore, die per LG-Befehl umgeschaltet werden, melden dort dagegen weiter
    /// den alten Eingang (LG ULTRAGEAR+: „DisplayPort 1“, während HDMI 2 angezeigt wird) – dort zählt daher der
    /// zuletzt von MonSwitch gesetzte Eingang.
    /// </summary>
    internal static int ChooseTarget(MonitorAction action, BindingMode mode, bool lgCommand, int? readInput, int? lastSetInput)
    {
        if (mode != BindingMode.Toggle || action.InputB is not int inputB)
            return action.InputA;
        int? current = lgCommand ? lastSetInput ?? readInput : readInput ?? lastSetInput;
        return current is int c && InputSources.Matches(c, action.InputA) ? inputB : action.InputA;
    }

    /// <summary>Automatisch: LG-Befehl für LG-Monitore, sofern der Grafiktreiber ihn senden kann.</summary>
    private static bool UseLgCommand(SwitchMethod method, PhysicalMonitor monitor) => method switch
    {
        SwitchMethod.Lg => true,
        SwitchMethod.Standard => false,
        _ => LgInput.IsLgMonitor(monitor.Id) && LgInput.IsTransportAvailable,
    };
}

/// <summary>
/// Zuletzt von MonSwitch gesetzter Eingang je Monitor. Liegt in einer Datei, damit Hintergrundprozess und
/// Einstellungen („Jetzt testen“) denselben Stand sehen und er einen Neustart übersteht.
/// Format: eine Zeile je Monitor – Eingang (hex) TAB Monitor-ID.
/// </summary>
internal static class LastInputStore
{
    public static string FilePath { get; } = Path.Combine(Path.GetDirectoryName(Log.FilePath)!, "lastinput.txt");

    public static Dictionary<string, int> Load()
    {
        var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (string line in File.ReadLines(FilePath))
            {
                int tab = line.IndexOf('\t');
                if (tab > 0 && int.TryParse(line.AsSpan(0, tab), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out int input))
                    result[line[(tab + 1)..]] = input;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Noch kein Stand gespeichert oder nicht lesbar: dann entscheidet der vom Monitor gemeldete Eingang.
        }
        return result;
    }

    public static void Save(Dictionary<string, int> values)
    {
        string temp = $"{FilePath}.{Environment.ProcessId}.tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllLines(temp, values.Select(kv => $"{kv.Value:X2}\t{kv.Key}"));
            File.Move(temp, FilePath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error(ex, "Zuletzt gesetzte Eingänge speichern");
        }
    }
}
