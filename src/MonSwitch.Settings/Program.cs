using System.Diagnostics;
using System.Runtime.InteropServices;
using MonSwitch.Core;

namespace MonSwitch.Settings;

internal static partial class Program
{
    private const string MutexName = @"Local\MonSwitch.Settings";

    [STAThread]
    private static void Main()
    {
        using var mutex = new Mutex(initiallyOwned: true, MutexName, out bool createdNew);
        if (!createdNew)
        {
            ActivateExistingInstance();
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.SetColorMode(SystemColorMode.System);
        Application.ThreadException += (_, e) =>
        {
            Log.Error(e.Exception, "Einstellungen");
            Ui.ShowError(null, "Unerwarteter Fehler", e.Exception.Message);
        };

        // Der Hintergrundprozess gibt seine Hotkeys frei, solange dieses Fenster offen ist –
        // nur so erkennt die Konfliktprüfung zuverlässig Belegungen durch andere Programme.
        bool agentRunning = AgentIpc.Send(AgentIpc.MsgSuspend, Environment.ProcessId);
        try
        {
            Application.Run(new MainForm(agentRunning));
        }
        finally
        {
            if (agentRunning)
                AgentIpc.Send(AgentIpc.MsgResume);
        }
    }

    private static void ActivateExistingInstance()
    {
        using var current = Process.GetCurrentProcess();
        foreach (var process in Process.GetProcessesByName(current.ProcessName))
        {
            using (process)
            {
                if (process.Id == current.Id || process.MainWindowHandle == 0)
                    continue;
                if (IsIconic(process.MainWindowHandle))
                    ShowWindow(process.MainWindowHandle, 9 /* SW_RESTORE */);
                SetForegroundWindow(process.MainWindowHandle);
            }
        }
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetForegroundWindow(nint hWnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ShowWindow(nint hWnd, int nCmdShow);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsIconic(nint hWnd);
}
