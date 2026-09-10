using System.Globalization;
using System.Text.Json;
using EvangelionERPV2.CustomerModule.Application.Interface;
using Microsoft.Extensions.Caching.Distributed;
using Serilog;

namespace EvangelionERPV2.CustomerModule.Application.Services
{
    /// <summary>
    /// Turns a customer's address into coordinates using Nominatim, OpenStreetMap's own geocoder.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Nominatim rather than a CEP service because a CEP service does not answer the question. Every
    /// CEP-to-point API this had access to resolves through the same open dataset, and for most
    /// Brazilian cities that dataset holds one point per city: BrasilAPI answers 90160-150 (Menino
    /// Deus), 90020-000 (Centro Histórico) and 91910-000 (Tristeza) with the identical pair. Passing
    /// the street instead is what separates them, and the structured address is now stored.
    /// </para>
    /// <para>
    /// No key and no account, and the map already renders OpenStreetMap tiles, so this adds a
    /// dependency the product had taken on anyway. Their usage policy asks for an identifying
    /// User-Agent (set in CustomerIoC) and at most one request a second, which is the gate below.
    /// </para>
    /// <para>
    /// ponytail: one request a second, serialised process-wide. A 50-customer backfill therefore
    /// takes about a minute of wall clock; it is a rare batch and the cache keeps a re-run instant.
    /// Move to a keyed provider if this ever needs to geocode faster than a person clicks.
    /// </para>
    /// </remarks>
    public sealed class NominatimAddressGeocoder : IAddressGeocoder
    {
        // The trailing number is the shape of the answer, not of the request: a cached point was
        // produced by whatever ParseBestPoint did at the time, so changing how a result is chosen
        // has to leave the old points behind. Bump it whenever that method's output could move.
        private const string CacheKeyPrefix = "Geocode:Address:2:";
        private static readonly TimeSpan CacheTtl = TimeSpan.FromDays(30);
        private const string NoCoordinatesMarker = "-";

        // Nominatim's usage policy: an absolute maximum of one request per second, from everybody.
        private static readonly TimeSpan MinimumInterval = TimeSpan.FromSeconds(1);
        private static readonly SemaphoreSlim Throttle = new(1, 1);
        private static DateTime _lastRequestUtc = DateTime.MinValue;

        private readonly HttpClient _httpClient;
        private readonly IDistributedCache _cache;

        public NominatimAddressGeocoder(HttpClient httpClient, IDistributedCache cache)
        {
            _httpClient = httpClient;
            _cache = cache;
        }

        public async Task<GeocodedPoint?> ResolveAsync(AddressToLocate address, CancellationToken cancellation = default)
        {
            var query = BuildQuery(address);
            // A município is the least that places anybody. Below that there is nothing to ask.
            if (query == null)
                return null;

            var cacheKey = CacheKeyPrefix + query;

            var cached = await TryReadCacheAsync(cacheKey, cancellation);
            if (cached.HasValue)
                return cached.Value.Point;

            GeocodedPoint? point;
            try
            {
                await WaitForTurnAsync(cancellation);

                // limit=5, not 1: a named street is usually several OSM ways, all with the same
                // importance, so "the first" is whichever the tie broke towards. See ParseBestPoint.
                using var response = await _httpClient.GetAsync($"search?{query}&format=jsonv2&limit=5", cancellation);

                point = response.IsSuccessStatusCode
                    ? ParseBestPoint(await response.Content.ReadAsStringAsync(cancellation))
                    : null;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Absorbed on purpose: this decorates a map. Nothing upstream should fail because a
                // public API was slow, and the next backfill will try again.
                Log.Logger.Warning(ex, "Address geocoding failed. ErrorType={ErrorType}", ex.GetType().Name);
                return null;
            }

            await WriteCacheAsync(cacheKey, point, cancellation);
            return point;
        }

        /// <summary>
        /// The structured form of Nominatim's search, which is the accurate one: given the parts
        /// separately it does not have to guess where a free-text line's street ends.
        /// </summary>
        /// <remarks>
        /// The CEP is deliberately not sent. Nominatim treats postalcode as a filter, not a hint,
        /// and OSM's Brazilian postcode coverage is patchy enough that a correct street plus a
        /// postcode nobody mapped returns nothing at all — the field that is most often right would
        /// be the one throwing the answer away.
        /// </remarks>
        private static string? BuildQuery(AddressToLocate address)
        {
            var municipio = Clean(address.Municipio);
            if (municipio == null)
                return null;

            var parameters = new List<string>();

            // "273 Rua Barão de Teffe" — house number first is the form Nominatim documents.
            var street = string.Join(' ', new[] { Clean(address.Numero), Clean(address.Logradouro) }.Where(part => part != null));
            if (street.Length > 0)
                parameters.Add("street=" + Uri.EscapeDataString(street));

            parameters.Add("city=" + Uri.EscapeDataString(municipio));

            var uf = Clean(address.Uf);
            if (uf != null)
                parameters.Add("state=" + Uri.EscapeDataString(uf));

            parameters.Add("country=Brazil");

            return string.Join('&', parameters);
        }

        private static string? Clean(string? value) =>
            string.IsNullOrWhiteSpace(value) ? null : value.Trim();

        /// <summary>
        /// The best point the results support: the building if one was matched, otherwise the middle
        /// of the street.
        /// </summary>
        /// <remarks>
        /// <para>
        /// A house number only resolves where somebody has surveyed it, and in Brazil that is rare —
        /// Rua Barão de Teffé in Porto Alegre has no numbered address in OSM at all. When the number
        /// misses, Nominatim answers with the road, and a named road is typically several ways: that
        /// one is four, spread over 350 m, every one of them with the same importance. Taking the
        /// first is then taking whichever way the tie broke towards, which is how number 273 landed
        /// at the north end of the street.
        /// </para>
        /// <para>
        /// Averaging the segments puts the pin in the middle of the street instead, which is the
        /// best an unnumbered street supports and is never worse than a random end of it.
        /// </para>
        /// <para>
        /// ponytail: an unweighted mean of segment midpoints, so a street split into uneven ways
        /// leans towards the finely-split end, and a long curved one can land slightly off its own
        /// kerb. Both are inside the couple of hundred metres this is accurate to anyway. Door-level
        /// accuracy needs a geocoder with Brazilian house numbers, which means a key and an account.
        /// </para>
        /// </remarks>
        private static GeocodedPoint? ParseBestPoint(string payload)
        {
            using var document = JsonDocument.Parse(payload);

            if (document.RootElement.ValueKind != JsonValueKind.Array || document.RootElement.GetArrayLength() == 0)
                return null;

            var roads = new List<GeocodedPoint>();
            string? roadName = null;

            foreach (var result in document.RootElement.EnumerateArray())
            {
                var point = ReadPoint(result);
                if (point == null)
                    continue;

                var addressType = result.TryGetProperty("addresstype", out var typeElement)
                    ? typeElement.GetString()
                    : null;

                // An actual address beats any amount of averaging, and Nominatim ranks it first.
                if (addressType is "building" or "house" or "amenity" or "shop")
                    return point;

                if (addressType != "road")
                    continue;

                var name = result.TryGetProperty("name", out var nameElement) ? nameElement.GetString() : null;

                // Only segments of one street. A query that matched two different roads is a query
                // whose answer is a guess, and averaging across them would invent a point on neither.
                roadName ??= name;
                if (name == roadName)
                    roads.Add(point.Value);
            }

            if (roads.Count > 0)
            {
                return new GeocodedPoint(
                    roads.Average(road => road.Latitude),
                    roads.Average(road => road.Longitude));
            }

            // Neither a building nor a road: a bairro or a city, which is still where the customer is.
            return ReadPoint(document.RootElement[0]);
        }

        private static GeocodedPoint? ReadPoint(JsonElement result)
        {
            if (!result.TryGetProperty("lat", out var latitudeElement) || !result.TryGetProperty("lon", out var longitudeElement))
                return null;

            var latitude = ReadDecimal(latitudeElement);
            var longitude = ReadDecimal(longitudeElement);

            return latitude.HasValue && longitude.HasValue
                ? new GeocodedPoint(latitude.Value, longitude.Value)
                : null;
        }

        // Nominatim returns them as strings ("-30.0577754"), always with a dot regardless of locale.
        private static decimal? ReadDecimal(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Number)
                return element.GetDecimal();

            var raw = element.GetString();
            return decimal.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                ? value
                : null;
        }

        private static async Task WaitForTurnAsync(CancellationToken cancellation)
        {
            await Throttle.WaitAsync(cancellation);
            try
            {
                var since = DateTime.UtcNow - _lastRequestUtc;
                if (since < MinimumInterval)
                    await Task.Delay(MinimumInterval - since, cancellation);

                _lastRequestUtc = DateTime.UtcNow;
            }
            finally
            {
                Throttle.Release();
            }
        }

        private async Task<(GeocodedPoint? Point, bool Found)?> TryReadCacheAsync(string cacheKey, CancellationToken cancellation)
        {
            try
            {
                var cached = await _cache.GetStringAsync(cacheKey, cancellation);
                if (cached == null)
                    return null;

                // An address with no coordinates is cached too: re-asking every backfill for an
                // answer that will not change is the slow path this marker exists to skip.
                if (cached == NoCoordinatesMarker)
                    return (null, true);

                var parts = cached.Split(';');
                if (parts.Length == 2
                    && decimal.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var latitude)
                    && decimal.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var longitude))
                {
                    return (new GeocodedPoint(latitude, longitude), true);
                }
            }
            catch (Exception ex)
            {
                Log.Logger.Warning(ex, "Address geocode cache read failed. ErrorType={ErrorType}", ex.GetType().Name);
            }

            return null;
        }

        private async Task WriteCacheAsync(string cacheKey, GeocodedPoint? point, CancellationToken cancellation)
        {
            try
            {
                var payload = point.HasValue
                    ? string.Create(CultureInfo.InvariantCulture, $"{point.Value.Latitude};{point.Value.Longitude}")
                    : NoCoordinatesMarker;

                await _cache.SetStringAsync(
                    cacheKey,
                    payload,
                    new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = CacheTtl },
                    cancellation);
            }
            catch (Exception ex)
            {
                Log.Logger.Warning(ex, "Address geocode cache write failed. ErrorType={ErrorType}", ex.GetType().Name);
            }
        }
    }
}
