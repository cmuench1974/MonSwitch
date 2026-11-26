namespace MonSwitch.Core.Monitors;

/// <summary>Ergebnis der Monitorerkennung für die Einstellungen.</summary>
public sealed class DetectedMonitor
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string GdiDeviceName { get; init; }
    public bool IsPrimary { get; init; }

    public string Label => PhysicalMonitor.FormatLabel(Name, GdiDeviceName);

    /// <summary>null = noch nicht gelesen.</summary>
    public bool? DdcAvailable { get; set; }

    public int? CurrentInput { get; set; }

    /// <summary>Laut Capabilities unterstützte Eingänge; null = unbekannt.</summary>
    public List<int>? SupportedInputs { get; set; }

    public bool CapabilitiesRead { get; set; }
}

public static class MonitorDiscovery
{
    /// <summary>
    /// Erkennt alle Monitore und liest aktuellen Eingang sowie (optional) die Capabilities.
    /// <paramref name="progress"/> wird nach jedem Schritt aufgerufen, damit die Oberfläche schrittweise aktualisiert werden kann.
    /// </summary>
    public static List<DetectedMonitor> Detect(bool readCapabilities, Action<IReadOnlyList<DetectedMonitor>>? progress = null, CancellationToken cancel = default)
    {
        var result = new List<DetectedMonitor>();
        lock (SwitchExecutor.DdcLock)
        {
            using var set = PhysicalMonitorSet.Open();
            foreach (var m in set.Monitors)
            {
                result.Add(new DetectedMonitor
                {
                    Id = m.Id,
                    Name = m.Name,
                    GdiDeviceName = m.GdiDeviceName,
                    IsPrimary = m.IsPrimary,
                });
            }
            progress?.Invoke(result);

            for (int i = 0; i < set.Monitors.Count; i++)
            {
                cancel.ThrowIfCancellationRequested();
                var m = set.Monitors[i];
                var d = result[i];
                if (m.TryGetInput(out int input))
                {
                    d.CurrentInput = input;
                    d.DdcAvailable = true;
                }
                progress?.Invoke(result);
            }

            if (readCapabilities)
            {
                for (int i = 0; i < set.Monitors.Count; i++)
                {
                    cancel.ThrowIfCancellationRequested();
                    var m = set.Monitors[i];
                    var d = result[i];
                    string? caps = m.TryGetCapabilities();
                    d.CapabilitiesRead = true;
                    if (caps != null)
                    {
                        d.DdcAvailable = true;
                        d.SupportedInputs = CapabilitiesParser.GetVcpValues(caps, InputSources.VcpCode);
                    }
                    d.DdcAvailable ??= false;
                    progress?.Invoke(result);
                }
            }
            else
            {
                foreach (var d in result)
                    d.DdcAvailable ??= false;
            }
        }
        return result;
    }
}
