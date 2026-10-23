using EvangelionERPV2.NFeModule.Application.DI;
using EvangelionERPV2.NFeModule.Application.Interface;
using EvangelionERPV2.NFeModule.Application.Providers;
using EvangelionERPV2.NFeModule.Infra.Providers;
using EvangelionERPV2.NFeModule.Infra.Sefaz;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace EvangelionERPV2.Test.Resilience;

public class NFeProviderRegistrationTests
{
    [Fact]
    public void Stub_is_registered_only_when_explicitly_allowed_by_the_host()
    {
        Assert.Equal(typeof(SefazNFeProvider), ProviderType(useStubProvider: false));
        Assert.Equal(typeof(StubNFeProvider), ProviderType(useStubProvider: true));
    }

    private static Type ProviderType(bool useStubProvider)
    {
        var services = new ServiceCollection();
        NFeIoC.Configure(services, new ConfigurationBuilder().Build(), useStubProvider);
        return services.Last(descriptor => descriptor.ServiceType == typeof(INFeProvider)).ImplementationType!;
    }
}
