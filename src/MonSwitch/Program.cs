using MonSwitch.Core;
using MonSwitch.Core.Config;

namespace MonSwitch.Agent;

internal static class Program
{
    private const string MutexName = @"Local\MonSwitch.Agent";

    /// <summary>
    /// MonSwitch.exe            – startet den Hintergrundprozess (bzw. öffnet die Einstellungen, falls er schon läuft)
    /// MonSwitch.exe --exit     – beendet einen laufenden Hintergrundprozess
    /// </summary>
    [STAThread]
    private static int Main(string[] args)
    {
        bool exit = args.Any(a => a.Equals("--exit", StringComparison.OrdinalIgnoreCase) || a.Equals("/exit", StringComparison.OrdinalIgnoreCase));

        using var mutex = new Mutex(initiallyOwned: true, MutexName, out bool createdNew);
        if (!createdNew)
        {
            // Bereits aktiv: statt einer zweiten Instanz die Einstellungen öffnen bzw. beenden.
            if (exit)
                AgentIpc.Post(0x0010 /* WM_CLOSE */);
            else
                AgentIpc.RequestShowSettings();
            return 0;
        }
        if (exit)
            return 0;

        try
        {
            return new AgentApp(firstRun: !ConfigStore.Exists).Run();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Unbehandelter Fehler im Hintergrundprozess");
            return 1;
        }
    }
}
