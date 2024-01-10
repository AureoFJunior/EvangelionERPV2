using Serilog.Events;
using Serilog;

namespace EvangelionERPV2.Web.Logging
{
    public static class LogConfig
    {
        public static void Configure()
        {
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .MinimumLevel.Override("Microsoft", LogEventLevel.Information)  // Adjust the log level for Microsoft logs
                .Enrich.FromLogContext()
                .WriteTo.Console()
                // Add more configuration as needed, such as additional sinks, file logging, etc.
                .CreateLogger();
        }
    }
}
