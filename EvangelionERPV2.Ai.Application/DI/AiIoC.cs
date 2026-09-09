using EvangelionERPV2.AiModule.Application.Configs;
using EvangelionERPV2.AiModule.Application.Interface;
using EvangelionERPV2.AiModule.Application.Services;
using EvangelionERPV2.AiModule.Application.Tools;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace EvangelionERPV2.AiModule.Application.DI
{
    public static class AiIoC
    {
        public static void Configure(IServiceCollection services, IConfiguration configuration)
        {
            services.Configure<AiSettings>(configuration.GetSection("AiSettings"));

            services.AddScoped<IAiTool, BuscarProdutosTool>();
            services.AddScoped<IAiTool, BuscarClientesTool>();
            services.AddScoped<IAiTool, ConsultarPedidosTool>();

            services.AddScoped<IAiToolRegistry>(sp =>
                new AiToolRegistry(sp.GetServices<IAiTool>()));
        }
    }
}
