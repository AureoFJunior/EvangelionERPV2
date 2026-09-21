using EvangelionERPV2.Shared.DTOs;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Enums;

namespace EvangelionERPV2.PdvModule.Application.Interface
{
    /// <summary>
    /// Everything a till asks of the ERP.
    /// </summary>
    public interface IPdvService
    {
        /// <summary>Opens a till session, or returns the one already open for that client id.</summary>
        Task<CashSession> OpenSessionAsync(OpenCashSessionRequestDTO request, Guid enterpriseId, Guid userId);

        /// <summary>Closes it against a count the operator committed to before seeing the expected figure.</summary>
        Task<CashSessionSummaryDTO> CloseSessionAsync(CloseCashSessionRequestDTO request, Guid enterpriseId, Guid userId);

        /// <summary>Records a sangria or a suprimento.</summary>
        Task<CashMovement> RegisterMovementAsync(CashMovementRequestDTO request, Guid enterpriseId, Guid userId);

        /// <summary>
        /// Turns a till sale into an <see cref="Order"/>. Safe to call more than once with the same
        /// <see cref="PdvSaleSyncRequestDTO.ClientSaleId"/>.
        /// </summary>
        Task<PdvSaleSyncResultDTO> SyncSaleAsync(PdvSaleSyncRequestDTO request, Guid enterpriseId, Guid userId);

        Task<CashSessionSummaryDTO> GetSessionSummaryAsync(Guid clientSessionId, Guid enterpriseId, bool includeExpectedCash);

        /// <summary>Products changed since the terminal last asked, for its local mirror.</summary>
        Task<PdvCatalogPageDTO> GetCatalogAsync(DateTime? since, Guid enterpriseId, int pageSize);

        /// <summary>A page of product thumbnails, oldest change first, for a till filling its image cache.</summary>
        Task<PdvProductImagePageDTO> GetProductImagesAsync(DateTime? since, Guid enterpriseId, int pageSize);

        /// <summary>The session currently open on a terminal, if any.</summary>
        Task<CashSession?> GetOpenSessionAsync(string terminalId, Guid enterpriseId);

        /// <summary>One synced sale, by the id the terminal minted for it.</summary>
        Task<PdvSale?> GetSaleAsync(Guid clientSaleId, Guid enterpriseId);

        /// <summary>Every till enrolled for this shop, revoked ones included. Never the credential.</summary>
        Task<IReadOnlyList<PdvTerminalDTO>> GetTerminalsAsync(Guid enterpriseId);

        /// <summary>
        /// Retires one till. Answers false when there was nothing to revoke — already revoked, or
        /// not this tenant's.
        /// </summary>
        Task<bool> RevokeTerminalAsync(Guid terminalId, Guid enterpriseId);

        /// <summary>Every till currently open, so a supervisor can see the shop without asking each one.</summary>
        Task<IReadOnlyList<CashSessionDTO>> GetOpenSessionsAsync(Guid enterpriseId);

        /// <summary>Recent override attempts, newest first. <paramref name="onlyRefused"/> is the interesting case.</summary>
        Task<IReadOnlyList<PdvOverrideAuditDTO>> GetOverrideAuditAsync(
            Guid enterpriseId, DateTime? since, bool? onlyRefused, int take);

        /// <summary>Raises a PIX charge for a basket, or returns the one already raised for it.</summary>
        Task<PdvChargeDTO> StartPixChargeAsync(StartPixChargeRequestDTO request, Guid enterpriseId);

        /// <summary>The charge as we last recorded it. Reads our own row; never calls the provider.</summary>
        Task<PdvChargeDTO?> GetChargeAsync(Guid chargeId, Guid enterpriseId);

        /// <summary>Abandons a charge nobody paid. A charge that already settled is left alone.</summary>
        Task<PdvChargeDTO> CancelPixChargeAsync(Guid chargeId, Guid enterpriseId);

        /// <summary>
        /// Moves a charge to what the provider says it now is. False when nothing changed.
        /// </summary>
        Task<bool> ApplyChargeEventAsync(string eventId, string providerIntentId, int status,
            string? endToEndId, decimal? feeAmount, DateTime? paidAt);
    }
}
