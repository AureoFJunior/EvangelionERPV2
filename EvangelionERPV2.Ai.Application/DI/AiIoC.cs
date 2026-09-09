using EvangelionERPV2.AiModule.Application.Configs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace EvangelionERPV2.AiModule.Application.DI
{
    public static class AiIoC
    {
        public static void Configure(IServiceCollection services, IConfiguration configuration)
        {
            services.Configure<AiSettings>(configuration.GetSection("AiSettings"));
        }
    }
}
