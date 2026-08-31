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
    /// <remarks>
    /// It invents no identity: the chave, number and series are allocated and committed by
    /// NFeService before transmission, and a second, contradictory set coming back from the
    /// transport was the thing the write-ahead ordering exists to prevent.
    /// </remarks>
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

        /// <remarks>
        /// The stub has no memory of what it "transmitted", so it cannot answer for a chave. It
        /// returns the honest answer — still unknown — which leaves the document Pending for a human
        /// instead of inventing an authorization for a note that was never sent anywhere.
        /// </remarks>
        /// <remarks>
        /// Same honesty as <see cref="ConsultAsync"/>: the stub transmits nothing, so it reports
        /// that nothing was transmitted. Claiming success would mark a contingency note as settled
        /// with SEFAZ while it still owed one.
        /// </remarks>
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

        /// <remarks>
        /// Reports failure rather than success. A stub that "cancelled" a note would write
        /// Cancelled into a database while the note stayed valid at SEFAZ, which is the single most
        /// expensive lie this module could tell.
        /// </remarks>
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
