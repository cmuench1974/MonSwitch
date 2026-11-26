using Microsoft.Win32;

namespace MonSwitch.Core;

/// <summary>Autostart bei der Anmeldung über HKCU\...\Run (keine Administratorrechte nötig).</summary>
public static class Autostart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "MonSwitch";

    /// <summary>Pfad der Agent-EXE, die beim Anmelden gestartet wird (liegt neben der aktuellen EXE).</summary>
    public static string AgentPath => Path.Combine(AppContext.BaseDirectory, "MonSwitch.exe");

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: false);
        return key?.GetValue(ValueName) is string { Length: > 0 };
    }

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
        if (enabled)
            key.SetValue(ValueName, $"\"{AgentPath}\"", RegistryValueKind.String);
        else if (key.GetValue(ValueName) != null)
            key.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
