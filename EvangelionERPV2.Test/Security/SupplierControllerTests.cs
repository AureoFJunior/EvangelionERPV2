using System.Linq.Expressions;
using AutoMapper;
using EvangelionERPV2.Shared.DTOs;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Repositories;
using EvangelionERPV2.SupplierModule.Application.Interface;
using EvangelionERPV2.Web.Controllers;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace EvangelionERPV2.Test.Security
{
    /// <summary>
    /// The supplier endpoints carry no tenant filter of their own in the query — every one of them
    /// compares <c>EnterpriseId</c> after the read. These assert that comparison, because it is the
    /// only thing standing between a guessable id and another tenant's supplier list.
    /// </summary>
    public class SupplierControllerTests
    {
        private readonly Guid _enterpriseId = Guid.NewGuid();
        private readonly Guid _userId = Guid.NewGuid();
        private readonly Mock<ISupplierService<Supplier>> _service = new();
        private readonly Mock<IRepository<Supplier>> _repository = new();
        private readonly Mock<IRepository<User>> _userRepository = new();

        private SupplierController Controller(bool withUser = true) =>
            new SupplierController(_service.Object, _repository.Object, _userRepository.Object, Mapper())
                .WithTenant(_enterpriseId, withUser ? _userId : null);

        private static IMapper Mapper()
        {
            var mapper = new Mock<IMapper>();
            mapper.Setup(m => m.Map<SupplierDTO>(It.IsAny<Supplier>()))
                .Returns<Supplier>(s => new SupplierDTO { Id = s.Id, Name = s.Name, EnterpriseId = s.EnterpriseId });
            mapper.Setup(m => m.Map<IEnumerable<SupplierDTO>>(It.IsAny<IEnumerable<Supplier>>()))
                .Returns<IEnumerable<Supplier>>(all => all.Select(s => new SupplierDTO { Id = s.Id, Name = s.Name }));
            return mapper.Object;
        }

        private static Supplier NewSupplier(Guid enterpriseId, bool isActive = true) => new()
        {
            Id = Guid.NewGuid(),
            Name = "Fornecedor",
            Email = "vendas@example.com",
            PhoneNumber = "000",
            Adress = "Rua",
            EnterpriseId = enterpriseId,
            IsActive = isActive
        };

        private static CreateSupplierRequestDTO CreateRequest() => new()
        {
            Name = " Fornecedor ",
            Email = " vendas@example.com ",
            PhoneNumber = " 000 ",
            Adress = " Rua ",
            Document = " 11222333000181 "
        };

        [Fact]
        public async Task GetSuppliers_WithoutATenantClaim_IsUnauthorized()
        {
            var controller = new SupplierController(_service.Object, _repository.Object, _userRepository.Object, Mapper())
                .WithoutTenant();

            Assert.IsType<UnauthorizedResult>(await controller.GetSuppliers());
        }

        [Fact]
        public async Task GetSuppliers_ScopesTheQueryToTheCallersTenant()
        {
            var other = Guid.NewGuid();
            Expression<Func<Supplier, bool>>? captured = null;

            _repository
                .Setup(r => r.GetAllAsync(It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<Expression<Func<Supplier, bool>>>()))
                .Callback<int?, int?, Expression<Func<Supplier, bool>>?>((_, _, predicate) => captured = predicate)
                .ReturnsAsync([NewSupplier(_enterpriseId)]);

            var result = await Controller().GetSuppliers();

            Assert.IsType<OkObjectResult>(result);
            Assert.NotNull(captured);

            var matches = captured!.Compile();
            Assert.True(matches(NewSupplier(_enterpriseId)));
            Assert.False(matches(NewSupplier(other)));
        }

        /// <summary>
        /// Unbounded paging is a denial of service with a page number. The controller normalises
        /// before it reaches the repository, so a missing page is 1/50 and a huge one is clamped.
        /// </summary>
        [Theory]
        [InlineData(null, null, 1, 50)]
        [InlineData(3, 10, 3, 10)]
        [InlineData(1, 100000, 1, 200)]
        public async Task GetSuppliers_NormalisesPaginationBeforeItReachesTheRepository(
            int? pageNumber, int? pageSize, int expectedPage, int expectedSize)
        {
            int? capturedPage = null;
            int? capturedSize = null;

            _repository
                .Setup(r => r.GetAllAsync(It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<Expression<Func<Supplier, bool>>>()))
                .Callback<int?, int?, Expression<Func<Supplier, bool>>?>((p, s, _) => { capturedPage = p; capturedSize = s; })
                .ReturnsAsync([]);

            await Controller().GetSuppliers(pageNumber, pageSize);

            Assert.Equal(expectedPage, capturedPage);
            Assert.Equal(expectedSize, capturedSize);
        }

        [Fact]
        public async Task GetSupplier_WithAnotherTenantsId_AnswersNoContentRatherThanTheRow()
        {
            var foreign = NewSupplier(Guid.NewGuid());
            _repository.Setup(r => r.GetByIdAsync(foreign.Id)).ReturnsAsync(foreign);

            Assert.IsType<NoContentResult>(await Controller().GetSupplier(foreign.Id));
        }

        [Fact]
        public async Task GetSupplier_ThatWasSoftDeleted_AnswersNoContent()
        {
            var inactive = NewSupplier(_enterpriseId, isActive: false);
            _repository.Setup(r => r.GetByIdAsync(inactive.Id)).ReturnsAsync(inactive);

            Assert.IsType<NoContentResult>(await Controller().GetSupplier(inactive.Id));
        }

        [Fact]
        public async Task GetSupplier_OwnedByTheCaller_AnswersTheRow()
        {
            var supplier = NewSupplier(_enterpriseId);
            _repository.Setup(r => r.GetByIdAsync(supplier.Id)).ReturnsAsync(supplier);

            var result = Assert.IsType<OkObjectResult>(await Controller().GetSupplier(supplier.Id));
            Assert.Equal(supplier.Id, Assert.IsType<SupplierDTO>(result.Value).Id);
        }

        /// <summary>
        /// A token can outlive the membership it was minted for. The write path re-reads the user
        /// rather than trusting the tenant claim alone.
        /// </summary>
        [Fact]
        public async Task AddSupplier_WhenTheCallerNoLongerBelongsToTheTenant_IsUnauthorized()
        {
            _userRepository.Setup(r => r.GetByIdAsync(_userId))
                .ReturnsAsync(new User { Id = _userId, IsActive = true, EnterpriseId = Guid.NewGuid() });

            Assert.IsType<UnauthorizedResult>(await Controller().AddSupplier(CreateRequest()));
        }

        [Fact]
        public async Task AddSupplier_WhenTheCallerWasDeactivated_IsUnauthorized()
        {
            _userRepository.Setup(r => r.GetByIdAsync(_userId))
                .ReturnsAsync(new User { Id = _userId, IsActive = false, EnterpriseId = _enterpriseId });

            Assert.IsType<UnauthorizedResult>(await Controller().AddSupplier(CreateRequest()));
        }

        [Fact]
        public async Task AddSupplier_WithNoUserClaim_IsUnauthorized()
        {
            Assert.IsType<UnauthorizedResult>(await Controller(withUser: false).AddSupplier(CreateRequest()));
        }

        [Fact]
        public async Task AddSupplier_StampsTheCallersTenantAndTrimsTheFields()
        {
            _userRepository.Setup(r => r.GetByIdAsync(_userId))
                .ReturnsAsync(new User { Id = _userId, IsActive = true, EnterpriseId = _enterpriseId });

            Supplier? created = null;
            _service.Setup(s => s.CreateAsync(It.IsAny<Supplier>()))
                .Callback<Supplier>(s => created = s)
                .ReturnsAsync((Supplier s) => s);

            Assert.IsType<OkObjectResult>(await Controller().AddSupplier(CreateRequest()));

            Assert.NotNull(created);
            Assert.Equal(_enterpriseId, created!.EnterpriseId);
            Assert.Equal("Fornecedor", created.Name);
            Assert.Equal("vendas@example.com", created.Email);
            Assert.Equal("11222333000181", created.Document);
            Assert.True(created.IsActive);
        }

        [Fact]
        public async Task UpdateSupplier_WithAnotherTenantsId_AnswersNoContentAndWritesNothing()
        {
            var foreign = NewSupplier(Guid.NewGuid());
            _repository.Setup(r => r.GetByIdAsync(foreign.Id)).ReturnsAsync(foreign);

            var result = await Controller().UpdateSupplier(new UpdateSupplierRequestDTO
            {
                Id = foreign.Id,
                Name = "Novo",
                Email = "novo@example.com",
                PhoneNumber = "111",
                Adress = "Outra",
                IsActive = true
            });

            Assert.IsType<NoContentResult>(result);
            _service.Verify(s => s.Update(It.IsAny<Supplier>()), Times.Never);
        }

        [Fact]
        public async Task UpdateSupplier_OwnedByTheCaller_WritesTheTrimmedFields()
        {
            var supplier = NewSupplier(_enterpriseId);
            _repository.Setup(r => r.GetByIdAsync(supplier.Id)).ReturnsAsync(supplier);
            _service.Setup(s => s.Update(It.IsAny<Supplier>())).Returns<Supplier>(s => s);

            var result = await Controller().UpdateSupplier(new UpdateSupplierRequestDTO
            {
                Id = supplier.Id,
                Name = "  Novo  ",
                Email = "  novo@example.com  ",
                PhoneNumber = "  111  ",
                Adress = "  Outra  ",
                IsActive = true
            });

            Assert.IsType<OkObjectResult>(result);
            Assert.Equal("Novo", supplier.Name);
            Assert.Equal("novo@example.com", supplier.Email);
            Assert.Equal("111", supplier.PhoneNumber);
            Assert.Equal("Outra", supplier.Adress);
        }

        [Fact]
        public async Task DeleteSupplier_WithAnotherTenantsId_AnswersNoContentAndDeletesNothing()
        {
            var foreign = NewSupplier(Guid.NewGuid());
            _repository.Setup(r => r.GetByIdAsync(foreign.Id)).ReturnsAsync(foreign);

            Assert.IsType<NoContentResult>(await Controller().DeleteSupplier(foreign.Id));
            _service.Verify(s => s.Delete(It.IsAny<Guid>()), Times.Never);
        }

        [Fact]
        public async Task DeleteSupplier_OwnedByTheCaller_AnswersTheDeletedRow()
        {
            var supplier = NewSupplier(_enterpriseId);
            _repository.Setup(r => r.GetByIdAsync(supplier.Id)).ReturnsAsync(supplier);
            _service.Setup(s => s.Delete(supplier.Id)).Returns(supplier);

            Assert.IsType<OkObjectResult>(await Controller().DeleteSupplier(supplier.Id));
        }

        [Fact]
        public async Task DeleteSupplier_ThatIsAlreadyInactive_AnswersNoContent()
        {
            var inactive = NewSupplier(_enterpriseId, isActive: false);
            _repository.Setup(r => r.GetByIdAsync(inactive.Id)).ReturnsAsync(inactive);

            Assert.IsType<NoContentResult>(await Controller().DeleteSupplier(inactive.Id));
            _service.Verify(s => s.Delete(It.IsAny<Guid>()), Times.Never);
        }
    }
}
