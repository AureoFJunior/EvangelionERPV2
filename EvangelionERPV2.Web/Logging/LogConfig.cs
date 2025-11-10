using Serilog.Events;
using Serilog;
using Serilog.Sinks.SystemConsole.Themes;

namespace EvangelionERPV2.Web.Logging
{
    public static class LogConfig
    {
        public static void Configure()
        {
            var customThemeStyles =
                new Dictionary<ConsoleThemeStyle, SystemConsoleThemeStyle>
                {
                    {
                        ConsoleThemeStyle.Text, new SystemConsoleThemeStyle
                        {
                            Foreground = ConsoleColor.Green,
                        }
                    },
                    {
                        ConsoleThemeStyle.LevelInformation, new SystemConsoleThemeStyle
                        {
                            Foreground = ConsoleColor.Magenta,
                        }
                    },
                    {
                        ConsoleThemeStyle.LevelError, new SystemConsoleThemeStyle
                        {
                            Foreground = ConsoleColor.Red,
                        }
                    },
                    {
                        ConsoleThemeStyle.LevelWarning, new SystemConsoleThemeStyle
                        {
                            Foreground = ConsoleColor.DarkYellow,
                        }
                    },
                };

            var customTheme = new SystemConsoleTheme(customThemeStyles);

            var baseLogPath = OperatingSystem.IsWindows() ? @"C:\evaerpv2\logs\" : "/var/log/evaerpv2/";

            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .MinimumLevel.Override("Microsoft", LogEventLevel.Information)  // Adjust the log level for Microsoft logs
                .Enrich.FromLogContext()
                .WriteTo.Console(theme: customTheme) // Log to console and use a custom theme to the log
                .WriteTo.File(path: $@"{baseLogPath}evarpv2.log", // Path that contains the log file
                    rollingInterval: RollingInterval.Day,
                    rollOnFileSizeLimit: true,
                    fileSizeLimitBytes: 50000) // Create another file when the size exceds this value
                                               // Add more configuration as needed, such as additional sinks, file logging, etc.
                .CreateLogger();
        }
    }
}
