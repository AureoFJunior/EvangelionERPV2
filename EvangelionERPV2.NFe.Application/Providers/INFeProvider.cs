using EvangelionERPV2.NFeModule.Application.Configs;
using EvangelionERPV2.NFeModule.Application.Models;
using EvangelionERPV2.Shared.Entities;

namespace EvangelionERPV2.NFeModule.Application.Providers
{
    public interface INFeProvider
    {
        Task<NFeProviderResult> IssueAsync(Order order, Enterprise? enterprise, Customer? customer, NFeDocumentType type, NFeSettings settings);

        /// <summary>
        /// Asks SEFAZ what became of an already transmitted access key. This is the only legal way
        /// to resolve a document whose outcome is unknown — retransmitting it double-emits.
        /// </summary>
        /// <returns>
        /// A result whose <see cref="NFeProviderResult.Status"/> is still
        /// <see cref="NFeStatus.Pending"/> when SEFAZ has no answer yet; anything else is final.
        /// </returns>
        Task<NFeProviderResult> ConsultAsync(string accessKey, NFeSettings settings);
    }
}
