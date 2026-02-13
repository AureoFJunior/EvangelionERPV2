using EvangelionERPV2.NFeModule.Application.Configs;
using EvangelionERPV2.NFeModule.Application.Models;
using EvangelionERPV2.Shared.Entities;

namespace EvangelionERPV2.NFeModule.Application.Providers
{
    public interface INFeProvider
    {
        Task<NFeProviderResult> IssueAsync(Order order, Enterprise? enterprise, Customer? customer, NFeDocumentType type, NFeSettings settings);
    }
}
