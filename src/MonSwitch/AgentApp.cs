using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using MonSwitch.Core;
using MonSwitch.Core.Config;
using MonSwitch.Core.Monitors;

namespace MonSwitch.Agent;

/// <summary>
/// Hintergrundprozess: unsichtbares Fenster + Nachrichtenschleife. Im Leerlauf blockiert GetMessage –
/// es gibt keine Timer, kein Polling und keine offenen Monitor-Handles.
/// </summary>
internal sealed unsafe class AgentApp
{
    private const uint WM_TRAY = Win32.WM_APP + 1;
    private const uint WM_SWITCH_DONE = Win32.WM_APP + 2;
    private const uint WM_SETTINGS_EXITED = Win32.WM_APP + 3;

    private const nuint TimerTrim = 1;

    private const int CmdSettings = 1;
    private const int CmdPause = 2;
    private const int CmdAutostart = 3;
    private const int CmdExit = 4;
    private const int CmdRunBindingBase = 1000;

    private static AgentApp? s_instance;
    private static nint s_hwnd;

    private readonly Dictionary<int, HotkeyBinding> _registered = [];
    private readonly ConcurrentQueue<(HotkeyBinding Binding, SwitchResult Result)> _results = new();
    private readonly bool _firstRun;

    private nint _hwnd;
    private TrayIcon _tray = null!;
    private AppConfig _config = new();
    private uint _taskbarCreated;
    private nint _iconNormal, _iconPaused;
    private bool _paused;
    private int _busy;
    private int _failedRegistrations;

    // Solange die Einstellungen geöffnet sind, gibt der Agent seine Hotkeys frei (für die Konfliktprüfung).
    private uint _settingsPid;
    private nint _settingsProcess;
    private nint _settingsWait;

    public AgentApp(bool firstRun) => _firstRun = firstRun;

    private bool HotkeysActive => !_paused && _settingsPid == 0;

    public int Run()
    {
        s_instance = this;
        Efficiency.Apply();
        Efficiency.EnableDarkMenus();

        nint hInstance = Win32.GetModuleHandle(0);
        fixed (char* className = AgentIpc.WindowClass)
        {
            var wc = new WNDCLASSEXW
            {
                cbSize = (uint)sizeof(WNDCLASSEXW),
                lpfnWndProc = &WndProc,
                hInstance = hInstance,
                lpszClassName = className,
            };
            if (Win32.RegisterClassEx(&wc) == 0)
            {
                Log.Error($"RegisterClassEx fehlgeschlagen ({Marshal.GetLastPInvokeError()}).");
                return 1;
            }
        }

        // Unsichtbares Top-Level-Fenster (kein Message-Only-Fenster), damit „TaskbarCreated“ ankommt.
        _hwnd = Win32.CreateWindowEx(Win32.WS_EX_TOOLWINDOW, AgentIpc.WindowClass, "MonSwitch", Win32.WS_POPUP, 0, 0, 0, 0, 0, 0, hInstance, 0);
        if (_hwnd == 0)
        {
            Log.Error($"CreateWindowEx fehlgeschlagen ({Marshal.GetLastPInvokeError()}).");
            return 1;
        }
        s_hwnd = _hwnd;

        _taskbarCreated = Win32.RegisterWindowMessage("TaskbarCreated");
        _tray = new TrayIcon(_hwnd, WM_TRAY);
        LoadIcons();

        _config = ConfigStore.Load();
        if (_firstRun)
            InitializeFirstRun();

        RegisterHotkeys(notify: true);
        _tray.Add();
        UpdateTray();

        if (_firstRun)
        {
            _tray.ShowBalloon("MonSwitch läuft im Hintergrund",
                "Klicken Sie auf das Symbol im Infobereich, um Tastenkombinationen für Ihre Monitore festzulegen.", warning: false);
            OpenSettings();
        }

        ScheduleTrim(2000);

        MSG msg;
        while (Win32.GetMessage(&msg, 0, 0, 0) > 0)
        {
            Win32.TranslateMessage(&msg);
            Win32.DispatchMessage(&msg);
        }

        Cleanup();
        return 0;
    }

    private void InitializeFirstRun()
    {
        try
        {
            ConfigStore.Save(_config);
            Autostart.SetEnabled(true);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Ersteinrichtung");
        }
    }

    // ------------------------------------------------------------------ Fensterprozedur

    [UnmanagedCallersOnly]
    private static nint WndProc(nint hwnd, uint msg, nint wParam, nint lParam)
    {
        try
        {
            if (s_instance != null && s_instance._hwnd == hwnd)
                return s_instance.HandleMessage(hwnd, msg, wParam, lParam);
        }
        catch (Exception ex)
        {
            // Ausnahmen dürfen niemals in nativen Code durchschlagen.
            Log.Error(ex, $"Nachricht 0x{msg:X4}");
        }
        return Win32.DefWindowProc(hwnd, msg, wParam, lParam);
    }

    private nint HandleMessage(nint hwnd, uint msg, nint wParam, nint lParam)
    {
        switch (msg)
        {
            case Win32.WM_HOTKEY:
                if (_registered.TryGetValue((int)wParam, out var binding))
                    StartSwitch(binding);
                return 0;

            case WM_TRAY:
                OnTrayMessage(wParam, lParam);
                return 0;

            case WM_SWITCH_DONE:
                OnSwitchDone();
                return 0;

            case AgentIpc.MsgSuspend:
                BeginSettingsSession((uint)wParam);
                return 1;

            case AgentIpc.MsgResume:
                EndSettingsSession();
                return 1;

            case WM_SETTINGS_EXITED:
                if (_settingsPid != 0)
                    EndSettingsSession();
                return 0;

            case AgentIpc.MsgReload:
                ReloadConfig();
                return 1;

            case AgentIpc.MsgShowSettings:
                OpenSettings();
                return 1;

            case Win32.WM_TIMER:
                if ((nuint)wParam == TimerTrim)
                {
                    Win32.KillTimer(hwnd, TimerTrim);
                    Efficiency.Trim();
                }
                return 0;

            case Win32.WM_SETTINGCHANGE:
                if (lParam != 0 && new string((char*)lParam) == "ImmersiveColorSet")
                    Efficiency.FlushMenuThemes();
                break;

            case Win32.WM_DISPLAYCHANGE:
            case Win32.WM_DPICHANGED:
                LoadIcons();
                UpdateTray();
                break;

            case Win32.WM_QUERYENDSESSION:
                return 1;

            case Win32.WM_ENDSESSION:
                if (wParam != 0)
                    Cleanup();
                return 0;

            case Win32.WM_CLOSE:
                Win32.DestroyWindow(hwnd);
                return 0;

            case Win32.WM_DESTROY:
                Win32.PostQuitMessage(0);
                return 0;
        }

        if (msg == _taskbarCreated && _taskbarCreated != 0)
        {
            _tray.Add();
            UpdateTray();
            return 0;
        }

        return Win32.DefWindowProc(hwnd, msg, wParam, lParam);
    }

    // ------------------------------------------------------------------ Hotkeys

    private void RegisterHotkeys(bool notify)
    {
        UnregisterHotkeys();
        _failedRegistrations = 0;
        if (!HotkeysActive)
            return;

        var failures = new List<string>();
        int id = 1;
        foreach (var binding in _config.Bindings)
        {
            if (!binding.Enabled || !binding.Hotkey.IsValid || binding.Actions.Count == 0)
                continue;
            if (Win32.RegisterHotKey(_hwnd, id, binding.Modifiers | 0x4000 /* MOD_NOREPEAT */, binding.Key))
            {
                _registered[id] = binding;
            }
            else
            {
                int error = Marshal.GetLastPInvokeError();
                string reason = error == 1409 ? "bereits durch ein anderes Programm belegt" : $"Fehler {error}";
                failures.Add($"{binding.Hotkey} ({reason})");
                Log.Error($"Tastenkombination {binding.Hotkey} konnte nicht registriert werden: {reason}.");
            }
            id++;
        }

        _failedRegistrations = failures.Count;
        if (notify && failures.Count > 0 && _config.ShowErrorNotifications)
            _tray.ShowBalloon("Tastenkombination nicht verfügbar", string.Join("\n", failures), warning: true);
    }

    private void UnregisterHotkeys()
    {
        foreach (int id in _registered.Keys)
            Win32.UnregisterHotKey(_hwnd, id);
        _registered.Clear();
    }

    private void ReloadConfig()
    {
        _config = ConfigStore.Load();
        RegisterHotkeys(notify: true);
        UpdateTray();
        ScheduleTrim(3000);
    }

    // ------------------------------------------------------------------ Umschalten

    private void StartSwitch(HotkeyBinding binding)
    {
        // Ein Umschaltvorgang zur Zeit – weitere Tastendrücke währenddessen werden verworfen.
        if (Interlocked.Exchange(ref _busy, 1) == 1)
            return;

        ThreadPool.UnsafeQueueUserWorkItem(static state =>
        {
            var (app, b) = state;
            SwitchResult result;
            try
            {
                result = SwitchExecutor.Execute(b);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Umschalten");
                result = new SwitchResult(false, [ex.Message], []);
            }
            app._results.Enqueue((b, result));
            Volatile.Write(ref app._busy, 0);
            Win32.PostMessage(s_hwnd, WM_SWITCH_DONE, 0, 0);
        }, (this, binding), preferLocal: false);
    }

    private void OnSwitchDone()
    {
        while (_results.TryDequeue(out var item))
        {
            if (!item.Result.Success && _config.ShowErrorNotifications)
                _tray.ShowBalloon($"Umschalten fehlgeschlagen ({item.Binding.Hotkey})", string.Join("\n", item.Result.Errors), warning: true);
        }
        ScheduleTrim(5000);
    }

    // ------------------------------------------------------------------ Einstellungen

    private void OpenSettings()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "MonSwitch.Settings.exe");
        if (!File.Exists(path))
        {
            _tray.ShowBalloon("Einstellungen nicht gefunden", path, warning: true);
            return;
        }
        Win32.AllowSetForegroundWindow(Win32.ASFW_ANY);
        nint rc = Win32.ShellExecute(_hwnd, null, path, null, AppContext.BaseDirectory, Win32.SW_SHOWNORMAL);
        if (rc <= 32)
            Log.Error($"Einstellungen konnten nicht gestartet werden (ShellExecute {rc}).");
    }

    private void BeginSettingsSession(uint pid)
    {
        if (_settingsPid != 0)
            ReleaseSettingsWait();

        _settingsPid = pid;
        UnregisterHotkeys();
        UpdateTray();

        // Falls die Einstellungen abstürzen, ohne „Resume“ zu senden: Prozessende überwachen (ohne Polling).
        _settingsProcess = Win32.OpenProcess(0x00100000 /* SYNCHRONIZE */, false, pid);
        if (_settingsProcess != 0
            && !Win32.RegisterWaitForSingleObject(out _settingsWait, _settingsProcess, &OnSettingsProcessExited, 0, 0xFFFFFFFF, 0x8 /* WT_EXECUTEONLYONCE */))
        {
            _settingsWait = 0;
        }
    }

    [UnmanagedCallersOnly]
    private static void OnSettingsProcessExited(nint context, byte timedOut) =>
        Win32.PostMessage(s_hwnd, WM_SETTINGS_EXITED, 0, 0);

    private void EndSettingsSession()
    {
        ReleaseSettingsWait();
        _settingsPid = 0;
        ReloadConfig();
    }

    private void ReleaseSettingsWait()
    {
        if (_settingsWait != 0)
        {
            Win32.UnregisterWait(_settingsWait);
            _settingsWait = 0;
        }
        if (_settingsProcess != 0)
        {
            Win32.CloseHandle(_settingsProcess);
            _settingsProcess = 0;
        }
    }

    // ------------------------------------------------------------------ Tray-Symbol und Menü

    private void OnTrayMessage(nint wParam, nint lParam)
    {
        switch ((uint)Win32.LoWord(lParam))
        {
            case Win32.NIN_SELECT:
            case Win32.NIN_KEYSELECT:
                OpenSettings();
                break;
            case Win32.WM_CONTEXTMENU:
                ShowMenu(Win32.SignedLoWord(wParam), Win32.SignedHiWord(wParam));
                break;
        }
    }

    private void ShowMenu(int x, int y)
    {
        nint menu = Win32.CreatePopupMenu();
        nint runMenu = 0;
        try
        {
            Win32.AppendMenu(menu, Win32.MF_STRING, CmdSettings, "&Einstellungen…");
            Win32.SetMenuDefaultItem(menu, CmdSettings, 0);

            var runnable = _config.Bindings.Where(b => b.Actions.Count > 0).ToList();
            if (runnable.Count > 0)
            {
                runMenu = Win32.CreatePopupMenu();
                for (int i = 0; i < runnable.Count; i++)
                {
                    var b = runnable[i];
                    string text = $"{b.DisplayName}\t{b.Hotkey}".Replace("&", "&&", StringComparison.Ordinal);
                    Win32.AppendMenu(runMenu, Win32.MF_STRING, CmdRunBindingBase + i, text);
                }
                Win32.AppendMenu(menu, Win32.MF_POPUP, runMenu, "&Jetzt umschalten");
            }

            Win32.AppendMenu(menu, Win32.MF_SEPARATOR, 0, null);
            Win32.AppendMenu(menu, Win32.MF_STRING | (_paused ? Win32.MF_CHECKED : 0), CmdPause, "Tastenkombinationen &pausieren");
            bool autostart = false;
            try { autostart = Autostart.IsEnabled(); } catch (Exception ex) { Log.Error(ex, "Autostart lesen"); }
            Win32.AppendMenu(menu, Win32.MF_STRING | (autostart ? Win32.MF_CHECKED : 0), CmdAutostart, "Mit &Windows starten");
            Win32.AppendMenu(menu, Win32.MF_SEPARATOR, 0, null);
            Win32.AppendMenu(menu, Win32.MF_STRING, CmdExit, "&Beenden");

            // Notwendig, damit sich das Menü beim Klick daneben schließt (KB135788).
            Win32.SetForegroundWindow(_hwnd);
            int cmd = Win32.TrackPopupMenuEx(menu, Win32.TPM_RIGHTBUTTON | Win32.TPM_BOTTOMALIGN | Win32.TPM_RETURNCMD | Win32.TPM_NONOTIFY, x, y, _hwnd, 0);
            Win32.PostMessage(_hwnd, Win32.WM_NULL, 0, 0);

            switch (cmd)
            {
                case CmdSettings:
                    OpenSettings();
                    break;
                case CmdPause:
                    _paused = !_paused;
                    RegisterHotkeys(notify: !_paused);
                    UpdateTray();
                    break;
                case CmdAutostart:
                    try { Autostart.SetEnabled(!autostart); } catch (Exception ex) { Log.Error(ex, "Autostart setzen"); }
                    break;
                case CmdExit:
                    Win32.DestroyWindow(_hwnd);
                    break;
                case >= CmdRunBindingBase when cmd - CmdRunBindingBase < runnable.Count:
                    StartSwitch(runnable[cmd - CmdRunBindingBase]);
                    break;
            }
        }
        finally
        {
            // Untermenüs werden mit dem Hauptmenü zerstört.
            Win32.DestroyMenu(menu);
        }
        ScheduleTrim(5000);
    }

    private void LoadIcons()
    {
        int size = Win32.GetSystemMetricsForDpi(Win32.SM_CXSMICON, Win32.GetDpiForSystem());
        if (size <= 0)
            size = 16;
        nint normal = IconLoader.Load("MonSwitch.ico", size);
        nint paused = IconLoader.Load("MonSwitchPaused.ico", size);
        if (_iconNormal != 0) Win32.DestroyIcon(_iconNormal);
        if (_iconPaused != 0) Win32.DestroyIcon(_iconPaused);
        _iconNormal = normal;
        _iconPaused = paused;
    }

    private void UpdateTray()
    {
        string status;
        if (_paused)
            status = "Tastenkombinationen pausiert";
        else if (_settingsPid != 0)
            status = "Einstellungen geöffnet";
        else if (_registered.Count == 0 && _failedRegistrations == 0)
            status = "Keine Tastenkombinationen eingerichtet";
        else
        {
            status = _registered.Count == 1 ? "1 Tastenkombination aktiv" : $"{_registered.Count} Tastenkombinationen aktiv";
            if (_failedRegistrations > 0)
                status += $"\n{_failedRegistrations} nicht verfügbar (belegt)";
        }
        _tray.Update(_paused ? _iconPaused : _iconNormal, "MonSwitch\n" + status);
    }

    private void ScheduleTrim(uint delayMs) => Win32.SetTimer(_hwnd, TimerTrim, delayMs, 0);

    private void Cleanup()
    {
        UnregisterHotkeys();
        ReleaseSettingsWait();
        _tray?.Remove();
    }
}
