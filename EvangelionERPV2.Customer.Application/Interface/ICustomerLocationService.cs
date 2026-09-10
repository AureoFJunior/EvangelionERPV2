using EvangelionERPV2.Shared.DTOs;

namespace EvangelionERPV2.CustomerModule.Application.Interface
{
    public interface ICustomerLocationService
    {
        /// <summary>
        /// Customers within <paramref name="radiusKm"/> of a point, nearest first — or, when
        /// <paramref name="place"/> is given, the customers of that place instead.
        /// </summary>
        /// <param name="take">Hard cap on rows. Both limits apply: a radius and a count.</param>
        /// <param name="place">
        /// A município, UF or bairro, matched loosely against all three. Supplied, it *replaces*
        /// the radius: a shop asking for Campinas means the whole of Campinas, not the part of it
        /// that happens to fall inside a circle drawn around wherever the map was left. The point
        /// still orders the answer, so the list stays "nearest first" from where the user is.
        /// </param>
        Task<IReadOnlyList<NearbyCustomerDTO>> GetNearbyAsync(
            Guid enterpriseId,
            decimal latitude,
            decimal longitude,
            double radiusKm,
            int take,
            string? place = null,
            CancellationToken cancellation = default);

        /// <summary>
        /// Resolves coordinates for customers that have an address and no location yet.
        /// </summary>
        /// <remarks>
        /// A batch, not a write-path hook: geocoding calls a third party, and a customer must never
        /// fail to save because a public API was down.
        /// </remarks>
        Task<GeocodeBackfillResultDTO> GeocodePendingAsync(
            Guid enterpriseId,
            int take,
            CancellationToken cancellation = default);

        /// <summary>
        /// Puts a customer exactly where somebody says they are.
        /// </summary>
        /// <remarks>
        /// The geocoder cannot finish this job and no free one can: OpenStreetMap has no surveyed
        /// house number on most Brazilian streets, so an address resolves to the middle of its road
        /// and stops there, a couple of hundred metres out. The person who visits the customer knows
        /// the door. This is where they say so.
        ///
        /// Nothing overwrites the result. <see cref="GeocodePendingAsync"/> only looks at customers
        /// with no coordinates at all, so a hand-placed pin survives every later backfill.
        /// </remarks>
        Task<bool> SetLocationAsync(
            Guid enterpriseId,
            Guid customerId,
            decimal latitude,
            decimal longitude,
            CancellationToken cancellation = default);
    }
}
