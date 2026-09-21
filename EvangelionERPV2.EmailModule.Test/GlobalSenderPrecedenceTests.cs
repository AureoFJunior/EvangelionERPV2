using EvangelionERPV2.Shared.Entities;
using Microsoft.Extensions.Configuration;

namespace EvangelionERPV2.EmailModule.Test
{
    /// <summary>
    /// Pins which mailbox the application sends from.
    /// </summary>
    public class GlobalSenderPrecedenceTests
    {
        [Fact]
        public void ConfiguredAccount_ComesBefore_StoredTenantRows()
        {
            var stored = new List<Email>
            {
                new() { HostName = "tenant.smtp.example", UserName = "tenant@example.com", Port = 25, IsActive = true }
            };

            var ordered = BuildCandidates(stored, ConfigurationWith("smtp.gmail.com", "ops@example.com", "587"));

            Assert.Equal("smtp.gmail.com", ordered[0].HostName);
            Assert.Equal("tenant.smtp.example", ordered[1].HostName);
        }

        /// <summary>
        /// Clearing the configuration has to put a tenant back in charge, or this change would be
        /// irreversible without a code edit.
        /// </summary>
        [Fact]
        public void WithoutConfiguration_TenantRowIsUsed()
        {
            var stored = new List<Email>
            {
                new() { HostName = "tenant.smtp.example", UserName = "tenant@example.com", Port = 25, IsActive = true }
            };

            var ordered = BuildCandidates(stored, ConfigurationWith(null, null, null));

            Assert.Single(ordered);
            Assert.Equal("tenant.smtp.example", ordered[0].HostName);
        }

        [Fact]
        public void PortDefaultsTo587_WhenConfigurationOmitsIt()
        {
            var ordered = BuildCandidates([], ConfigurationWith("smtp.gmail.com", "ops@example.com", null));

            Assert.Equal(587, ordered[0].Port);
        }

        private static List<Email> BuildCandidates(List<Email> stored, IConfiguration configuration)
        {
            var candidates = new List<Email>();
            var section = configuration.GetSection("EmailSettings");
            var host = section["HostName"];
            var user = section["Username"];

            if (!string.IsNullOrWhiteSpace(host) && !string.IsNullOrWhiteSpace(user))
            {
                _ = int.TryParse(section["Port"], out var port);
                candidates.Add(new Email
                {
                    HostName = host,
                    UserName = user,
                    Password = section["Password"] ?? string.Empty,
                    Port = port == 0 ? 587 : port,
                    IsActive = true
                });
            }

            foreach (var row in stored)
            {
                var duplicate = candidates.Any(x =>
                    string.Equals(x.HostName?.Trim(), row.HostName?.Trim(), StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(x.UserName?.Trim(), row.UserName?.Trim(), StringComparison.OrdinalIgnoreCase));

                if (!duplicate)
                    candidates.Add(row);
            }

            return candidates;
        }

        private static IConfiguration ConfigurationWith(string? host, string? user, string? port)
        {
            return new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["EmailSettings:HostName"] = host,
                    ["EmailSettings:Username"] = user,
                    ["EmailSettings:Port"] = port,
                    ["EmailSettings:Password"] = "irrelevant"
                })
                .Build();
        }
    }
}
