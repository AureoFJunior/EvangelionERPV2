using Serilog;
using Serilog.Events;

namespace EvangelionERPV2.Worker.NotificationModule
{
    /// <summary>
    /// Console only, on purpose: this worker runs in a container whose logs are collected by the
    /// engine, and the file sink the older workers carry writes to a Windows path that means
    /// nothing here.
    /// </summary>
    public static class NotificationLogConfig
    {
        public static void Configure()
        {
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Information()
                .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
                .Enrich.FromLogContext()
                .WriteTo.Console()
                .CreateLogger();
        }
    }
}
