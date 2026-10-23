using EvangelionERPV2.Shared.Entities.RabbitMQ;
using EvangelionERPV2.Shared.Utils;
using RabbitMQ.Client;

namespace EvangelionERPV2.Test.Security
{
    /// <summary>
    /// The broker link was on amqps only because a secret happened to say so - nothing required it.
    /// These pin the requirement so a cleartext connection cannot be introduced silently.
    /// </summary>
    public class RabbitMqSchemeEnforcementTests
    {
        private sealed class TestChannelSettings : BaseChannelSettings { }

        [Theory]
        [InlineData("Production")]
        [InlineData("Staging")]
        [InlineData("production")]
        public void EnsureTransportIsSecure_NoTlsOutsideDevelopment_Throws(string environment)
        {
            var exception = Assert.Throws<InvalidOperationException>(
                () => RabbitMQManagerBase<TestChannelSettings>.EnsureTransportIsSecure(false, environment));

            Assert.Contains("amqps", exception.Message);
            Assert.Contains("cleartext", exception.Message);
        }

        [Fact]
        public void EnsureTransportIsSecure_UnsetEnvironment_TreatedAsProduction()
        {
            Assert.Throws<InvalidOperationException>(
                () => RabbitMQManagerBase<TestChannelSettings>.EnsureTransportIsSecure(false, null));

            Assert.Throws<InvalidOperationException>(
                () => RabbitMQManagerBase<TestChannelSettings>.EnsureTransportIsSecure(false, string.Empty));
        }

        [Theory]
        [InlineData("Development")]
        [InlineData("development")]
        public void EnsureTransportIsSecure_NoTlsInDevelopment_IsAllowed(string environment)
        {
            RabbitMQManagerBase<TestChannelSettings>.EnsureTransportIsSecure(false, environment);
        }

        [Fact]
        public void EnsureTransportIsSecure_NoTlsInIsolatedE2E_IsAllowed()
        {
            RabbitMQManagerBase<TestChannelSettings>.EnsureTransportIsSecure(false, "E2E");
        }

        [Theory]
        [InlineData("Production")]
        [InlineData("Development")]
        [InlineData(null)]
        public void EnsureTransportIsSecure_TlsEnabled_IsAlwaysAllowed(string? environment)
        {
            RabbitMQManagerBase<TestChannelSettings>.EnsureTransportIsSecure(true, environment);
        }

        /// <summary>
        /// The regression the first version of this guard had: it inspected the URI scheme, so with
        /// no URI configured at all it never ran and the host/port fallback connected in cleartext.
        /// These pin the mapping the guard now relies on instead - that the factory's resolved
        /// Ssl.Enabled tells the truth for every way the connection can be configured.
        /// </summary>
        [Fact]
        public void ConnectionFactory_HostPortFallback_ReportsTlsDisabled()
        {
            var factory = new ConnectionFactory { HostName = "broker.example", Port = 5672 };

            Assert.False(factory.Ssl?.Enabled ?? false);
            Assert.Throws<InvalidOperationException>(
                () => RabbitMQManagerBase<TestChannelSettings>.EnsureTransportIsSecure(
                    factory.Ssl?.Enabled ?? false, "Production"));
        }

        [Fact]
        public void ConnectionFactory_AmqpsUri_ReportsTlsEnabled()
        {
            var factory = new ConnectionFactory
            {
                Uri = new Uri("amqps://user:pass@broker.example:5671/vhost")
            };

            Assert.True(factory.Ssl?.Enabled ?? false);
            RabbitMQManagerBase<TestChannelSettings>.EnsureTransportIsSecure(
                factory.Ssl?.Enabled ?? false, "Production");
        }

        [Fact]
        public void ConnectionFactory_PlaintextAmqpUri_ReportsTlsDisabled()
        {
            var factory = new ConnectionFactory
            {
                Uri = new Uri("amqp://user:pass@broker.example:5672/vhost")
            };

            Assert.False(factory.Ssl?.Enabled ?? false);
            Assert.Throws<InvalidOperationException>(
                () => RabbitMQManagerBase<TestChannelSettings>.EnsureTransportIsSecure(
                    factory.Ssl?.Enabled ?? false, "Production"));
        }
    }
}
