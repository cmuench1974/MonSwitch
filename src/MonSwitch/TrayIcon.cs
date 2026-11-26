using System.Buffers.Binary;

namespace MonSwitch.Agent;

/// <summary>Dünner Wrapper um Shell_NotifyIcon (NOTIFYICON_VERSION_4).</summary>
internal sealed unsafe class TrayIcon
{
    private const uint IconId = 1;
    private readonly nint _hwnd;
    private readonly uint _callbackMessage;
    private nint _icon;
    private string _tip = "MonSwitch";

    public TrayIcon(nint hwnd, uint callbackMessage)
    {
        _hwnd = hwnd;
        _callbackMessage = callbackMessage;
    }

    public bool IsVisible { get; private set; }

    /// <summary>Fügt das Symbol hinzu (auch nach einem Neustart des Explorers über „TaskbarCreated“).</summary>
    public bool Add()
    {
        var data = CreateData(Win32.NIF_MESSAGE | Win32.NIF_ICON | Win32.NIF_TIP | Win32.NIF_SHOWTIP);
        Win32.Shell_NotifyIcon(Win32.NIM_DELETE, &data);
        IsVisible = Win32.Shell_NotifyIcon(Win32.NIM_ADD, &data);
        if (IsVisible)
        {
            data.uVersion = Win32.NOTIFYICON_VERSION_4;
            Win32.Shell_NotifyIcon(Win32.NIM_SETVERSION, &data);
        }
        return IsVisible;
    }

    public void Update(nint icon, string tip)
    {
        _icon = icon;
        _tip = tip;
        if (!IsVisible)
            return;
        var data = CreateData(Win32.NIF_ICON | Win32.NIF_TIP | Win32.NIF_SHOWTIP);
        Win32.Shell_NotifyIcon(Win32.NIM_MODIFY, &data);
    }

    public void ShowBalloon(string title, string text, bool warning)
    {
        if (!IsVisible)
            return;
        var data = CreateData(Win32.NIF_INFO);
        Copy(title, data.szInfoTitle, 64);
        Copy(text, data.szInfo, 256);
        data.dwInfoFlags = (warning ? Win32.NIIF_WARNING : Win32.NIIF_INFO) | Win32.NIIF_RESPECT_QUIET_TIME;
        Win32.Shell_NotifyIcon(Win32.NIM_MODIFY, &data);
    }

    public void Remove()
    {
        if (!IsVisible)
            return;
        var data = CreateData(0);
        Win32.Shell_NotifyIcon(Win32.NIM_DELETE, &data);
        IsVisible = false;
    }

    private NOTIFYICONDATAW CreateData(uint flags)
    {
        var data = new NOTIFYICONDATAW
        {
            cbSize = (uint)sizeof(NOTIFYICONDATAW),
            hWnd = _hwnd,
            uID = IconId,
            uFlags = flags,
            uCallbackMessage = _callbackMessage,
            hIcon = _icon,
        };
        Copy(_tip, data.szTip, 128);
        return data;
    }

    private static void Copy(string text, char* destination, int capacity)
    {
        int length = Math.Min(text.Length, capacity - 1);
        text.AsSpan(0, length).CopyTo(new Span<char>(destination, capacity));
        destination[length] = '\0';
    }
}

/// <summary>Lädt Symbole aus den eingebetteten .ico-Dateien in exakt der benötigten Größe.</summary>
internal static unsafe class IconLoader
{
    public static nint Load(string resourceName, int size)
    {
        using var stream = typeof(IconLoader).Assembly.GetManifestResourceStream(resourceName);
        if (stream == null)
            return 0;
        var data = new byte[stream.Length];
        stream.ReadExactly(data);

        // ICONDIR: reserved, type, count – danach je 16 Byte ICONDIRENTRY.
        int count = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(4));
        int best = -1, bestSize = 0;
        for (int i = 0; i < count; i++)
        {
            int entry = 6 + i * 16;
            int w = data[entry] == 0 ? 256 : data[entry];
            bool better = best < 0
                || (w >= size && (bestSize < size || w < bestSize))
                || (w < size && bestSize < size && w > bestSize);
            if (better)
            {
                best = entry;
                bestSize = w;
            }
        }
        if (best < 0)
            return 0;

        int length = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(best + 8));
        int offset = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(best + 12));
        if (offset < 0 || length <= 0 || offset + length > data.Length)
            return 0;
        fixed (byte* p = &data[offset])
            return Win32.CreateIconFromResourceEx(p, (uint)length, true, 0x00030000, size, size, 0);
    }
}
