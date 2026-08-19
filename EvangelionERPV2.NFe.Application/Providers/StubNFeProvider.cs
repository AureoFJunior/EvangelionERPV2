using System.Text;
using EvangelionERPV2.NFeModule.Application.Configs;
using EvangelionERPV2.NFeModule.Application.Models;
using EvangelionERPV2.Shared.Entities;

namespace EvangelionERPV2.NFeModule.Application.Providers
{
    /// <summary>
    /// Placeholder transport. Fabricates an authorization without contacting anybody, and is
    /// deleted in phase 9 (docs/fiscal/SPEC-remaining-code.md §2.6). It is only safe because
    /// NFeSettings.Enabled is false in every environment.
    /// </summary>
    /// <remarks>
    /// It no longer invents an access key, number or series: those are allocated and committed by
    /// NFeService before transmission, and a second, contradictory set coming back from the
    /// transport was the thing the write-ahead ordering exists to prevent.
    /// </remarks>
    public sealed class StubNFeProvider : INFeProvider
    {
        public Task<NFeProviderResult> IssueAsync(Order order, Enterprise? enterprise, Customer? customer, NFeDocumentType type, NFeSettings settings)
        {
            var issuedAt = DateTime.UtcNow;

            var xmlBuilder = new StringBuilder();
            xmlBuilder.AppendLine("<nfeStub>");
            xmlBuilder.AppendLine($"  <type>{type}</type>");
            xmlBuilder.AppendLine($"  <orderId>{order.Id}</orderId>");
            xmlBuilder.AppendLine($"  <issuedAt>{issuedAt:O}</issuedAt>");
            xmlBuilder.AppendLine("</nfeStub>");

            var result = new NFeProviderResult
            {
                Protocol = $"HOMOLOG-{issuedAt:yyyyMMddHHmmss}",
                XmlContent = xmlBuilder.ToString(),
                IssuedAt = issuedAt,
                TotalValue = order.TotalValue,
                Status = NFeStatus.Authorized
            };

            return Task.FromResult(result);
        }

        /// <remarks>
        /// The stub has no memory of what it "transmitted", so it cannot answer for an access key.
        /// It returns the honest answer — still unknown — which leaves the document Pending for a
        /// human instead of inventing an authorization for a note that was never sent anywhere.
        /// </remarks>
        public Task<NFeProviderResult> ConsultAsync(string accessKey, NFeSettings settings) =>
            Task.FromResult(new NFeProviderResult
            {
                AccessKey = accessKey,
                Status = NFeStatus.Pending
            });
    }
}
