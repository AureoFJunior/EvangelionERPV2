using Serilog.Events;
using Serilog.Sinks.SystemConsole.Themes;
using Serilog;

namespace EvangelionERPV2.Worker.OrderModule.OrderWorker
{
    public static class OrderLogConfig
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

            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Information()
                .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
                .Enrich.FromLogContext()
                .WriteTo.Console(theme: customTheme)
                .WriteTo.File(path: @"c:\evaerpv2\logs\evarpv2.log",
                    rollingInterval: RollingInterval.Day,
                    rollOnFileSizeLimit: true,
                    fileSizeLimitBytes: 50000)
                .CreateLogger();
        }
    }
}
