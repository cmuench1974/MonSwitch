using MonSwitch.Core.Config;

namespace MonSwitch.Core.Monitors;

public sealed record SwitchResult(bool Success, IReadOnlyList<string> Errors, IReadOnlyList<string> Done);

/// <summary>Führt die Monitoraktionen einer Tastenkombination per DDC/CI aus.</summary>
public static class SwitchExecutor
{
    /// <summary>Alle DDC/CI-Zugriffe eines Prozesses laufen seriell (der I²C-Bus verträgt keine parallelen Befehle).</summary>
    public static readonly Lock DdcLock = new();

    // Zuletzt gesetzter Eingang je Monitor – Fallback fürs Umschalten, falls der Monitor den aktuellen Wert nicht liefert.
    private static readonly Dictionary<string, int> LastSet = new(StringComparer.OrdinalIgnoreCase);

    public static SwitchResult Execute(HotkeyBinding binding)
    {
        var errors = new List<string>();
        var done = new List<string>();

        lock (DdcLock)
        {
            using var set = PhysicalMonitorSet.Open();
            foreach (var action in binding.Actions)
            {
                var monitor = set.Find(action.MonitorId, action.MonitorName);
                if (monitor == null)
                {
                    errors.Add($"Monitor „{action.MonitorName}“ wurde nicht gefunden.");
                    continue;
                }

                int target = action.InputA;
                if (binding.Mode == BindingMode.Toggle && action.InputB is int inputB)
                {
                    int? current = monitor.TryGetInput(out int cur) ? cur
                        : LastSet.TryGetValue(monitor.Id, out int last) ? last
                        : null;
                    target = current is int c && InputSources.Matches(c, action.InputA) ? inputB : action.InputA;
                }

                if (UseLgCommand(action.Method, monitor))
                {
                    if (LgInput.TrySet(monitor, target, out string? error))
                    {
                        LastSet[monitor.Id] = target;
                        done.Add($"{monitor.Name}: {InputSources.GetName(target)} (LG-Befehl)");
                    }
                    else
                    {
                        errors.Add($"„{monitor.Name}“: {error}");
                    }
                }
                else if (monitor.TrySetInput(target))
                {
                    LastSet[monitor.Id] = target;
                    done.Add($"{monitor.Name}: {InputSources.GetName(target)}");
                }
                else
                {
                    errors.Add($"„{monitor.Name}“ hat den Befehl nicht angenommen (DDC/CI im Monitormenü aktiviert?).");
                }
            }
        }

        foreach (var e in errors)
            Log.Error($"Umschalten ({binding.Hotkey}): {e}");
        return new SwitchResult(errors.Count == 0, errors, done);
    }

    /// <summary>Automatisch: LG-Befehl für LG-Monitore, sofern der Grafiktreiber ihn senden kann.</summary>
    private static bool UseLgCommand(SwitchMethod method, PhysicalMonitor monitor) => method switch
    {
        SwitchMethod.Lg => true,
        SwitchMethod.Standard => false,
        _ => LgInput.IsLgMonitor(monitor.Id) && LgInput.IsTransportAvailable,
    };
}
