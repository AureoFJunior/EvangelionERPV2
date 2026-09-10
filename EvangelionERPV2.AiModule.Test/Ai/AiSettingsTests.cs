using EvangelionERPV2.AiModule.Application.Configs;
using EvangelionERPV2.AiModule.Application.DI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace EvangelionERPV2.AiModule.Test.Ai
{
    public class AiSettingsTests
    {
        [Fact]
        public void Configure_BindsTheSection()
        {
            var services = BuildServices(new Dictionary<string, string?>
            {
                ["AiSettings:Enabled"] = "true",
                ["AiSettings:Model"] = "qwen3:14b",
                ["AiSettings:MaxToolIterations"] = "4",
            });

            var settings = services.GetRequiredService<IOptions<AiSettings>>().Value;

            Assert.True(settings.Enabled);
            Assert.Equal("qwen3:14b", settings.Model);
            Assert.Equal(4, settings.MaxToolIterations);
        }

        [Fact]
        public void Configure_DefaultsToDisabled()
        {
            var services = BuildServices(new Dictionary<string, string?>());

            var settings = services.GetRequiredService<IOptions<AiSettings>>().Value;

            Assert.False(settings.Enabled);
            Assert.Equal("http://localhost:11434", settings.Endpoint);
        }

        private static ServiceProvider BuildServices(Dictionary<string, string?> values)
        {
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
            var services = new ServiceCollection();
            AiIoC.Configure(services, configuration);
            return services.BuildServiceProvider();
        }
    }
}
