using EvangelionERPV2.Shared.DTOs;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Enums;

namespace EvangelionERPV2.PdvModule.Application.Interface
{
    /// <summary>
    /// Everything a till asks of the ERP.
    /// </summary>
    /// <remarks>
    /// No repository interface sits under this. The module owns no entity of its own that needs a
    /// custom query language — it composes <c>OrderService</c>, <c>ProductService</c> and the
    /// generic <c>IRepository&lt;T&gt;</c> — so a per-entity repository contract here would be an
    /// interface with one implementation and no callers but this file.
    /// </remarks>
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

        /// <summary>Raises a PIX charge for a basket, or returns the one already raised for it.</summary>
        Task<PdvChargeDTO> StartPixChargeAsync(StartPixChargeRequestDTO request, Guid enterpriseId);

        /// <summary>The charge as we last recorded it. Reads our own row; never calls the provider.</summary>
        Task<PdvChargeDTO?> GetChargeAsync(Guid chargeId, Guid enterpriseId);

        /// <summary>Abandons a charge nobody paid. A charge that already settled is left alone.</summary>
        Task<PdvChargeDTO> CancelPixChargeAsync(Guid chargeId, Guid enterpriseId);

        /// <summary>
        /// Moves a charge to what the provider says it now is. False when nothing changed.
        /// </summary>
        /// <remarks>
        /// No enterprise id: the tenant comes from the charge row the intent id matches, never from
        /// the caller. A webhook that could be told which tenant it concerned would be a webhook
        /// that could move another shop's charges.
        /// </remarks>
        Task<bool> ApplyChargeEventAsync(string eventId, string providerIntentId, int status,
            string? endToEndId, decimal? feeAmount, DateTime? paidAt);
    }
}
