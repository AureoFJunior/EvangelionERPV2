using EvangelionERPV2.Shared.Entities.RabbitMQ;
using EvangelionERPV2.Shared.Utils;

namespace EvangelionERPV2.Test.Security
{
    /// <summary>
    /// The broker link was on amqps only because a secret happened to say so - nothing required it.
    /// These pin the requirement so a plaintext URI cannot be introduced silently.
    /// </summary>
    public class RabbitMqSchemeEnforcementTests
    {
        private sealed class TestChannelSettings : BaseChannelSettings { }

        [Theory]
        [InlineData("Production")]
        [InlineData("Staging")]
        [InlineData("production")]
        public void EnsureSchemeIsAllowed_PlaintextOutsideDevelopment_Throws(string environment)
        {
            var exception = Assert.Throws<InvalidOperationException>(
                () => RabbitMQManagerBase<TestChannelSettings>.EnsureSchemeIsAllowed("amqp", environment));

            Assert.Contains("amqps", exception.Message);
            // The message has to say why, or the next person just switches it back.
            Assert.Contains("cleartext", exception.Message);
        }

        [Fact]
        public void EnsureSchemeIsAllowed_UnsetEnvironment_TreatedAsProduction()
        {
            // "I do not know where I am" must not be the permissive branch.
            Assert.Throws<InvalidOperationException>(
                () => RabbitMQManagerBase<TestChannelSettings>.EnsureSchemeIsAllowed("amqp", null));

            Assert.Throws<InvalidOperationException>(
                () => RabbitMQManagerBase<TestChannelSettings>.EnsureSchemeIsAllowed("amqp", string.Empty));
        }

        [Theory]
        [InlineData("Development")]
        [InlineData("development")]
        public void EnsureSchemeIsAllowed_PlaintextInDevelopment_IsAllowed(string environment)
        {
            // A local broker with no certificate is a normal thing to develop against.
            RabbitMQManagerBase<TestChannelSettings>.EnsureSchemeIsAllowed("amqp", environment);
        }

        [Theory]
        [InlineData("Production")]
        [InlineData("Development")]
        [InlineData(null)]
        public void EnsureSchemeIsAllowed_Amqps_IsAlwaysAllowed(string? environment)
        {
            RabbitMQManagerBase<TestChannelSettings>.EnsureSchemeIsAllowed("amqps", environment);
            RabbitMQManagerBase<TestChannelSettings>.EnsureSchemeIsAllowed("AMQPS", environment);
        }
    }
}
