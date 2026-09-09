using EvangelionERPV2.AiModule.Application.Configs;
using EvangelionERPV2.AiModule.Application.Interface;
using EvangelionERPV2.AiModule.Application.Services;
using EvangelionERPV2.AiModule.Application.Tools;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace EvangelionERPV2.AiModule.Application.DI
{
    public static class AiIoC
    {
        public static void Configure(IServiceCollection services, IConfiguration configuration)
        {
            services.Configure<AiSettings>(configuration.GetSection("AiSettings"));

            // OllamaSharp's client implements IChatClient directly. Microsoft.Extensions.AI.Ollama
            // is deprecated and must not be used. Nothing else in this module knows about Ollama —
            // pointing the agent at a hosted provider is this registration and nothing more.
            services.AddSingleton<IChatClient>(serviceProvider =>
            {
                var settings = serviceProvider.GetRequiredService<IOptions<AiSettings>>().Value;
                return new OllamaSharp.OllamaApiClient(new Uri(settings.Endpoint), settings.Model);
            });

            services.AddScoped<IAiTool, BuscarProdutosTool>();
            services.AddScoped<IAiTool, BuscarClientesTool>();
            services.AddScoped<IAiTool, ConsultarPedidosTool>();
            services.AddScoped<IAiTool, ResumoDeVendasTool>();
            services.AddScoped<IAiTool, PainelDoDiaTool>();
            services.AddScoped<IAiTool, ContasAReceberTool>();
            services.AddScoped<IAiTool, PrevisaoDeCaixaTool>();
            services.AddScoped<IAiTool, EstoqueBaixoTool>();

            services.AddScoped<IAiToolRegistry>(sp =>
                new AiToolRegistry(sp.GetServices<IAiTool>()));

            services.AddScoped<IAiAgentService, AiAgentService>();
        }
    }
}
