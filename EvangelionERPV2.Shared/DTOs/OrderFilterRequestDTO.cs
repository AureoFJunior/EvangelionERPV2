namespace EvangelionERPV2.Shared.DTOs
{
    public sealed class OrderFilterRequestDTO
    {
        public Guid? CustomerId { get; set; }
        public int? Status { get; set; }
        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }
        public bool? IsActive { get; set; } = true;

        /// <summary><see cref="Enums.EnumOrderChannel"/>; null for every channel.</summary>
        public int? Channel { get; set; }
    }
}
