namespace EvangelionERPV2.Shared.DTOs
{
    public sealed class NearbyCustomerDTO
    {
        public Guid CustomerId { get; set; }
        public string CustomerName { get; set; } = string.Empty;
        public string PhoneNumber { get; set; } = string.Empty;
        public string? Bairro { get; set; }
        public string? Municipio { get; set; }
        public string? Uf { get; set; }

        /// <summary>
        /// Null when the customer has never been geocoded. A radius search cannot return one of
        /// those — it filters on the coordinates — but a search by place can, and it should: a
        /// customer nobody could pin is exactly the one whose absence from the map is confusing.
        /// </summary>
        public decimal? Latitude { get; set; }
        public decimal? Longitude { get; set; }

        /// <summary>
        /// Straight-line distance from the point that was searched from. Null for a customer with
        /// no coordinates, which sorts last.
        /// </summary>
        public double? DistanceKm { get; set; }
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

    /// <summary>Where somebody says a customer actually is.</summary>
    public sealed class SetCustomerLocationRequestDTO
    {
        public Guid CustomerId { get; set; }
        public decimal Latitude { get; set; }
        public decimal Longitude { get; set; }
    }
}
