namespace EvangelionERPV2.CustomerModule.Application.Interface
{
    public readonly record struct GeocodedPoint(decimal Latitude, decimal Longitude);

    public interface ICepGeocoder
    {
        /// <summary>
        /// Coordinates for a CEP, or null when the service has none for it.
        /// </summary>
        /// <remarks>
        /// Never throws for a failure it can absorb: a CEP with no coordinates, a malformed CEP and
        /// an unreachable service all answer null. The callers use this to decorate a map, and a map
        /// missing a pin is a smaller problem than a request that fails.
        /// </remarks>
        Task<GeocodedPoint?> ResolveAsync(string? cep, CancellationToken cancellation = default);
    }
}
