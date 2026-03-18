using EvangelionERPV2.OpportunityRadarModule.Application.Configs;
using EvangelionERPV2.OpportunityRadarModule.Application.Interface;
using EvangelionERPV2.OpportunityRadarModule.Application.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace EvangelionERPV2.OpportunityRadarModule.Application.DI
{
    public static class OpportunityRadarIoC
    {
        public static void Configure(IServiceCollection services, IConfiguration configuration)
        {
            services.Configure<OpportunityRadarSettings>(configuration.GetSection("OpportunityRadarSettings"));
            services.AddScoped<IOpportunityRadarService, OpportunityRadarService>();
        }
    }
}

