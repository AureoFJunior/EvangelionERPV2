namespace EvangelionERPV2.CustomerModule.Application.Interface
{
    public readonly record struct GeocodedPoint(decimal Latitude, decimal Longitude);

    /// <summary>
    /// The parts of a customer's address worth handing a geocoder, in the order they narrow it.
    /// </summary>
    public readonly record struct AddressToLocate(
        string? Logradouro,
        string? Numero,
        string? Bairro,
        string? Municipio,
        string? Uf,
        string? Cep);

    public interface IAddressGeocoder
    {
        /// <summary>
        /// Coordinates for an address, or null when the service has none for it.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The whole address, not the CEP alone: a CEP is not a point. BrasilAPI, which this used
        /// to ask, answers every CEP in Porto Alegre with -30.03283,-51.23019 — the city centroid,
        /// in Centro Histórico — so a customer in Menino Deus landed three kilometres away, on the
        /// wrong side of the ninety percent of radius searches that are smaller than that.
        /// </para>
        /// <para>
        /// Never throws for a failure it can absorb: an address nobody can place, a blank one and
        /// an unreachable service all answer null. The callers use this to decorate a map, and a
        /// map missing a pin is a smaller problem than a request that fails.
        /// </para>
        /// </remarks>
        Task<GeocodedPoint?> ResolveAsync(AddressToLocate address, CancellationToken cancellation = default);
    }
}
