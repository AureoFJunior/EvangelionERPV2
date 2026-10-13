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
        Task<GeocodedPoint?> ResolveAsync(AddressToLocate address, CancellationToken cancellation = default);
    }
}
