using System.IO;
using System.Text;

namespace HyprWin.Core;

/// <summary>
/// Simple file logger for HyprWin. Writes timestamped entries to %APPDATA%\HyprWin\hyprwin.log.
/// </summary>
public sealed class Logger : IDisposable
{
    private static Logger? _instance;
    private static readonly object _lock = new();
    private readonly StreamWriter _writer;
    private readonly string _logPath;
    private bool _disposed;

    public enum Level { INFO, WARN, ERROR, DEBUG }

    private Logger(string logPath)
    {
        _logPath = logPath;
        var dir = Path.GetDirectoryName(logPath)!;
        if (!Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        _writer = new StreamWriter(logPath, append: true, encoding: System.Text.Encoding.UTF8)
        {
            AutoFlush = true
        };
    }

    public static Logger Instance
    {
        get
        {
            if (_instance is null)
            {
                lock (_lock)
                {
                    _instance ??= new Logger(GetDefaultLogPath());
                }
            }
            return _instance;
        }
    }

    public static string GetDefaultLogPath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, "HyprWin", "hyprwin.log");
    }

    public static void Initialize(string? logPath = null)
    {
        lock (_lock)
        {
            _instance?.Dispose();
            _instance = new Logger(logPath ?? GetDefaultLogPath());
        }
    }

    public void Log(Level level, string message)
    {
        if (_disposed) return;
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
        var line = $"[{timestamp}] [{level}] {message}";
        lock (_lock)
        {
            try
            {
                _writer.WriteLine(line);
            }
            catch
            {
                // Swallow write errors to prevent cascading failures
            }
        }
    }

    public void Info(string message) => Log(Level.INFO, message);
    public void Warn(string message) => Log(Level.WARN, message);
    public void Error(string message) => Log(Level.ERROR, message);
    public void Error(string message, Exception ex) => Log(Level.ERROR, $"{message}: {ex.Message}\n{ex.StackTrace}");
    public void Debug(string message) => Log(Level.DEBUG, message);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _writer.Flush(); _writer.Dispose(); }
        catch { /* ignore */ }
    }
}
