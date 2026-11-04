using System.Text;

namespace AlienHelper;

/// <summary>
/// Small file log: %APPDATA%\AlienHelper\logs\alienhelper-yyyyMMdd.log, one file per day, kept for 7 days.
/// A fatal (unhandled) exception also leaves crash.flag so the next start can offer a report.
/// </summary>
public static class Log
{
    /// <summary>Program name as shown to people. Files, folders, tasks and the exe use <see cref="AppId.Key"/>.</summary>
    public const string AppTitle = AppId.Title;

    /// <summary>Public source code (shown in About).</summary>
    public const string RepoUrl = "https://github.com/kirillgetmanskiy/AlienHelper";
    public const string StoreUrl = "https://apps.microsoft.com/detail/9PC5V3BMXDWP";   // buying it there is the way to support the project
    public const string ContactEmail = "kirill.getmanskiy@gmail.com";

    static readonly object _lock = new();
    public static string Dir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppId.Key);
    public static string LogDir => Path.Combine(Dir, "logs");
    static string CrashFlag => Path.Combine(Dir, "crash.flag");

    public static void Info(string message) => Write("INFO", message);
    public static void Warn(string message) => Write("WARN", message);
    public static void Error(string message, Exception? ex = null) => Write("ERROR", ex == null ? message : $"{message}: {ex}");

    /// <summary>Unhandled exception that ends the process: logged and flagged for the next start.</summary>
    public static void Fatal(Exception? ex)
    {
        Write("FATAL", ex?.ToString() ?? "unknown error");
        try { File.WriteAllText(CrashFlag, DateTime.Now.ToString("O")); } catch { }
    }

    /// <summary>True (once) if the previous run ended with an unhandled exception.</summary>
    public static bool TakeCrashFlag()
    {
        try
        {
            if (!File.Exists(CrashFlag)) return false;
            File.Delete(CrashFlag);
            return true;
        }
        catch { return false; }
    }

    static void Write(string level, string message)
    {
        try
        {
            lock (_lock)
            {
                Directory.CreateDirectory(LogDir);
                var file = Path.Combine(LogDir, $"alienhelper-{DateTime.Now:yyyyMMdd}.log");
                File.AppendAllText(file, $"{DateTime.Now:HH:mm:ss.fff} [{level}] {message}{Environment.NewLine}", Encoding.UTF8);
            }
        }
        catch { /* logging must never break the app */ }
    }

    /// <summary>Deletes logs older than a week.</summary>
    public static void Cleanup()
    {
        try
        {
            foreach (var f in Directory.EnumerateFiles(LogDir, "alienhelper-*.log"))
                if (File.GetLastWriteTime(f) < DateTime.Now.AddDays(-7)) File.Delete(f);
        }
        catch { }
    }
}
