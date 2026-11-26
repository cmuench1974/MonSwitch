using System.Runtime.InteropServices;

namespace MonSwitch.Agent;

[StructLayout(LayoutKind.Sequential)]
internal struct POINT
{
    public int X, Y;
}

[StructLayout(LayoutKind.Sequential)]
internal struct MSG
{
    public nint hwnd;
    public uint message;
    public nint wParam;
    public nint lParam;
    public uint time;
    public POINT pt;
    public uint lPrivate;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct WNDCLASSEXW
{
    public uint cbSize;
    public uint style;
    public delegate* unmanaged<nint, uint, nint, nint, nint> lpfnWndProc;
    public int cbClsExtra;
    public int cbWndExtra;
    public nint hInstance;
    public nint hIcon;
    public nint hCursor;
    public nint hbrBackground;
    public char* lpszMenuName;
    public char* lpszClassName;
    public nint hIconSm;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct NOTIFYICONDATAW
{
    public uint cbSize;
    public nint hWnd;
    public uint uID;
    public uint uFlags;
    public uint uCallbackMessage;
    public nint hIcon;
    public fixed char szTip[128];
    public uint dwState;
    public uint dwStateMask;
    public fixed char szInfo[256];
    public uint uVersion;
    public fixed char szInfoTitle[64];
    public uint dwInfoFlags;
    public Guid guidItem;
    public nint hBalloonIcon;
}

[StructLayout(LayoutKind.Sequential)]
internal struct PROCESS_POWER_THROTTLING_STATE
{
    public uint Version;
    public uint ControlMask;
    public uint StateMask;
}

internal static unsafe partial class Win32
{
    // Fensternachrichten
    public const uint WM_NULL = 0x0000;
    public const uint WM_DESTROY = 0x0002;
    public const uint WM_CLOSE = 0x0010;
    public const uint WM_QUERYENDSESSION = 0x0011;
    public const uint WM_ENDSESSION = 0x0016;
    public const uint WM_SETTINGCHANGE = 0x001A;
    public const uint WM_CONTEXTMENU = 0x007B;
    public const uint WM_DISPLAYCHANGE = 0x007E;
    public const uint WM_TIMER = 0x0113;
    public const uint WM_HOTKEY = 0x0312;
    public const uint WM_DPICHANGED = 0x02E0;
    public const uint WM_APP = 0x8000;
    public const uint NIN_SELECT = 0x0400;
    public const uint NIN_KEYSELECT = 0x0401;

    // Shell_NotifyIcon
    public const uint NIM_ADD = 0, NIM_MODIFY = 1, NIM_DELETE = 2, NIM_SETVERSION = 4;
    public const uint NIF_MESSAGE = 0x1, NIF_ICON = 0x2, NIF_TIP = 0x4, NIF_INFO = 0x10, NIF_SHOWTIP = 0x80;
    public const uint NIIF_INFO = 0x1, NIIF_WARNING = 0x2, NIIF_RESPECT_QUIET_TIME = 0x80;
    public const uint NOTIFYICON_VERSION_4 = 4;

    // Menüs
    public const uint MF_STRING = 0x0, MF_GRAYED = 0x1, MF_CHECKED = 0x8, MF_POPUP = 0x10, MF_SEPARATOR = 0x800;
    public const uint TPM_RIGHTBUTTON = 0x2, TPM_BOTTOMALIGN = 0x20, TPM_NONOTIFY = 0x80, TPM_RETURNCMD = 0x100;

    public const uint WS_POPUP = 0x80000000;
    public const uint WS_EX_TOOLWINDOW = 0x80;
    public const int SM_CXSMICON = 49;
    public const int SW_SHOWNORMAL = 1;
    public const uint ASFW_ANY = unchecked((uint)-1);

    [LibraryImport("user32.dll", EntryPoint = "RegisterClassExW", SetLastError = true)]
    public static partial ushort RegisterClassEx(WNDCLASSEXW* lpwcx);

    [LibraryImport("user32.dll", EntryPoint = "CreateWindowExW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    public static partial nint CreateWindowEx(uint dwExStyle, string lpClassName, string lpWindowName, uint dwStyle, int x, int y, int nWidth, int nHeight, nint hWndParent, nint hMenu, nint hInstance, nint lpParam);

    [LibraryImport("user32.dll", EntryPoint = "DefWindowProcW")]
    public static partial nint DefWindowProc(nint hWnd, uint msg, nint wParam, nint lParam);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DestroyWindow(nint hWnd);

    [LibraryImport("user32.dll", EntryPoint = "GetMessageW")]
    public static partial int GetMessage(MSG* lpMsg, nint hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool TranslateMessage(MSG* lpMsg);

    [LibraryImport("user32.dll", EntryPoint = "DispatchMessageW")]
    public static partial nint DispatchMessage(MSG* lpMsg);

    [LibraryImport("user32.dll")]
    public static partial void PostQuitMessage(int nExitCode);

    [LibraryImport("user32.dll", EntryPoint = "PostMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool PostMessage(nint hWnd, uint msg, nint wParam, nint lParam);

    [LibraryImport("user32.dll", EntryPoint = "RegisterWindowMessageW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial uint RegisterWindowMessage(string lpString);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool RegisterHotKey(nint hWnd, int id, uint fsModifiers, uint vk);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool UnregisterHotKey(nint hWnd, int id);

    [LibraryImport("user32.dll")]
    public static partial nint CreatePopupMenu();

    [LibraryImport("user32.dll", EntryPoint = "AppendMenuW", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool AppendMenu(nint hMenu, uint uFlags, nint uIDNewItem, string? lpNewItem);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetMenuDefaultItem(nint hMenu, uint uItem, uint fByPos);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DestroyMenu(nint hMenu);

    [LibraryImport("user32.dll")]
    public static partial int TrackPopupMenuEx(nint hMenu, uint uFlags, int x, int y, nint hWnd, nint lptpm);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetForegroundWindow(nint hWnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool AllowSetForegroundWindow(uint dwProcessId);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetCursorPos(out POINT lpPoint);

    [LibraryImport("user32.dll")]
    public static partial nuint SetTimer(nint hWnd, nuint nIDEvent, uint uElapse, nint lpTimerFunc);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool KillTimer(nint hWnd, nuint uIDEvent);

    [LibraryImport("user32.dll")]
    public static partial nint CreateIconFromResourceEx(byte* presbits, uint dwResSize, [MarshalAs(UnmanagedType.Bool)] bool fIcon, uint dwVer, int cxDesired, int cyDesired, uint flags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DestroyIcon(nint hIcon);

    [LibraryImport("user32.dll")]
    public static partial int GetSystemMetricsForDpi(int nIndex, uint dpi);

    [LibraryImport("user32.dll")]
    public static partial uint GetDpiForSystem();

    [LibraryImport("shell32.dll", EntryPoint = "Shell_NotifyIconW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool Shell_NotifyIcon(uint dwMessage, NOTIFYICONDATAW* lpData);

    [LibraryImport("shell32.dll", EntryPoint = "ShellExecuteW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial nint ShellExecute(nint hwnd, string? lpOperation, string lpFile, string? lpParameters, string? lpDirectory, int nShowCmd);

    [LibraryImport("kernel32.dll", EntryPoint = "GetModuleHandleW")]
    public static partial nint GetModuleHandle(nint lpModuleName);

    [LibraryImport("kernel32.dll")]
    public static partial nint GetCurrentProcess();

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetProcessWorkingSetSize(nint hProcess, nint dwMinimumWorkingSetSize, nint dwMaximumWorkingSetSize);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetProcessInformation(nint hProcess, int processInformationClass, void* processInformation, uint processInformationSize);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    public static partial nint OpenProcess(uint dwDesiredAccess, [MarshalAs(UnmanagedType.Bool)] bool bInheritHandle, uint dwProcessId);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool CloseHandle(nint hObject);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool RegisterWaitForSingleObject(out nint phNewWaitObject, nint hObject, delegate* unmanaged<nint, byte, void> callback, nint context, uint dwMilliseconds, uint dwFlags);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool UnregisterWait(nint waitHandle);

    [LibraryImport("kernel32.dll", EntryPoint = "LoadLibraryExW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial nint LoadLibraryEx(string lpLibFileName, nint hFile, uint dwFlags);

    [LibraryImport("kernel32.dll")]
    public static partial nint GetProcAddress(nint hModule, nint lpProcName);

    public static int LoWord(nint value) => (int)(value & 0xFFFF);

    public static short SignedLoWord(nint value) => unchecked((short)(value & 0xFFFF));

    public static short SignedHiWord(nint value) => unchecked((short)((value >> 16) & 0xFFFF));
}
