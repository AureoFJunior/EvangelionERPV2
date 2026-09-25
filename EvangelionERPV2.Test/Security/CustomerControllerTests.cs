using AutoMapper;
using EvangelionERPV2.BillsModule.Application.Interface;
using EvangelionERPV2.CustomerModule.Application.Interface;
using EvangelionERPV2.Shared.DTOs;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Exceptions;
using EvangelionERPV2.Shared.Repositories;
using EvangelionERPV2.Web.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace EvangelionERPV2.Test.Security
{
    /// <summary>
    /// The routes the map and the ficha added, plus the write half of the CRUD. What is asserted is
    /// the tenant comparison each one makes and the address normalisation the fiscal pre-flight
    /// depends on — an empty string there looks present and produces a blank xLgr on a nota.
    /// </summary>
    public class CustomerControllerTests
    {
        private readonly Guid _enterpriseId = Guid.NewGuid();
        private readonly Guid _userId = Guid.NewGuid();
        private readonly Mock<ICustomerService<Customer>> _service = new();
        private readonly Mock<IRepository<Customer>> _repository = new();
        private readonly Mock<IRepository<User>> _userRepository = new();
        private readonly Mock<ICustomerSummaryService> _summary = new();
        private readonly Mock<ICustomerLocationService> _location = new();

        private CustomerController Controller(bool canReadBills = false, bool withUser = true) =>
            Build(canReadBills).WithTenant(_enterpriseId, withUser ? _userId : null);

        private CustomerController Anonymous() => Build(false).WithoutTenant();

        private CustomerController Build(bool canReadBills)
        {
            var authorization = new Mock<IAuthorizationService>();
            authorization
                .Setup(a => a.AuthorizeAsync(
                    It.IsAny<System.Security.Claims.ClaimsPrincipal>(), It.IsAny<object?>(), It.IsAny<string>()))
                .ReturnsAsync(canReadBills ? AuthorizationResult.Success() : AuthorizationResult.Failed());

            return new CustomerController(
                _service.Object, _repository.Object, _userRepository.Object,
                _summary.Object, _location.Object, authorization.Object, Mapper());
        }

        private static IMapper Mapper()
        {
            var mapper = new Mock<IMapper>();
            mapper.Setup(m => m.Map<CustomerDTO>(It.IsAny<Customer>()))
                .Returns<Customer>(c => new CustomerDTO { Id = c.Id, Name = c.Name, Email = c.Email });
            mapper.Setup(m => m.Map<IEnumerable<CustomerDTO>>(It.IsAny<IEnumerable<Customer>>()))
                .Returns<IEnumerable<Customer>>(all => all.Select(c => new CustomerDTO { Id = c.Id }));
            return mapper.Object;
        }

        private Customer NewCustomer(Guid? enterpriseId = null, bool isActive = true) => new()
        {
            Id = Guid.NewGuid(),
            Name = "Cliente",
            Email = "cliente@example.com",
            PhoneNumber = "000",
            Adress = "Rua",
            EnterpriseId = enterpriseId ?? _enterpriseId,
            IsActive = isActive
        };

        private void MembershipIsGood() =>
            _userRepository.Setup(r => r.GetByIdAsync(_userId))
                .ReturnsAsync(new User { Id = _userId, IsActive = true, EnterpriseId = _enterpriseId });

        private static CreateCustomerRequestDTO CreateRequest() => new()
        {
            Name = "  Cliente  ",
            Email = "  cliente@example.com  ",
            PhoneNumber = "  000  ",
            Adress = "  Rua  "
        };

        [Fact]
        public async Task GetNearbyCustomers_WithoutATenantClaim_IsUnauthorized()
        {
            Assert.IsType<UnauthorizedResult>(await Anonymous().GetNearbyCustomers(-30m, -51m));
        }

        [Fact]
        public async Task GetNearbyCustomers_ScopesTheSearchToTheCallersTenant()
        {
            _location.Setup(l => l.GetNearbyAsync(
                    _enterpriseId, -30m, -51m, 50, 100, null, It.IsAny<CancellationToken>()))
                .ReturnsAsync([]);

            Assert.IsType<OkObjectResult>(await Controller().GetNearbyCustomers(-30m, -51m));
            _location.VerifyAll();
        }

        /// <summary>
        /// A place *replaces* the radius rather than narrowing it — "Campinas" inside a 50 km
        /// circle drawn over São Paulo is nothing at all — so the term has to reach the service.
        /// </summary>
        [Fact]
        public async Task GetNearbyCustomers_ForwardsThePlaceTerm()
        {
            string? forwarded = null;
            _location.Setup(l => l.GetNearbyAsync(
                    _enterpriseId, It.IsAny<decimal>(), It.IsAny<decimal>(), It.IsAny<double>(),
                    It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                .Callback((Guid _, decimal _, decimal _, double _, int _, string? place, CancellationToken _) =>
                    forwarded = place)
                .ReturnsAsync([]);

            await Controller().GetNearbyCustomers(-30m, -51m, place: "campinas");

            Assert.Equal("campinas", forwarded);
        }

        /// <summary>
        /// Only the two messages this code wrote come back; anything carrying an inner exception is
        /// answered generically, so a column or constraint name never reaches the map screen.
        /// </summary>
        [Fact]
        public async Task GetNearbyCustomers_WithCoordinatesOutOfRange_SaysSo()
        {
            _location.Setup(l => l.GetNearbyAsync(
                    _enterpriseId, It.IsAny<decimal>(), It.IsAny<decimal>(), It.IsAny<double>(),
                    It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InsertDatabaseException("Latitude or longitude is out of range."));

            var result = Assert.IsType<BadRequestObjectResult>(await Controller().GetNearbyCustomers(-999m, -51m));

            Assert.Equal("Latitude or longitude is out of range.", result.Value);
        }

        [Fact]
        public async Task GetNearbyCustomers_WhenTheFailureCarriesAnInnerException_AnswersGenerically()
        {
            _location.Setup(l => l.GetNearbyAsync(
                    _enterpriseId, It.IsAny<decimal>(), It.IsAny<decimal>(), It.IsAny<double>(),
                    It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InsertDatabaseException(
                    "Latitude or longitude is out of range.", new Exception("column Customer.Latitude")));

            var result = Assert.IsType<BadRequestObjectResult>(await Controller().GetNearbyCustomers(-30m, -51m));

            Assert.Equal("An internal error occurred. Please try again later.", result.Value);
        }

        [Fact]
        public async Task GetNearbyCustomers_WithAMessageThisCodeNeverWrote_AnswersGenerically()
        {
            _location.Setup(l => l.GetNearbyAsync(
                    _enterpriseId, It.IsAny<decimal>(), It.IsAny<decimal>(), It.IsAny<double>(),
                    It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InsertDatabaseException("Timeout expired at Customer_Geo_IX."));

            var result = Assert.IsType<BadRequestObjectResult>(await Controller().GetNearbyCustomers(-30m, -51m));

            Assert.Equal("An internal error occurred. Please try again later.", result.Value);
        }

        [Fact]
        public async Task GeocodePendingCustomers_ScopesTheBackfillToTheCallersTenant()
        {
            _location.Setup(l => l.GeocodePendingAsync(_enterpriseId, 25, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new GeocodeBackfillResultDTO());

            Assert.IsType<OkObjectResult>(await Controller().GeocodePendingCustomers());
            _location.VerifyAll();
        }

        [Fact]
        public async Task GeocodePendingCustomers_WithoutATenantClaim_IsUnauthorized()
        {
            Assert.IsType<UnauthorizedResult>(await Anonymous().GeocodePendingCustomers());
        }

        /// <summary>
        /// Dropping a pin writes a location, and a customer id is guessable, so the service is
        /// given the caller's tenant rather than trusting the body.
        /// </summary>
        [Fact]
        public async Task SetCustomerLocation_ScopesTheWriteToTheCallersTenant()
        {
            var customerId = Guid.NewGuid();
            _location.Setup(l => l.SetLocationAsync(
                    _enterpriseId, customerId, -30.05m, -51.21m, It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);

            var result = await Controller().SetCustomerLocation(new SetCustomerLocationRequestDTO
            {
                CustomerId = customerId,
                Latitude = -30.05m,
                Longitude = -51.21m
            });

            Assert.IsType<NoContentResult>(result);
            _location.VerifyAll();
        }

        /// <summary>
        /// It answers 204 whether it moved somebody or found nobody — anything else would make the
        /// endpoint an oracle for which customer ids exist in another tenant.
        /// </summary>
        [Fact]
        public async Task SetCustomerLocation_WhenItMovedNobody_StillAnswersNoContent()
        {
            _location.Setup(l => l.SetLocationAsync(
                    _enterpriseId, It.IsAny<Guid>(), It.IsAny<decimal>(), It.IsAny<decimal>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(false);

            Assert.IsType<NoContentResult>(await Controller().SetCustomerLocation(
                new SetCustomerLocationRequestDTO { CustomerId = Guid.NewGuid() }));
        }

        [Fact]
        public async Task SetCustomerLocation_WithoutATenantClaim_IsUnauthorized()
        {
            Assert.IsType<UnauthorizedResult>(
                await Anonymous().SetCustomerLocation(new SetCustomerLocationRequestDTO()));
        }

        /// <summary>
        /// Reading a customer is not the same permission as reading what they owe, so the caller's
        /// `bills.read` is resolved here and passed down rather than assumed.
        /// </summary>
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task GetCustomerSummary_PassesDownWhetherTheCallerMaySeeTheFinancialBlock(bool canReadBills)
        {
            var id = Guid.NewGuid();
            bool? forwarded = null;

            _summary.Setup(s => s.GetSummaryAsync(id, _enterpriseId, It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .Callback((Guid _, Guid _, bool includeFinancial, CancellationToken _) => forwarded = includeFinancial)
                .ReturnsAsync(new CustomerSummaryDTO());

            Assert.IsType<OkObjectResult>(await Controller(canReadBills).GetCustomerSummary(id));
            Assert.Equal(canReadBills, forwarded);
        }

        [Fact]
        public async Task GetCustomerSummary_ForACustomerThatIsNotThisTenants_AnswersNoContent()
        {
            var id = Guid.NewGuid();
            _summary.Setup(s => s.GetSummaryAsync(id, _enterpriseId, It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((CustomerSummaryDTO?)null);

            Assert.IsType<NoContentResult>(await Controller().GetCustomerSummary(id));
        }

        [Fact]
        public async Task GetCustomerSummary_WithoutATenantClaim_IsUnauthorized()
        {
            Assert.IsType<UnauthorizedResult>(await Anonymous().GetCustomerSummary(Guid.NewGuid()));
        }

        [Fact]
        public async Task GetCustomer_FromAnotherTenant_AnswersNoContentRatherThanTheRow()
        {
            var foreign = NewCustomer(Guid.NewGuid());
            _repository.Setup(r => r.GetByIdAsync(foreign.Id)).ReturnsAsync(foreign);

            Assert.IsType<NoContentResult>(await Controller().GetCustomer(foreign.Id));
        }

        [Fact]
        public async Task GetCustomer_ThatWasSoftDeleted_AnswersNoContent()
        {
            var inactive = NewCustomer(isActive: false);
            _repository.Setup(r => r.GetByIdAsync(inactive.Id)).ReturnsAsync(inactive);

            Assert.IsType<NoContentResult>(await Controller().GetCustomer(inactive.Id));
        }

        [Fact]
        public async Task GetCustomer_OwnedByTheCaller_AnswersTheRow()
        {
            var customer = NewCustomer();
            _repository.Setup(r => r.GetByIdAsync(customer.Id)).ReturnsAsync(customer);

            var result = Assert.IsType<OkObjectResult>(await Controller().GetCustomer(customer.Id));
            Assert.Equal(customer.Id, Assert.IsType<CustomerDTO>(result.Value).Id);
        }

        [Fact]
        public async Task AddCustomer_WhenTheCallerNoLongerBelongsToTheTenant_IsUnauthorized()
        {
            _userRepository.Setup(r => r.GetByIdAsync(_userId))
                .ReturnsAsync(new User { Id = _userId, IsActive = true, EnterpriseId = Guid.NewGuid() });

            Assert.IsType<UnauthorizedResult>(await Controller().AddCustomer(CreateRequest()));
        }

        [Fact]
        public async Task AddCustomer_WithNoUserClaim_IsUnauthorized()
        {
            Assert.IsType<UnauthorizedResult>(await Controller(withUser: false).AddCustomer(CreateRequest()));
        }

        [Fact]
        public async Task AddCustomer_StampsTheCallersTenantAndTrimsTheFields()
        {
            MembershipIsGood();

            Customer? created = null;
            _service.Setup(s => s.CreateAsync(It.IsAny<Customer>()))
                .Callback<Customer>(c => created = c)
                .ReturnsAsync((Customer c) => c);

            Assert.IsType<OkObjectResult>(await Controller().AddCustomer(CreateRequest()));

            Assert.Equal(_enterpriseId, created!.EnterpriseId);
            Assert.Equal("Cliente", created.Name);
            Assert.Equal("cliente@example.com", created.Email);
            Assert.True(created.IsActive);
        }

        /// <summary>
        /// The fiscal address fields are "absent or complete". A whitespace-only value must become
        /// null, because an empty string looks present to the issuance pre-flight and produces an
        /// enderDest with a blank xLgr instead of a named error.
        /// </summary>
        [Fact]
        public async Task AddCustomer_CollapsesWhitespaceOnlyAddressFieldsToNull()
        {
            MembershipIsGood();

            Customer? created = null;
            _service.Setup(s => s.CreateAsync(It.IsAny<Customer>()))
                .Callback<Customer>(c => created = c)
                .ReturnsAsync((Customer c) => c);

            var request = CreateRequest();
            request.Logradouro = "   ";
            request.Numero = "";
            request.Bairro = "  Centro  ";

            await Controller().AddCustomer(request);

            Assert.Null(created!.Logradouro);
            Assert.Null(created.Numero);
            Assert.Equal("Centro", created.Bairro);
        }

        /// <summary>
        /// The CEP and IBGE columns are exactly 8 and 7 characters wide. The validator accepts a
        /// formatted "90230-000" because that is what a form sends, so the separators are stripped
        /// here — storing them would truncate on write.
        /// </summary>
        [Fact]
        public async Task AddCustomer_KeepsOnlyTheDigitsOfTheCepAndIbgeCode()
        {
            MembershipIsGood();

            Customer? created = null;
            _service.Setup(s => s.CreateAsync(It.IsAny<Customer>()))
                .Callback<Customer>(c => created = c)
                .ReturnsAsync((Customer c) => c);

            var request = CreateRequest();
            request.Cep = "90230-000";
            request.CodigoMunicipioIbge = "4314902";
            request.Uf = "rs";

            await Controller().AddCustomer(request);

            Assert.Equal("90230000", created!.Cep);
            Assert.Equal("4314902", created.CodigoMunicipioIbge);
            Assert.Equal("RS", created.Uf);
        }

        [Fact]
        public async Task UpdateCustomer_OnAnotherTenantsRow_AnswersNoContentAndWritesNothing()
        {
            var foreign = NewCustomer(Guid.NewGuid());
            _repository.Setup(r => r.GetByIdAsync(foreign.Id)).ReturnsAsync(foreign);

            var result = await Controller().UpdateCustomer(new UpdateCustomerRequestDTO
            {
                Id = foreign.Id,
                Name = "Novo", Email = "novo@example.com", PhoneNumber = "111", Adress = "Outra",
                IsActive = true
            });

            Assert.IsType<NoContentResult>(result);
            _service.Verify(s => s.Update(It.IsAny<Customer>()), Times.Never);
        }

        [Fact]
        public async Task UpdateCustomer_AppliesTheSameAddressNormalisationAsCreate()
        {
            var customer = NewCustomer();
            _repository.Setup(r => r.GetByIdAsync(customer.Id)).ReturnsAsync(customer);
            _service.Setup(s => s.Update(It.IsAny<Customer>())).Returns<Customer>(c => c);

            Assert.IsType<OkObjectResult>(await Controller().UpdateCustomer(new UpdateCustomerRequestDTO
            {
                Id = customer.Id,
                Name = "  Novo  ", Email = "  novo@example.com  ", PhoneNumber = "  111  ", Adress = "  Outra  ",
                Logradouro = "   ",
                Cep = "90230-000",
                Uf = "rs",
                IsActive = true
            }));

            Assert.Equal("Novo", customer.Name);
            Assert.Null(customer.Logradouro);
            Assert.Equal("90230000", customer.Cep);
            Assert.Equal("RS", customer.Uf);
        }

        [Fact]
        public async Task DeleteCustomer_OnAnotherTenantsRow_AnswersNoContentAndDeletesNothing()
        {
            var foreign = NewCustomer(Guid.NewGuid());
            _repository.Setup(r => r.GetByIdAsync(foreign.Id)).ReturnsAsync(foreign);

            Assert.IsType<NoContentResult>(await Controller().DeleteCustomer(foreign.Id));
            _service.Verify(s => s.Delete(It.IsAny<Guid>()), Times.Never);
        }

        [Fact]
        public async Task DeleteCustomer_ThatIsAlreadyInactive_AnswersNoContent()
        {
            var inactive = NewCustomer(isActive: false);
            _repository.Setup(r => r.GetByIdAsync(inactive.Id)).ReturnsAsync(inactive);

            Assert.IsType<NoContentResult>(await Controller().DeleteCustomer(inactive.Id));
            _service.Verify(s => s.Delete(It.IsAny<Guid>()), Times.Never);
        }

        [Fact]
        public async Task DeleteCustomer_OwnedByTheCaller_DeactivatesIt()
        {
            var customer = NewCustomer();
            _repository.Setup(r => r.GetByIdAsync(customer.Id)).ReturnsAsync(customer);
            _service.Setup(s => s.Delete(customer.Id)).Returns(customer);

            Assert.IsType<OkObjectResult>(await Controller().DeleteCustomer(customer.Id));
        }

        [Fact]
        public async Task DeleteCustomer_WithoutATenantClaim_IsUnauthorized()
        {
            Assert.IsType<UnauthorizedResult>(await Anonymous().DeleteCustomer(Guid.NewGuid()));
        }
    }
}
