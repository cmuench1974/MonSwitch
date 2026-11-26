namespace MonSwitch.Core;

/// <summary>Minimales Fehlerprotokoll (%LOCALAPPDATA%\MonSwitch\monswitch.log, max. ~256 KB + eine Vorgängerdatei).</summary>
public static class Log
{
    private const long MaxSize = 256 * 1024;
    private static readonly Lock Sync = new();

    public static string FilePath { get; } = Path.Combine(
        Environment.GetEnvironmentVariable(Config.ConfigStore.ConfigDirEnvVar) is { Length: > 0 } dir
            ? dir
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MonSwitch"),
        "monswitch.log");

    public static void Error(string message) => Write("FEHLER", message);

    public static void Error(Exception ex, string context) => Write("FEHLER", $"{context}: {ex}");

    public static void Info(string message) => Write("INFO", message);

    private static void Write(string level, string message)
    {
        try
        {
            lock (Sync)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                var fi = new FileInfo(FilePath);
                if (fi.Exists && fi.Length > MaxSize)
                    File.Move(FilePath, FilePath + ".old", overwrite: true);
                File.AppendAllText(FilePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{level}] [{Environment.ProcessId}] {message}{Environment.NewLine}");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Protokollierung darf niemals die Anwendung stören.
        }
    }
}
