namespace LoggerLib;

public class LogColor
{
    public const string ResetAnsi = "\x1b[0m";
    public const string DebugAnsi = "\x1b[38;2;240;110;250m";
    public const string InfoAnsi = "\x1b[38;2;210;210;210m";
    public const string WarningAnsi = "\x1b[38;2;230;230;70m";
    public const string ErrorAnsi = "\x1b[38;2;240;80;70m";
    public const string SystemAnsi = "\x1b[38;2;110;200;250m";
}