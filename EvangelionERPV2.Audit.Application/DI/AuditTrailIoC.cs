using EvangelionERPV2.AuditModule.Application.Interface;
using EvangelionERPV2.AuditModule.Application.Services;
using EvangelionERPV2.AuditModule.Domain.Interface;
using EvangelionERPV2.AuditModule.Domain.Repositories;
using EvangelionERPV2.Shared.Auditing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace EvangelionERPV2.AuditModule.Application.DI
{
    public class AuditTrailIoC
    {
        public static void Configure(IServiceCollection services, IConfiguration configuration)
        {
            try
            {
                services.AddSingleton<IAuditTrailEntryFactory, AuditTrailEntryFactory>();
                services.AddScoped<IAuditTrailRepository, AuditTrailRepository>();
                services.AddScoped<IAuditTrailService, AuditTrailService>();
            }
            catch (Exception ex)
            {
                Log.Logger.Error("Error at DI IoC AuditTrail. ErrorType={ErrorType}", ex.GetType().Name);
            }
        }
    }
}
