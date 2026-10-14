using EvangelionERPV2.StorefrontModule.Application.DTOs;

namespace EvangelionERPV2.StorefrontModule.Application.Interface
{
    /// <summary>
    /// Limen's quote, checkout and "Meus pedidos", answered from the ERP's own rows (spec §4.4, §14).
    /// Until Stripe is connected, payment is settled only by the test-mode simulator, and only while
    /// <c>Storefront:SimulatePayments</c> is on.
    /// </summary>
    public interface IStorefrontCheckoutService
    {
        bool SimulationEnabled { get; }

        Task<StorefrontQuoteDTO> QuoteAsync(IReadOnlyList<StorefrontCartLineDTO> lines);

        /// <summary>Throws <see cref="InvalidOperationException"/> when no payment method is available.</summary>
        Task<StorefrontCheckoutResultDTO> CreateAsync(StorefrontCheckoutRequestDTO request);

        Task<StorefrontCheckoutDTO?> GetAsync(Guid id);

        Task<StorefrontCheckoutDTO?> RenewPixAsync(Guid id);

        Task<StorefrontCheckoutDTO?> SimulateAsync(Guid id, StorefrontSimulatedOutcome outcome);

        Task<IReadOnlyList<StorefrontPurchaseDTO>> ListPurchasesAsync(Guid shopperId);

        Task<IReadOnlyList<StorefrontPurchaseContactDTO>> ListPurchaseContactsAsync(Guid shopperId);

        Task<StorefrontPurchaseDTO?> GetPurchaseAsync(Guid checkoutId);

        Task<bool> AttachShopperAsync(Guid checkoutId, Guid shopperId);

        Task<int> AttachGuestPurchasesAsync(string email, Guid shopperId);

        Task DetachShopperAsync(Guid shopperId);

        Task<Guid?> OwnerOfAsync(Guid checkoutId);

        /// <summary>Null when the order is not in that checkout; the order unchanged when the window is closed.</summary>
        Task<StorefrontOrderDTO?> RequestWithdrawalAsync(Guid checkoutId, string orderNumber, Guid actorUserId);

        Task<StorefrontOrderDTO?> SimulateStepAsync(Guid checkoutId, string orderNumber, StorefrontSimulatedOrderStep step);

        Task<StorefrontInvoiceDTO?> GetInvoiceAsync(Guid checkoutId, string orderNumber);
    }
}
