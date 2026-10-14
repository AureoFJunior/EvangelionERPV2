using EvangelionERPV2.NotificationModule.Application.Interface;
using EvangelionERPV2.NotificationModule.Application.Services;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Repositories;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace EvangelionERPV2.NotificationModule.Application.DI
{
    public class NotificationIoC
    {
        /// <summary>
        /// Must be configured after <c>BillsIoC</c>: the feed's alerts come from
        /// <c>IDailyPanelService</c>, which Bills registers.
        /// </summary>
        public static void Configure(IServiceCollection services, IConfiguration configuration)
        {
            try
            {
                services.AddScoped(typeof(IRepository<Notification>), typeof(Repository<Notification>));
                services.AddScoped(typeof(IRepository<NotificationRead>), typeof(Repository<NotificationRead>));
                services.AddScoped<INotificationService, NotificationService>();
            }
            catch (Exception ex)
            {
                Log.Logger.Error("Error at DI IoC Notification. ErrorType={ErrorType}", ex.GetType().Name);
            }
        }
    }
}
