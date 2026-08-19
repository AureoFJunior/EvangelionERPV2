using EvangelionERPV2.Shared.Entities;

namespace EvangelionERPV2.NFeModule.Infra.Providers
{
    /// <summary>
    /// The boundary between this system and SEFAZ. Exactly one implementation may name a fiscal
    /// library type; everything above this interface stays transport-agnostic.
    /// </summary>
    public interface INFeProvider
    {
        /// <summary>Builds, signs and transmits the note described by <paramref name="context"/>.</summary>
        Task<NFeProviderResult> IssueAsync(NFeIssueContext context, CancellationToken cancellationToken = default);

        /// <summary>
        /// Asks SEFAZ what became of an already transmitted chave. The only legal way to resolve a
        /// document whose outcome is unknown — retransmitting it double-emits.
        /// </summary>
        Task<NFeProviderResult> ConsultAsync(
            string accessKey, EnterpriseFiscalSettings fiscalSettings, CancellationToken cancellationToken = default);

        /// <summary>Sends event 110111 (cancelamento) and reports SEFAZ's answer.</summary>
        Task<NFeProviderResult> CancelAsync(
            string accessKey,
            string protocol,
            string reason,
            EnterpriseFiscalSettings fiscalSettings,
            CancellationToken cancellationToken = default);

        /// <summary>StatusServico. Used to tell "SEFAZ is down" from "our request was wrong".</summary>
        Task<NFeProviderResult> GetServiceStatusAsync(
            EnterpriseFiscalSettings fiscalSettings, CancellationToken cancellationToken = default);
    }
}
