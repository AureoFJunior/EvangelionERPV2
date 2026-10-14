using System.Security.Cryptography;
using System.Text;
using EvangelionERPV2.NFeModule.Application.Configs;
using EvangelionERPV2.NFeModule.Application.Models;
using EvangelionERPV2.Shared.Entities;

namespace EvangelionERPV2.NFeModule.Application.Providers
{
    public sealed class StubNFeProvider : INFeProvider
    {
        public Task<NFeProviderResult> IssueAsync(Order order, Enterprise? enterprise, Customer? customer, NFeDocumentType type, NFeSettings settings)
        {
            var accessKey = BuildNumericString(44);
            var series = string.IsNullOrWhiteSpace(settings.Series) ? "1" : settings.Series;
            var number = BuildNumericString(9);
            var environment = string.IsNullOrWhiteSpace(settings.Environment) ? "Homologation" : settings.Environment;
            var issuedAt = DateTime.UtcNow;

            var xmlBuilder = new StringBuilder();
            xmlBuilder.AppendLine("<nfeStub>");
            xmlBuilder.AppendLine($"  <type>{type}</type>");
            xmlBuilder.AppendLine($"  <accessKey>{accessKey}</accessKey>");
            xmlBuilder.AppendLine($"  <orderId>{order.Id}</orderId>");
            xmlBuilder.AppendLine($"  <issuedAt>{issuedAt:O}</issuedAt>");
            xmlBuilder.AppendLine($"  <environment>{environment}</environment>");
            xmlBuilder.AppendLine("</nfeStub>");

            var result = new NFeProviderResult
            {
                AccessKey = accessKey,
                Protocol = $"HOMOLOG-{issuedAt:yyyyMMddHHmmss}",
                XmlContent = xmlBuilder.ToString(),
                Number = number,
                Series = series,
                Environment = environment,
                IssuedAt = issuedAt,
                TotalValue = order.TotalValue,
                Status = NFeStatus.Authorized
            };

            return Task.FromResult(result);
        }

        private static string BuildNumericString(int length)
        {
            if (length <= 0)
                return "";

            var bytes = new byte[length];
            RandomNumberGenerator.Fill(bytes);

            var chars = new char[length];
            for (var i = 0; i < length; i++)
            {
                chars[i] = (char)('0' + (bytes[i] % 10));
            }

            return new string(chars);
        }
    }
}
