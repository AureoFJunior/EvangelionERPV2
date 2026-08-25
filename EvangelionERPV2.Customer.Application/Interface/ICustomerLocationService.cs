using EvangelionERPV2.Shared.DTOs;

namespace EvangelionERPV2.CustomerModule.Application.Interface
{
    public interface ICustomerLocationService
    {
        /// <summary>
        /// Customers within <paramref name="radiusKm"/> of a point, nearest first.
        /// </summary>
        /// <param name="take">Hard cap on rows. Both limits apply: a radius and a count.</param>
        Task<IReadOnlyList<NearbyCustomerDTO>> GetNearbyAsync(
            Guid enterpriseId,
            decimal latitude,
            decimal longitude,
            double radiusKm,
            int take,
            CancellationToken cancellation = default);

        /// <summary>
        /// Resolves coordinates for customers that have a CEP and no location yet.
        /// </summary>
        /// <remarks>
        /// A batch, not a write-path hook: geocoding calls a third party, and a customer must never
        /// fail to save because a public API was down.
        /// </remarks>
        Task<GeocodeBackfillResultDTO> GeocodePendingAsync(
            Guid enterpriseId,
            int take,
            CancellationToken cancellation = default);
    }
}
