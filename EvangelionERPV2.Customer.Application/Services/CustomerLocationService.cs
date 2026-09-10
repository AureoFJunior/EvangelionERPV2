using EvangelionERPV2.CustomerModule.Application.Interface;
using EvangelionERPV2.Shared.DTOs;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Exceptions;
using EvangelionERPV2.Shared.Repositories;
using EvangelionERPV2.Shared.Utils;

namespace EvangelionERPV2.CustomerModule.Application.Services
{
    /// <summary>
    /// The customer map: who is near a point, and filling in where everybody is.
    /// </summary>
    public class CustomerLocationService : ICustomerLocationService
    {
        private const double MaxRadiusKm = 500;
        private const int MaxTake = 500;
        private const int MaxBackfillBatch = 100;

        private readonly IRepository<Customer> _customerRepository;
        private readonly IAddressGeocoder _geocoder;

        public CustomerLocationService(IRepository<Customer> customerRepository, IAddressGeocoder geocoder)
        {
            _customerRepository = customerRepository;
            _geocoder = geocoder;
        }

        public async Task<IReadOnlyList<NearbyCustomerDTO>> GetNearbyAsync(
            Guid enterpriseId,
            decimal latitude,
            decimal longitude,
            double radiusKm,
            int take,
            string? place = null,
            CancellationToken cancellation = default)
        {
            if (latitude < -90 || latitude > 90 || longitude < -180 || longitude > 180)
                throw new InsertDatabaseException("Latitude or longitude is out of range.");

            if (radiusKm <= 0)
                throw new InsertDatabaseException("Radius must be greater than zero.");

            var boundedRadius = Math.Min(radiusKm, MaxRadiusKm);
            var boundedTake = Math.Clamp(take, 1, MaxTake);

            var searchedPlace = (place ?? string.Empty).Trim().ToLower();
            var searchingByPlace = searchedPlace.Length > 0;

            var box = GeoDistance.BoundingBox((double)latitude, (double)longitude, boundedRadius);

            var minLatitude = (decimal)box.MinLatitude;
            var maxLatitude = (decimal)box.MaxLatitude;
            var minLongitude = (decimal)box.MinLongitude;
            var maxLongitude = (decimal)box.MaxLongitude;

            // The box is filtered in SQL because a comparison can use an index and a Haversine
            // cannot; the circle is then cut in memory below. GetPagedAsync, not GetAllAsync:
            // CustomerRepository throws NotFoundDatabaseException on an empty result, and an empty
            // map is a normal answer.
            //
            // A place search drops the box rather than adding to it. The two would fight: the map
            // opens on São Paulo, and "Campinas" inside a 50 km circle drawn there is nothing at
            // all. Coordinates are still required — this feeds a map — but see the note on
            // GeocodePendingAsync about what that costs a customer who has none.
            //
            // COALESCE, not a null check per field: a customer with no bairro must fail the match,
            // not throw, and `(x ?? "")` is the form that means the same thing in SQL and in memory.
            var (candidates, _) = await _customerRepository.GetPagedAsync(
                customer => customer.IsActive == true
                            && customer.EnterpriseId == enterpriseId
                            && (searchingByPlace
                                ? (customer.Municipio ?? string.Empty).ToLower().Contains(searchedPlace)
                                    || (customer.Uf ?? string.Empty).ToLower().Contains(searchedPlace)
                                    || (customer.Bairro ?? string.Empty).ToLower().Contains(searchedPlace)
                                : customer.Latitude != null
                                    && customer.Longitude != null
                                    && customer.Latitude >= minLatitude
                                    && customer.Latitude <= maxLatitude
                                    && customer.Longitude >= minLongitude
                                    && customer.Longitude <= maxLongitude),
                pageNumber: 1,
                pageSize: MaxTake);

            return candidates
                .Select(customer => new NearbyCustomerDTO
                {
                    CustomerId = customer.Id,
                    CustomerName = customer.Name,
                    PhoneNumber = customer.PhoneNumber,
                    Bairro = customer.Bairro,
                    Municipio = customer.Municipio,
                    Uf = customer.Uf,
                    Latitude = customer.Latitude,
                    Longitude = customer.Longitude,
                    DistanceKm = customer.Latitude == null || customer.Longitude == null
                        ? null
                        : Math.Round(
                            GeoDistance.KilometresBetween(
                                (double)latitude,
                                (double)longitude,
                                (double)customer.Latitude.Value,
                                (double)customer.Longitude.Value),
                            2)
                })
                // The box over-selects at the corners; this is where the answer becomes a circle.
                // Skipped for a place search, which was never bounded by one.
                .Where(customer => searchingByPlace || customer.DistanceKm <= boundedRadius)
                // Nearest first, and the customers nobody could pin last — they are the answer to
                // "why is this one not on the map", so they belong in the list, not at the top of it.
                .OrderBy(customer => customer.DistanceKm ?? double.MaxValue)
                .ThenBy(customer => customer.CustomerName)
                .Take(boundedTake)
                .ToList();
        }

        public async Task<GeocodeBackfillResultDTO> GeocodePendingAsync(
            Guid enterpriseId,
            int take,
            CancellationToken cancellation = default)
        {
            var boundedTake = Math.Clamp(take, 1, MaxBackfillBatch);

            // Município, not CEP: the geocoder is asked for an address now, and a município is the
            // least of one that places anybody. A CEP with no city attached would only ever have
            // resolved to a city centroid anyway, which is the pin this stopped drawing.
            var (pending, remaining) = await _customerRepository.GetPagedAsync(
                customer => customer.IsActive == true
                            && customer.EnterpriseId == enterpriseId
                            && customer.Latitude == null
                            && customer.Municipio != null
                            && customer.Municipio != "",
                pageNumber: 1,
                pageSize: boundedTake);

            var located = 0;

            foreach (var customer in pending)
            {
                var point = await _geocoder.ResolveAsync(
                    new AddressToLocate(
                        customer.Logradouro,
                        customer.Numero,
                        customer.Bairro,
                        customer.Municipio,
                        customer.Uf,
                        customer.Cep),
                    cancellation);
                if (point == null)
                    continue;

                customer.Latitude = point.Value.Latitude;
                customer.Longitude = point.Value.Longitude;
                customer.UpdatedAt = DateTime.UtcNow;
                _customerRepository.Update(customer);
                located++;
            }

            if (located > 0)
                await _customerRepository.CommitAsync(cancellation);

            return new GeocodeBackfillResultDTO
            {
                Examined = pending.Count,
                Located = located,
                // What is left over after this pass. A CEP the service has no coordinates for stays
                // in this count forever, which is why the caller stops when it stops falling.
                Remaining = Math.Max(0, remaining - located)
            };
        }

        public async Task<bool> SetLocationAsync(
            Guid enterpriseId,
            Guid customerId,
            decimal latitude,
            decimal longitude,
            CancellationToken cancellation = default)
        {
            if (latitude < -90 || latitude > 90 || longitude < -180 || longitude > 180)
                throw new InsertDatabaseException("Latitude or longitude is out of range.");

            // Through the tenant filter, not GetByIdAsync: a customer id is guessable and this
            // writes. Answering false for another tenant's customer says only "not yours to move".
            var (found, _) = await _customerRepository.GetPagedAsync(
                customer => customer.IsActive == true
                            && customer.EnterpriseId == enterpriseId
                            && customer.Id == customerId,
                pageNumber: 1,
                pageSize: 1);

            var target = found.FirstOrDefault();
            if (target == null)
                return false;

            target.Latitude = latitude;
            target.Longitude = longitude;
            target.UpdatedAt = DateTime.UtcNow;

            _customerRepository.Update(target);
            await _customerRepository.CommitAsync(cancellation);

            return true;
        }
    }
}
