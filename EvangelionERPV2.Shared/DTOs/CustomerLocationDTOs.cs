namespace EvangelionERPV2.Shared.DTOs
{
    public sealed class NearbyCustomerDTO
    {
        public Guid CustomerId { get; set; }
        public string CustomerName { get; set; } = string.Empty;
        public string PhoneNumber { get; set; } = string.Empty;
        public string? Municipio { get; set; }
        public string? Uf { get; set; }
        public decimal Latitude { get; set; }
        public decimal Longitude { get; set; }

        /// <summary>Straight-line distance from the point that was searched from.</summary>
        public double DistanceKm { get; set; }
    }

    /// <summary>What one backfill pass managed to locate.</summary>
    public sealed class GeocodeBackfillResultDTO
    {
        public int Examined { get; set; }
        public int Located { get; set; }

        /// <summary>
        /// Customers still without coordinates after this pass — either their CEP is unknown to the
        /// service or they have no CEP at all. A caller loops until this stops falling.
        /// </summary>
        public int Remaining { get; set; }
    }
}
