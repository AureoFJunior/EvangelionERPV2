using System.Globalization;
using System.Text.Json;
using EvangelionERPV2.CustomerModule.Application.Interface;
using Microsoft.Extensions.Caching.Distributed;
using Serilog;

namespace EvangelionERPV2.CustomerModule.Application.Services
{
    /// <summary>
    /// Turns a CEP into coordinates using BrasilAPI's public CEP v2 endpoint.
    /// </summary>
    /// <remarks>
    /// <para>
    /// BrasilAPI rather than a geocoder because it needs no key, no account and no new package, and
    /// because a Brazilian address in this database always has a CEP but rarely a clean street line.
    /// </para>
    /// <para>
    /// ponytail: precision is CEP-level, not street-level — in a dense city that is a block or two,
    /// and in the interior a whole district shares one CEP. Good enough for "who is near this
    /// route?"; swap in a real geocoder if somebody needs to navigate to the door.
    /// </para>
    /// <para>
    /// Cached because a CEP's coordinates do not move and a backfill re-reads the same ones across
    /// customers in the same street.
    /// </para>
    /// </remarks>
    public sealed class BrasilApiCepGeocoder : ICepGeocoder
    {
        private const string CacheKeyPrefix = "Geocode:Cep:";
        private static readonly TimeSpan CacheTtl = TimeSpan.FromDays(30);
        private const string NoCoordinatesMarker = "-";

        private readonly HttpClient _httpClient;
        private readonly IDistributedCache _cache;

        public BrasilApiCepGeocoder(HttpClient httpClient, IDistributedCache cache)
        {
            _httpClient = httpClient;
            _cache = cache;
        }

        public async Task<GeocodedPoint?> ResolveAsync(string? cep, CancellationToken cancellation = default)
        {
            var digits = new string((cep ?? string.Empty).Where(char.IsDigit).ToArray());
            if (digits.Length != 8)
                return null;

            var cacheKey = CacheKeyPrefix + digits;

            var cached = await TryReadCacheAsync(cacheKey, cancellation);
            if (cached.HasValue)
                return cached.Value.Point;

            GeocodedPoint? point;
            try
            {
                using var response = await _httpClient.GetAsync($"api/cep/v2/{digits}", cancellation);

                // 404 is the ordinary answer for a CEP nobody has mapped — not an error worth logging.
                point = response.IsSuccessStatusCode
                    ? ParseCoordinates(await response.Content.ReadAsStringAsync(cancellation))
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
                Log.Logger.Warning(ex, "CEP geocoding failed. ErrorType={ErrorType}", ex.GetType().Name);
                return null;
            }

            await WriteCacheAsync(cacheKey, point, cancellation);
            return point;
        }

        private static GeocodedPoint? ParseCoordinates(string payload)
        {
            using var document = JsonDocument.Parse(payload);

            if (!document.RootElement.TryGetProperty("location", out var location)
                || !location.TryGetProperty("coordinates", out var coordinates))
            {
                return null;
            }

            // The pair is present but empty for a CEP BrasilAPI knows without having located it.
            if (!coordinates.TryGetProperty("latitude", out var latitudeElement)
                || !coordinates.TryGetProperty("longitude", out var longitudeElement))
            {
                return null;
            }

            var latitude = ReadDecimal(latitudeElement);
            var longitude = ReadDecimal(longitudeElement);

            return latitude.HasValue && longitude.HasValue
                ? new GeocodedPoint(latitude.Value, longitude.Value)
                : null;
        }

        // The API returns them as strings ("-23.5505"), and always with a dot regardless of locale.
        private static decimal? ReadDecimal(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Number)
                return element.GetDecimal();

            var raw = element.GetString();
            return decimal.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                ? value
                : null;
        }

        private async Task<(GeocodedPoint? Point, bool Found)?> TryReadCacheAsync(string cacheKey, CancellationToken cancellation)
        {
            try
            {
                var cached = await _cache.GetStringAsync(cacheKey, cancellation);
                if (cached == null)
                    return null;

                // A CEP with no coordinates is cached too: re-asking every backfill for an answer
                // that will not change is the slow path this marker exists to skip.
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
                Log.Logger.Warning(ex, "CEP geocode cache read failed. ErrorType={ErrorType}", ex.GetType().Name);
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
                Log.Logger.Warning(ex, "CEP geocode cache write failed. ErrorType={ErrorType}", ex.GetType().Name);
            }
        }
    }
}
