using System.Runtime.InteropServices;
using System.Text;
using FileLib;

namespace LoggerLib;

public static class Logger
{
    [DllImport("libc", SetLastError = true)]
    private static extern int chmod(string pathname, uint mode);
        
    [DllImport("libc", SetLastError = true)]
    private static extern int chown(string path, uint owner, uint group);
    
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
    /// The logger will only log calls higher or equal to the set value. This excludes debug, see <see cref="LogDebug"/>.
    /// </summary>
    public static LogLevel LogLevel { get; set; } = LogLevel.Info;
    
    /// <summary>
    /// If false, first chance exceptions will not be logged. Set this if the logger is flooded with exceptions.
    /// </summary>
    public static bool LogFirstChance { get; set; } = true;

    /// <summary>
    /// If true, opens a terminal and prints all the logs live into it.
    /// </summary>
    public static bool LogToTerminal { get; set; }

    public static bool HasFileBeenCreated { get; private set; }
    
    private static readonly SemaphoreSlim Semaphore = new(1, 1);
    private static FileStream? _stream;

    static Logger()
    {
        Initialize();
    }
    
    private static void Initialize()
    {
        AppDomain.CurrentDomain.ProcessExit += OnExit;
        AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
        {
            try
            {
                if (!HasFileBeenCreated)
                {
                    return;
                }
                
                LogE($"Unhandled exception: {e.ExceptionObject}").GetAwaiter().GetResult();
            }
            catch (InvalidOperationException) { }
            
            if (e.IsTerminating)
            {
                if (!HasFileBeenCreated)
                {
                    return;
                }

                OnExit(sender, EventArgs.Empty);
            }
        };

        AppDomain.CurrentDomain.FirstChanceException += (_, e) =>
        {
            if (!LogFirstChance)
            {
                return;
            }
            
            try
            {
                if (!HasFileBeenCreated)
                {
                    return;
                }

                LogE($"First chance: {e.Exception}");
            }
            catch (InvalidOperationException) { }
        };

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            try
            {
                if (!HasFileBeenCreated)
                {
                    return;
                }

                LogE($"Unobserved task exception: {e.Exception}");
            }
            catch (InvalidOperationException) { }
            
            e.SetObserved();
        };
    }

    public static void CreateFile()
    {
        CreateFileAsync().GetAwaiter().GetResult();
    }
    
    public static async Task CreateFileAsync()
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

        await Semaphore.WaitAsync();
        try
        {
            Directory.CreateDirectory(LogDir);

            string date = DateTime.Now.ToString("yyyy-MM-dd");
            string filePath = Path.Combine(LogDir, $"{date}.log");

            List<string> filePaths = Directory.GetFiles(LogDir).ToList();
            filePaths.Sort((a, b) => File.GetCreationTime(a).CompareTo(File.GetCreationTime(b)));

            string? path = filePaths.Select(Path.GetFileNameWithoutExtension)
                                    .LastOrDefault(path => path.StartsWith(date));
            if (path != null)
            {
                string[] parts = path.Split('-');
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
                 
                 Creation Time: {DateTime.Now.ToString("HH:mm:ss")}
                 Log Level: {LogLevel}

                 ----------

                 
                 """;
            
            File.WriteAllText(filePath, info);
            
            if (LogToTerminal)
            {
                await Console.Out.WriteAsync(info);
            }
            
            _stream = new FileStream(
                filePath,
                FileMode.Append,
                FileAccess.Write,
                FileShare.Read,
                4096,
                useAsync: true);
            
            HasFileBeenCreated = true;

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                chmod(filePath, (uint) (UnixFileModes.UserRW | UnixFileModes.GroupRX | UnixFileModes.OtherRX));
                
                string? sudoUidStr = Environment.GetEnvironmentVariable("SUDO_UID");
                string? sudoGidStr = Environment.GetEnvironmentVariable("SUDO_GID");

                if (sudoUidStr != null && sudoGidStr != null &&
                    uint.TryParse(sudoUidStr, out uint uid) &&
                    uint.TryParse(sudoGidStr, out uint gid))
                {
                    chown(filePath, uid, gid);
                }
            }
            
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
        catch { }
        finally
        {
            Semaphore.Release();
        }
    }

    public static async Task Log(LogLevel level, string text)
    {
        if (!ShouldLog)
        {
            return;
        }

        if (level != LogLevel.Debug && level < LogLevel)
        {
            return;
        }
        
        if (level == LogLevel.Debug && !LogDebug)
        {
            return;
        }
        
        if (_stream == null)
        {
            throw new InvalidOperationException("File has not been created");
        }
        
        await Semaphore.WaitAsync();
        
        try
        {
            byte[] buffer = BuildOutput(level, text);
            await _stream.WriteAsync(buffer, 0, buffer.Length);
            await _stream.FlushAsync();
            
            if (LogToTerminal)
            {
                await Console.Out.WriteLineAsync(BuildTerminalOutput(level, text));
            }
        }
        catch { }
        finally
        {
            Semaphore.Release();
        }
    }
    
    public static Task LogD(string text)
        => Log(LogLevel.Debug, text);
    
    public static Task LogI(string text)
        => Log(LogLevel.Info, text);

    public static Task LogW(string text)
        => Log(LogLevel.Warning, text);

    public static Task LogE(string text)
        => Log(LogLevel.Error, text);
    
    public static Task LogS(string text)
        => Log(LogLevel.System, text);

    private static byte[] BuildOutput(LogLevel level, string text)
    {
        return Encoding.UTF8.GetBytes($"[{DateTime.Now.ToString("HH:mm:ss")}] [{level}] --- {text}\n");
    }

    private static string BuildTerminalOutput(LogLevel level, string text)
    {
        string colorAnsi = level switch
        {
            LogLevel.Debug => LogColor.DebugAnsi,
            LogLevel.Info => LogColor.InfoAnsi,
            LogLevel.Warning => LogColor.WarningAnsi,
            LogLevel.Error => LogColor.ErrorAnsi,
            LogLevel.System => LogColor.SystemAnsi,
            _ => LogColor.ResetAnsi,
        };
        
        return $"[{DateTime.Now.ToString("HH:mm:ss")}] [{colorAnsi}{level}{LogColor.ResetAnsi}] --- {text}";
    }

    private static void OnExit(object? sender, EventArgs e)
    {
        try
        {
            if (!HasFileBeenCreated)
            {
                return;
            }

            LogI("Process exiting...").GetAwaiter().GetResult();
        }
        catch (InvalidOperationException) { }
    }
}