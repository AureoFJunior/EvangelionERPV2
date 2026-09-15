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

            services.AddSingleton<IChatClient>(serviceProvider =>
            {
                var settings = serviceProvider.GetRequiredService<IOptions<AiSettings>>().Value;

                // OllamaSharp's Uri constructor builds an HttpClient with the 100 s BuiltIn default,
                // which cuts a turn short before AiSettings.TimeoutSeconds ever applies. Give the
                // transport a longer leash than the turn budget so the budget stays the authority
                // (and the diagnostics stay truthful); no budget means no transport deadline either.
                var http = new HttpClient
                {
                    BaseAddress = new Uri(settings.Endpoint),
                    Timeout = settings.TimeoutSeconds > 0
                        ? TimeSpan.FromSeconds(settings.TimeoutSeconds + 15)
                        : Timeout.InfiniteTimeSpan
                };

                return new OllamaSharp.OllamaApiClient(http, settings.Model);
            });

            services.AddScoped<IAiTool, BuscarProdutosTool>();
            services.AddScoped<IAiTool, BuscarClientesTool>();
            services.AddScoped<IAiTool, ConsultarPedidosTool>();
            services.AddScoped<IAiTool, ResumoDeVendasTool>();
            services.AddScoped<IAiTool, PainelDoDiaTool>();
            services.AddScoped<IAiTool, ContasAReceberTool>();
            services.AddScoped<IAiTool, PrevisaoDeCaixaTool>();
            services.AddScoped<IAiTool, EstoqueBaixoTool>();
            services.AddScoped<IAiTool, ProdutosMaisVendidosTool>();
            services.AddScoped<IAiTool, MelhoresClientesTool>();
            services.AddScoped<IAiTool, ClientesInadimplentesTool>();
            services.AddScoped<IAiTool, ContasAPagarTool>();
            services.AddScoped<IAiTool, ComprasPendentesTool>();

            services.AddScoped<IAiTool, CriarClienteTool>();
            services.AddScoped<IAiTool, AtualizarClienteTool>();
            services.AddScoped<IAiTool, AtualizarProdutoTool>();
            services.AddScoped<IAiTool, CriarPedidoTool>();
            services.AddScoped<IAiTool, AjustarEstoqueTool>();
            services.AddScoped<IAiTool, ReceberCompraTool>();

            services.AddScoped<IAiToolRegistry>(sp =>
                new AiToolRegistry(sp.GetServices<IAiTool>()));

            // Speech has its own client and its own timeout: a 30 s transcription budget has nothing
            // to do with a 120 s agent turn. Registered unconditionally — the service itself refuses
            // when SpeechEnabled is false, which is cheaper than making every caller ask the
            // container whether the feature exists.
            services.AddHttpClient<IAiSpeechService, AiSpeechService>((serviceProvider, client) =>
            {
                var settings = serviceProvider.GetRequiredService<IOptions<AiSettings>>().Value;
                client.BaseAddress = new Uri(settings.SpeechEndpoint);
                client.Timeout = TimeSpan.FromSeconds(Math.Max(5, settings.SpeechTimeoutSeconds));
            });

            // Same arrangement as speech, and the same reason: its own endpoint, its own budget, and
            // the service refuses when VoiceEnabled is false rather than being absent from the
            // container. A synthesis that takes 20 s has already failed the listener.
            services.AddHttpClient<IAiVoiceService, AiVoiceService>((serviceProvider, client) =>
            {
                var settings = serviceProvider.GetRequiredService<IOptions<AiSettings>>().Value;
                client.BaseAddress = new Uri(settings.VoiceEndpoint);
                client.Timeout = TimeSpan.FromSeconds(Math.Max(5, settings.VoiceTimeoutSeconds));
            });

            services.AddScoped<IAiPendingActionService, AiPendingActionService>();
            services.AddScoped<IAiRetentionService, AiRetentionService>();
            services.AddScoped<IAiAgentService, AiAgentService>();

            if (configuration.GetSection("AiSettings").GetValue<bool>("Enabled"))
                services.AddHostedService<AiPendingActionExpiryWorker>();
        }
    }
}
