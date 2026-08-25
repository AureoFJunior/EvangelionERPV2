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
        private readonly ICepGeocoder _geocoder;

        public CustomerLocationService(IRepository<Customer> customerRepository, ICepGeocoder geocoder)
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
            CancellationToken cancellation = default)
        {
            if (latitude < -90 || latitude > 90 || longitude < -180 || longitude > 180)
                throw new InsertDatabaseException("Latitude or longitude is out of range.");

            if (radiusKm <= 0)
                throw new InsertDatabaseException("Radius must be greater than zero.");

            var boundedRadius = Math.Min(radiusKm, MaxRadiusKm);
            var boundedTake = Math.Clamp(take, 1, MaxTake);

            var box = GeoDistance.BoundingBox((double)latitude, (double)longitude, boundedRadius);

            var minLatitude = (decimal)box.MinLatitude;
            var maxLatitude = (decimal)box.MaxLatitude;
            var minLongitude = (decimal)box.MinLongitude;
            var maxLongitude = (decimal)box.MaxLongitude;

            // The box is filtered in SQL because a comparison can use an index and a Haversine
            // cannot; the circle is then cut in memory below. GetPagedAsync, not GetAllAsync:
            // CustomerRepository throws NotFoundDatabaseException on an empty result, and an empty
            // map is a normal answer.
            var (candidates, _) = await _customerRepository.GetPagedAsync(
                customer => customer.IsActive == true
                            && customer.EnterpriseId == enterpriseId
                            && customer.Latitude != null
                            && customer.Longitude != null
                            && customer.Latitude >= minLatitude
                            && customer.Latitude <= maxLatitude
                            && customer.Longitude >= minLongitude
                            && customer.Longitude <= maxLongitude,
                pageNumber: 1,
                pageSize: MaxTake);

            return candidates
                .Select(customer => new NearbyCustomerDTO
                {
                    CustomerId = customer.Id,
                    CustomerName = customer.Name,
                    PhoneNumber = customer.PhoneNumber,
                    Municipio = customer.Municipio,
                    Uf = customer.Uf,
                    Latitude = customer.Latitude!.Value,
                    Longitude = customer.Longitude!.Value,
                    DistanceKm = Math.Round(
                        GeoDistance.KilometresBetween(
                            (double)latitude,
                            (double)longitude,
                            (double)customer.Latitude!.Value,
                            (double)customer.Longitude!.Value),
                        2)
                })
                // The box over-selects at the corners; this is where the answer becomes a circle.
                .Where(customer => customer.DistanceKm <= boundedRadius)
                .OrderBy(customer => customer.DistanceKm)
                .Take(boundedTake)
                .ToList();
        }

        public async Task<GeocodeBackfillResultDTO> GeocodePendingAsync(
            Guid enterpriseId,
            int take,
            CancellationToken cancellation = default)
        {
            var boundedTake = Math.Clamp(take, 1, MaxBackfillBatch);

            var (pending, remaining) = await _customerRepository.GetPagedAsync(
                customer => customer.IsActive == true
                            && customer.EnterpriseId == enterpriseId
                            && customer.Latitude == null
                            && customer.Cep != null
                            && customer.Cep != "",
                pageNumber: 1,
                pageSize: boundedTake);

            var located = 0;

            foreach (var customer in pending)
            {
                var point = await _geocoder.ResolveAsync(customer.Cep, cancellation);
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
    }
}
