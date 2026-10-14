using Serilog.Events;
using Serilog.Sinks.SystemConsole.Themes;
using Serilog;

namespace EvangelionERPV2.Worker.EmailModule.EmailWorker
{
    public static class EmailLogConfig
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

            // These workers run in Linux containers. A hard-coded "c:\..." path there is not a
            // path at all - Serilog creates a file whose *name* contains backslashes, in the
            // working directory - so every file log this worker wrote was effectively lost.
            var baseLogPath = OperatingSystem.IsWindows() ? @"C:\evaerpv2\logs\" : "/var/log/evaerpv2/";

            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Information()
                .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
                .Enrich.FromLogContext()
                .WriteTo.Console(theme: customTheme)
                .WriteTo.File(path: $@"{baseLogPath}evarpv2.log",
                    rollingInterval: RollingInterval.Day,
                    rollOnFileSizeLimit: true,
                    fileSizeLimitBytes: 50000)
                .CreateLogger();
        }
    }
}
