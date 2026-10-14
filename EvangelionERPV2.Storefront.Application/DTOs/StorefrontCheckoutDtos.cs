namespace EvangelionERPV2.StorefrontModule.Application.DTOs
{
    // The wire shapes Storefront.Api reads (its Limen.Checkout.Domain DTOs): property and enum member
    // names are the contract, so rename nothing here without renaming it there.

    public sealed record StorefrontCartLineDTO(Guid ProductId, int Quantity);

    /// <summary><paramref name="Issue"/>: null, "gone", "out-of-stock" or "quantity-reduced".</summary>
    /// <summary>
    /// <see cref="HasPicture"/> is the row's own photo, <see cref="FamilyPictureId"/> its variant family's,
    /// as on catalog cards. Both are captured when the cart is priced, so a stored purchase keeps them.
    /// </summary>
    public sealed record StorefrontQuoteLineDTO(
        Guid ProductId,
        string Name,
        decimal UnitPrice,
        int Quantity,
        decimal LineTotal,
        string? Issue,
        bool HasPicture = false,
        Guid? FamilyPictureId = null);

    public sealed record StorefrontQuoteStoreDTO(
        string Slug,
        string Name,
        IReadOnlyList<StorefrontQuoteLineDTO> Lines,
        decimal Subtotal,
        decimal Shipping,
        int DeliveryDaysMin,
        int DeliveryDaysMax,
        Guid? EnterpriseId = null);

    /// <summary><paramref name="Installments"/>: every card plan offered for <paramref name="Total"/>, 1× first (SPEC-limen-pricing-rules §3).</summary>
    public sealed record StorefrontQuoteDTO(
        IReadOnlyList<StorefrontQuoteStoreDTO> Stores,
        decimal Subtotal,
        decimal Shipping,
        decimal Total,
        IReadOnlyList<StorefrontInstallmentDTO>? Installments = null);

    /// <summary>
    /// One card plan, with what CDC art. 52 requires next to it: <paramref name="Count"/> × <paramref name="Value"/>,
    /// <paramref name="Total"/> charged, monthly rate and CET a year, in percent. 1× has no interest.
    /// </summary>
    public sealed record StorefrontInstallmentDTO(int Count, decimal Value, decimal Total, decimal MonthlyRate, decimal CetAnnual);

    public sealed record StorefrontQuoteRequestDTO(IReadOnlyList<StorefrontCartLineDTO> Lines);

    public sealed record StorefrontCustomerDTO(string Email, string FullName, string Cpf, string Phone, bool MarketingOptIn);

    /// <summary><paramref name="IbgeCode"/>: the city's 7-digit IBGE code from the CEP lookup, when the shopper used it (NFe cMun).</summary>
    public sealed record StorefrontAddressDTO(string Cep, string Street, string? Number, string? Complement, string District, string City, string Uf, string? IbgeCode = null);

    public enum StorefrontPaymentMethod
    {
        Card,
        Pix
    }

    /// <summary><paramref name="ShopperId"/> is set by Storefront.Api from its signed-in session.</summary>
    public sealed record StorefrontCheckoutRequestDTO(
        IReadOnlyList<StorefrontCartLineDTO> Lines,
        StorefrontCustomerDTO Customer,
        StorefrontAddressDTO Address,
        StorefrontPaymentMethod PaymentMethod,
        decimal ExpectedTotal,
        Guid? ShopperId,
        int Installments = 1);

    public enum StorefrontCheckoutStatus
    {
        AwaitingPayment,
        Paid,
        Declined,
        Expired,
        /// <summary>Paid, and at least one store's order is still being created (a retry is due).</summary>
        Settling
    }

    public sealed record StorefrontPixDTO(string CopyPasteCode, DateTime ExpiresAt);

    public enum StorefrontOutcomeKind
    {
        Ordered,
        RefundedOutOfStock
    }

    public sealed record StorefrontOutcomeDTO(
        string Slug,
        string Name,
        StorefrontOutcomeKind Outcome,
        string? OrderNumber,
        decimal Amount,
        int DeliveryDaysMin,
        int DeliveryDaysMax,
        string ContactEmail,
        IReadOnlyList<StorefrontQuoteLineDTO> Lines);

    public sealed record StorefrontCheckoutDTO(
        Guid Id,
        StorefrontCheckoutStatus Status,
        StorefrontPaymentMethod PaymentMethod,
        decimal Total,
        string Email,
        DateTime CreatedAt,
        StorefrontPixDTO? Pix,
        string? DeclineCode,
        StorefrontQuoteDTO Quote,
        IReadOnlyList<StorefrontOutcomeDTO> Outcomes,
        bool Simulated,
        string CustomerName,
        StorefrontInstallmentDTO? Installment = null);

    /// <summary>Created, or refused because the price moved — then <c>ChangedQuote</c> says to what.</summary>
    public sealed record StorefrontCheckoutResultDTO(StorefrontCheckoutDTO? Checkout, StorefrontQuoteDTO? ChangedQuote);

    public enum StorefrontSimulatedOutcome
    {
        Approve,
        Decline,
        InsufficientFunds,
        PixPaid,
        PixExpire,
        OutOfStock
    }

    public sealed record StorefrontSimulateRequestDTO(StorefrontSimulatedOutcome Outcome);

    public enum StorefrontOrderStatus
    {
        Paid,
        Processing,
        Shipped,
        Delivered,
        Refunded
    }

    /// <summary>
    /// One store's order as the shopper sees it (spec §7.4). <paramref name="InvoiceAccessKey"/> is the
    /// 44-digit key of its authorized NFe, when the store issued one.
    /// </summary>
    public sealed record StorefrontOrderDTO(
        string OrderNumber,
        Guid CheckoutId,
        string StoreSlug,
        string StoreName,
        string ContactEmail,
        StorefrontOrderStatus Status,
        IReadOnlyList<StorefrontQuoteLineDTO> Lines,
        decimal Amount,
        int DeliveryDaysMin,
        int DeliveryDaysMax,
        StorefrontPaymentMethod PaymentMethod,
        string DeliveryCity,
        string DeliveryUf,
        DateTime PaidAt,
        DateTime? ShippedAt,
        DateTime? DeliveredAt,
        string? TrackingCode,
        string? TrackingUrl,
        DateTime? WithdrawalRequestedAt,
        string? WithdrawalProtocol,
        bool CanWithdraw,
        string? InvoiceAccessKey);

    public sealed record StorefrontPurchaseDTO(
        Guid CheckoutId,
        DateTime PaidAt,
        decimal Total,
        StorefrontPaymentMethod PaymentMethod,
        IReadOnlyList<StorefrontOrderDTO> Orders,
        IReadOnlyList<StorefrontOutcomeDTO> Refunds,
        StorefrontInstallmentDTO? Installment = null);

    public sealed record StorefrontPurchaseContactDTO(Guid CheckoutId, StorefrontCustomerDTO Customer, StorefrontAddressDTO Address);

    public sealed record StorefrontAttachRequestDTO(Guid ShopperId);

    public sealed record StorefrontAttachByEmailRequestDTO(string Email, Guid ShopperId);

    public sealed record StorefrontOwnerDTO(Guid? ShopperId);

    public sealed record StorefrontOrderRefDTO(Guid CheckoutId, string OrderNumber);

    public enum StorefrontSimulatedOrderStep
    {
        Processing,
        Shipped,
        Delivered
    }

    public sealed record StorefrontSimulateStepRequestDTO(Guid CheckoutId, string OrderNumber, StorefrontSimulatedOrderStep Step);

    public sealed record StorefrontInvoiceDTO(string AccessKey, byte[] Xml);
}
