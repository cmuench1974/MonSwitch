using MonSwitch.Core.Native;

namespace MonSwitch.Core;

/// <summary>
/// Kommunikation zwischen Einstellungen und Hintergrundprozess über Fensternachrichten
/// an das (unsichtbare) Agent-Fenster – kein Dienst, keine Pipes, keine Polling-Schleifen.
/// </summary>
public static class AgentIpc
{
    public const string WindowClass = "MonSwitch.AgentWindow";

    private const uint WM_APP = 0x8000;

    /// <summary>Einstellungen geöffnet: Hotkeys freigeben (wParam = Prozess-ID der Einstellungen).</summary>
    public const uint MsgSuspend = WM_APP + 10;

    /// <summary>Einstellungen geschlossen: Konfiguration neu laden und Hotkeys wieder registrieren.</summary>
    public const uint MsgResume = WM_APP + 11;

    /// <summary>Konfiguration wurde gespeichert: neu laden.</summary>
    public const uint MsgReload = WM_APP + 12;

    /// <summary>Einstellungen öffnen (z. B. bei erneutem Start von MonSwitch.exe).</summary>
    public const uint MsgShowSettings = WM_APP + 13;

    public static nint FindAgentWindow() => User32.FindWindow(WindowClass, null);

    public static bool IsAgentRunning => FindAgentWindow() != 0;

    public static bool Send(uint message, nint wParam = 0)
    {
        nint hwnd = FindAgentWindow();
        if (hwnd == 0)
            return false;
        return User32.SendMessageTimeout(hwnd, message, wParam, 0, User32.SMTO_ABORTIFHUNG, 5000, out _) != 0;
    }

    public static bool Post(uint message, nint wParam = 0)
    {
        nint hwnd = FindAgentWindow();
        return hwnd != 0 && User32.PostMessage(hwnd, message, wParam, 0);
    }

    /// <summary>
    /// Bittet den laufenden Hintergrundprozess, die Einstellungen zu öffnen. Das Recht, ein Fenster in den
    /// Vordergrund zu holen, wird dabei weitergereicht (sonst blinkt das Fenster nur in der Taskleiste).
    /// </summary>
    public static bool RequestShowSettings()
    {
        User32.AllowSetForegroundWindow(unchecked((uint)-1)); // ASFW_ANY
        return Post(MsgShowSettings);
    }
}
