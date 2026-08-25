namespace EvangelionERPV2.Shared.Utils
{
    /// <summary>
    /// Distance between two points on the globe, and the box that bounds a radius around one.
    /// </summary>
    /// <remarks>
    /// Haversine on a spherical earth. Good to a few tenths of a percent, which is far inside the
    /// error already carried by CEP-level coordinates — the customer map's radius is a sales
    /// question ("who is near this route?"), not a survey.
    /// </remarks>
    public static class GeoDistance
    {
        private const double EarthRadiusKm = 6371.0088;

        public static double KilometresBetween(double fromLatitude, double fromLongitude, double toLatitude, double toLongitude)
        {
            var deltaLatitude = ToRadians(toLatitude - fromLatitude);
            var deltaLongitude = ToRadians(toLongitude - fromLongitude);

            var a = Math.Sin(deltaLatitude / 2) * Math.Sin(deltaLatitude / 2)
                    + Math.Cos(ToRadians(fromLatitude)) * Math.Cos(ToRadians(toLatitude))
                    * Math.Sin(deltaLongitude / 2) * Math.Sin(deltaLongitude / 2);

            return EarthRadiusKm * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        }

        /// <summary>
        /// The latitude/longitude box that contains every point within <paramref name="radiusKm"/>.
        /// </summary>
        /// <remarks>
        /// The box is what the database filters on — a Haversine in SQL cannot use an index, and a
        /// box comparison can. It over-selects at the corners by design; the caller then measures
        /// the real distance in memory and drops what falls outside the circle.
        ///
        /// Longitude degrees narrow towards the poles, hence the cos(latitude) term. Near either
        /// pole that term collapses, so the longitude span is clamped to the whole globe rather
        /// than dividing by something approaching zero.
        /// </remarks>
        public static (double MinLatitude, double MaxLatitude, double MinLongitude, double MaxLongitude) BoundingBox(
            double latitude,
            double longitude,
            double radiusKm)
        {
            var latitudeSpan = radiusKm / 111.32;

            var cosLatitude = Math.Cos(ToRadians(latitude));
            var longitudeSpan = Math.Abs(cosLatitude) < 0.01
                ? 180.0
                : radiusKm / (111.32 * Math.Abs(cosLatitude));

            return (
                Math.Max(-90, latitude - latitudeSpan),
                Math.Min(90, latitude + latitudeSpan),
                Math.Max(-180, longitude - longitudeSpan),
                Math.Min(180, longitude + longitudeSpan));
        }

        private static double ToRadians(double degrees) => degrees * Math.PI / 180.0;
    }
}
