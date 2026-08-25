namespace EvangelionERPV2.Shared.DTOs
{
    /// <summary>
    /// The ficha do cliente: everything about one customer that today takes four screens.
    /// </summary>
    /// <remarks>
    /// Nothing here is stored. Every figure is derived from orders and boletos that already exist,
    /// which is what keeps it from disagreeing with the carteira and the aging report.
    /// </remarks>
    public sealed class CustomerSummaryDTO
    {
        public Guid CustomerId { get; set; }
        public string CustomerName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string PhoneNumber { get; set; } = string.Empty;
        public string? Document { get; set; }
        public string? Municipio { get; set; }
        public string? Uf { get; set; }

        /// <summary>
        /// The money half of the ficha. Null when the caller may read customers but not the
        /// carteira — the identity block above still renders.
        /// </summary>
        public CustomerFinancialSummaryDTO? Financial { get; set; }

        public int OrderCount { get; set; }
        public decimal TotalPurchased { get; set; }

        /// <summary>Total purchased over order count. Null when the customer has never ordered.</summary>
        public decimal? AverageTicket { get; set; }

        public DateTime? FirstPurchaseAtUtc { get; set; }
        public DateTime? LastPurchaseAtUtc { get; set; }

        /// <summary>Days since the last order. The churn signal a salesperson actually reads.</summary>
        public int? DaysSinceLastPurchase { get; set; }

        public IEnumerable<CustomerOrderSummaryDTO> RecentOrders { get; set; } = Array.Empty<CustomerOrderSummaryDTO>();
        public IEnumerable<CustomerTopProductDTO> TopProducts { get; set; } = Array.Empty<CustomerTopProductDTO>();
    }

    public sealed class CustomerFinancialSummaryDTO
    {
        /// <summary>Still owed across every open order. The same figure the credit guard uses.</summary>
        public decimal OpenExposure { get; set; }

        public decimal? CreditLimit { get; set; }

        /// <summary>Limit minus exposure. Null when no limit is set — unlimited, not zero.</summary>
        public decimal? CreditAvailable { get; set; }

        public decimal OverdueAmount { get; set; }
        public decimal NotYetDue { get; set; }
        public decimal Days1To30 { get; set; }
        public decimal Days31To60 { get; set; }
        public decimal Days61To90 { get; set; }
        public decimal Days90Plus { get; set; }
    }

    public sealed class CustomerOrderSummaryDTO
    {
        public Guid OrderId { get; set; }
        public DateTime CreatedAtUtc { get; set; }
        public decimal TotalValue { get; set; }
        public int Status { get; set; }
        public bool IsRefunded { get; set; }

        /// <summary>The live boleto's status, or null when the order has none.</summary>
        public int? BillStatus { get; set; }
        public DateTime? BillDueDate { get; set; }
    }

    public sealed class CustomerTopProductDTO
    {
        public Guid ProductId { get; set; }
        public string ProductName { get; set; } = string.Empty;
        public decimal Quantity { get; set; }
        public decimal Revenue { get; set; }
    }
}
