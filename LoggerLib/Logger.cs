using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace LoggerLib;

public static class Logger
{
    /// <summary>
    /// If false, the logger will not be logging any information.
    /// Log files will also not be created while this is false.
    /// </summary>
    public static bool ShouldLog = true;
    
    /// <summary>
    /// The amount of logs to keep before deleting the oldest.
    /// If -1, no logs will be deleted.
    /// </summary>
    public static int KeepLogs { get; set; } = -1;

    /// <summary>
    /// The directory to save the logs in.
    /// </summary>
    public static string? LogDir { get; set; }

    /// <summary>
    /// If false, all incoming debug logs will be ignored.
    /// </summary>
    public static bool LogDebug { get; set; } = true;
    
    /// <summary>
    /// If false, first chance exceptions will not be logged. Set this if the logger is flooded with exceptions.
    /// </summary>
    public static bool LogFirstChance { get; set; } = true;

    private static readonly SemaphoreSlim _semaphore = new(1, 1);
    private static FileStream? _stream;

    static Logger()
    {
        Initialize();
    }
    
    public static void Initialize()
    {
        AppDomain.CurrentDomain.ProcessExit += OnExit;
        AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
        {
            try
            {
                LogE($"Unhandled exception: {e.ExceptionObject}").GetAwaiter().GetResult();
            }
            catch (InvalidOperationException) { }
            
            if (e.IsTerminating)
            {
                OnExit(sender, EventArgs.Empty);
            }
        };

        AppDomain.CurrentDomain.FirstChanceException += (_, e) =>
        {
            if (LogFirstChance)
            {
                try
                {
                    LogE($"First chance: {e.Exception}");
                }
                catch (InvalidOperationException) { }
            }
        };

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            try
            {
                LogE($"Unobserved task exception: {e.Exception}");
            }
            catch (InvalidOperationException) { }
            
            e.SetObserved();
        };
    }

    public static async Task CreateFile()
    {
        if (!ShouldLog)
        {
            return;
        }
        
        if (LogDir == null)
        {
            throw new NullReferenceException("LogDir has not been set");
        }

        if (_stream != null)
        {
            await _stream.FlushAsync();
            
            _stream.Dispose();
            _stream = null;
        }

        await _semaphore.WaitAsync();
        try
        {
            Directory.CreateDirectory(LogDir);

            string date = DateTime.Now.ToString("yyyy-MM-dd");
            string filePath = Path.Combine(LogDir, $"{date}.log");

            List<string> filePaths = Directory.GetFiles(LogDir).ToList();
            filePaths.Sort((a, b) => File.GetCreationTime(a).CompareTo(File.GetCreationTime(b)));
            
            if (filePaths.Any(path => Path.GetFileName(path).StartsWith(date)))
            {
                string[] parts = filePaths.Select(Path.GetFileName)
                                          .Last(path => path.StartsWith(date))
                                          .TrimEnd(".log".ToCharArray())
                                          .Split('-');

                if (parts.Length > 3 && int.TryParse(parts[parts.Length - 1], out int index))
                {
                    filePath = Path.Combine(LogDir, $"{date}-{index + 1}.log");
                }
                else
                {
                    filePath = Path.Combine(LogDir, $"{date}-1.log");
                }
            }

            string info =
                $"""
                 ----------

                 Machine: {Environment.MachineName}
                 OS: {RuntimeInformation.OSDescription}
                 OS Architecture: {RuntimeInformation.OSArchitecture}
                 Process Architecture: {RuntimeInformation.ProcessArchitecture}
                 Runtime: {RuntimeInformation.FrameworkDescription}
                 CPU Cores: {Environment.ProcessorCount}

                 ----------

                 
                 """;

            File.WriteAllText(filePath, info);
            
            _stream = new FileStream(
                filePath,
                FileMode.Append,
                FileAccess.Write,
                FileShare.Read,
                4096,
                useAsync: true);

            if (KeepLogs == -1)
            {
                return;
            }
            
            while (KeepLogs < filePaths.Count + 1)
            {
                File.Delete(filePaths[0]);
                filePaths.RemoveAt(0);
            }
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public static async Task Log(LogLevel level, string text)
    {
        if (!ShouldLog)
        {
            return;
        }
        
        if (_stream == null)
        {
            throw new InvalidOperationException("File has not been created");
        }
        
        if (level == LogLevel.Debug && !LogDebug)
        {
            return;
        }
        
        await _semaphore.WaitAsync();
        try
        {
            byte[] buffer = BuildOutput(level, text);
            await _stream.WriteAsync(buffer, 0, buffer.Length);
            await _stream.FlushAsync();
        }
        finally
        {
            _semaphore.Release();
        }
    }
    
    public static Task LogD(string text)
        => Log(LogLevel.Debug, text);
    
    public static Task LogI(string text)
        => Log(LogLevel.Info, text);

    public static Task LogM(string text)
        => Log(LogLevel.Message, text);

    public static Task LogW(string text)
        => Log(LogLevel.Warning, text);

    public static Task LogE(string text)
        => Log(LogLevel.Error, text);

    private static byte[] BuildOutput(LogLevel type, string text)
    {
        return Encoding.UTF8.GetBytes($"[{DateTime.Now.ToString("HH:mm:ss")}] [{type}] --- {text}\n");
    }

    private static void OnExit(object? sender, EventArgs e)
    {
        try
        {
            LogI("Process exiting...").GetAwaiter().GetResult();
        }
        catch (InvalidOperationException) { }
    }
}