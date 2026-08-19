using EvangelionERPV2.Shared.Entities;

namespace EvangelionERPV2.NFeModule.Application.Interface
{
    public interface INFeFiscalSettingsService
    {
        /// <summary>
        /// Returns the tenant's fiscal profile, or a transient defaulted instance when the tenant
        /// has never saved one. Never returns null, so callers do not branch on first use.
        /// </summary>
        Task<EnterpriseFiscalSettings> GetSettingsAsync(Guid enterpriseId);

        /// <summary>
        /// Creates or updates the tenant's fiscal profile and ensures its number-sequence rows
        /// exist. Validates every field that SEFAZ would otherwise reject.
        /// </summary>
        Task<EnterpriseFiscalSettings> UpsertSettingsAsync(Guid enterpriseId, EnterpriseFiscalSettings settings);
    }
}
