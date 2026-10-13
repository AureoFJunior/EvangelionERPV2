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
                .Where(customer => searchingByPlace || customer.DistanceKm <= boundedRadius)
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
