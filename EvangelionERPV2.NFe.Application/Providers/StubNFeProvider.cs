using System.Text;
using EvangelionERPV2.NFeModule.Infra.Providers;
using EvangelionERPV2.Shared.Entities;

namespace EvangelionERPV2.NFeModule.Application.Providers
{
    /// <summary>
    /// Placeholder transport. Fabricates an authorization without contacting anybody, and is
    /// deleted in phase 9 (docs/fiscal/SPEC-remaining-code.md §2.6). It is only safe because
    /// NFeSettings.Enabled is false in every environment.
    /// </summary>
    public sealed class StubNFeProvider : INFeProvider
    {
        public Task<NFeProviderResult> IssueAsync(NFeIssueContext context, CancellationToken cancellationToken = default)
        {
            var issuedAt = DateTime.UtcNow;

            var xmlBuilder = new StringBuilder();
            xmlBuilder.AppendLine("<nfeStub>");
            xmlBuilder.AppendLine($"  <type>{context.Type}</type>");
            xmlBuilder.AppendLine($"  <orderId>{context.Order.Id}</orderId>");
            xmlBuilder.AppendLine($"  <issuedAt>{issuedAt:O}</issuedAt>");
            xmlBuilder.AppendLine("</nfeStub>");

            return Task.FromResult(new NFeProviderResult
            {
                AccessKey = context.AccessKey,
                Protocol = $"HOMOLOG-{issuedAt:yyyyMMddHHmmss}",
                XmlContent = xmlBuilder.ToString(),
                IssuedAt = issuedAt,
                TotalValue = context.Order.TotalValue,
                Status = NFeStatus.Authorized,
                Cstat = "100",
                XMotivo = "Autorizado o uso da NF-e"
            });
        }

        public Task<NFeProviderResult> TransmitSignedAsync(
            string accessKey,
            string signedXml,
            EnterpriseFiscalSettings fiscalSettings,
            NFeDocumentType type,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new NFeProviderResult
            {
                AccessKey = accessKey,
                Status = NFeStatus.Error,
                XMotivo = "The stub provider transmits nothing.",
                NeverTransmitted = true
            });

        public Task<NFeProviderResult> ConsultAsync(
            string accessKey, EnterpriseFiscalSettings fiscalSettings, CancellationToken cancellationToken = default) =>
            Task.FromResult(new NFeProviderResult
            {
                AccessKey = accessKey,
                Status = NFeStatus.Pending
            });

        public Task<NFeProviderResult> CancelAsync(
            string accessKey,
            string protocol,
            string reason,
            EnterpriseFiscalSettings fiscalSettings,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new NFeProviderResult
            {
                AccessKey = accessKey,
                Status = NFeStatus.Pending,
                XMotivo = "Cancellation is not available: no fiscal transport is configured.",
                NeverTransmitted = true
            });

        public Task<NFeProviderResult> GetServiceStatusAsync(
            EnterpriseFiscalSettings fiscalSettings, CancellationToken cancellationToken = default) =>
            Task.FromResult(new NFeProviderResult
            {
                Status = NFeStatus.Pending,
                XMotivo = "No fiscal transport is configured.",
                NeverTransmitted = true
            });
    }
}
