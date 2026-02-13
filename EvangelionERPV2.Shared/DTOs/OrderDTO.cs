using EvangelionERPV2.Shared.Entities;

namespace EvangelionERPV2.Shared.DTOs
{
    public sealed class OrderDTO : BaseDTO
    {
        public DateTime? Payday { get; set; } = null;
        public DateTime PaymentScheduledDate { get; set; } = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 01, 00, 00, 00).AddMonths(1);
        public double TotalValue { get; set; } = 0;
        public Guid? EnterpriseId { get; set; } = null;
        public Guid? CustomerId { get; set; } = null;
        public string? CustomerName { get; set; } = null;
        public IEnumerable<OrderedProduct>? OrderedProduct { get; set; } = null;

    }
}
