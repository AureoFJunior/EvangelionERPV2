using System.Linq.Expressions;
using AutoMapper;
using EvangelionERPV2.EmployeeModule.Application.Interface;
using EvangelionERPV2.EmployeeModule.Domain.Interface;
using EvangelionERPV2.Shared.DTOs;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Repositories;
using EvangelionERPV2.Web.Controllers;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace EvangelionERPV2.Test.Security
{
    /// <summary>
    /// An employee row carries a salary, a CPF and a photograph, so every read compares the tenant
    /// after the fetch and every picture is resolved separately — a list is never allowed to carry
    /// a hundred base64 images by accident.
    /// </summary>
    public class EmployeeControllerTests
    {
        private readonly Guid _enterpriseId = Guid.NewGuid();
        private readonly Guid _userId = Guid.NewGuid();
        private readonly Mock<IEmployeeService<Employee>> _service = new();
        private readonly Mock<IRepository<Employee>> _repository = new();
        private readonly Mock<IEmployeeRepository<Employee>> _employeeRepository = new();
        private readonly Mock<IRepository<User>> _userRepository = new();

        private EmployeeController Controller(bool withUser = true) =>
            new EmployeeController(
                    _service.Object, _repository.Object, _employeeRepository.Object, _userRepository.Object, Mapper())
                .WithTenant(_enterpriseId, withUser ? _userId : null);

        private EmployeeController Anonymous() =>
            new EmployeeController(
                    _service.Object, _repository.Object, _employeeRepository.Object, _userRepository.Object, Mapper())
                .WithoutTenant();

        private static IMapper Mapper()
        {
            var mapper = new Mock<IMapper>();
            mapper.Setup(m => m.Map<EmployeeDTO>(It.IsAny<Employee>()))
                .Returns<Employee>(e => new EmployeeDTO
                {
                    Id = e.Id,
                    Name = e.Name,
                    Position = e.Position,
                    Picture = e.Picture ?? string.Empty
                });
            return mapper.Object;
        }

        private Employee NewEmployee(Guid? enterpriseId = null, bool isActive = true, string? picture = null) => new()
        {
            Id = Guid.NewGuid(),
            Name = "Maria",
            Email = "maria@example.com",
            PhoneNumber = "000",
            Position = "Vendedora",
            HireDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            EnterpriseId = enterpriseId ?? _enterpriseId,
            IsActive = isActive,
            Picture = picture
        };

        private void MembershipIsGood() =>
            _userRepository.Setup(r => r.GetByIdAsync(_userId))
                .ReturnsAsync(new User { Id = _userId, IsActive = true, EnterpriseId = _enterpriseId });

        [Fact]
        public async Task GetEmployees_WithoutATenantClaim_IsUnauthorized()
        {
            Assert.IsType<UnauthorizedResult>(await Anonymous().GetEmployees());
        }

        [Fact]
        public async Task GetEmployees_ScopesTheQueryToLiveRowsOfTheCallersTenant()
        {
            Expression<Func<Employee, bool>>? captured = null;
            _repository
                .Setup(r => r.GetAllAsync(It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<Expression<Func<Employee, bool>>>()))
                .Callback<int?, int?, Expression<Func<Employee, bool>>?>((_, _, predicate) => captured = predicate)
                .ReturnsAsync([NewEmployee()]);

            Assert.IsType<OkObjectResult>(await Controller().GetEmployees());

            var matches = captured!.Compile();
            Assert.True(matches(NewEmployee()));
            Assert.False(matches(NewEmployee(Guid.NewGuid())));
            Assert.False(matches(NewEmployee(isActive: false)));
        }

        /// <summary>
        /// A page of employees with their photographs is a page of base64 blobs, so asking for
        /// pictures shrinks the page rather than being refused — 200 rows of image would be a
        /// response nobody can use.
        /// </summary>
        [Theory]
        [InlineData(false, 200, 200)]
        [InlineData(true, 200, 50)]
        public async Task GetEmployees_ClampsThePageHarderWhenPicturesAreAskedFor(
            bool includePictures, int requestedSize, int expectedSize)
        {
            int? capturedSize = null;
            _repository
                .Setup(r => r.GetAllAsync(It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<Expression<Func<Employee, bool>>>()))
                .Callback<int?, int?, Expression<Func<Employee, bool>>?>((_, size, _) => capturedSize = size)
                .ReturnsAsync([]);

            await Controller().GetEmployees(1, requestedSize, includePictures);

            Assert.Equal(expectedSize, capturedSize);
        }

        /// <summary>
        /// The stored value is a path, not an image. A list that does not ask for pictures must not
        /// leak it, and must not pay for the fetch either.
        /// </summary>
        [Fact]
        public async Task GetEmployees_WithoutPictures_LeavesThePictureFieldEmptyAndFetchesNothing()
        {
            _repository
                .Setup(r => r.GetAllAsync(It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<Expression<Func<Employee, bool>>>()))
                .ReturnsAsync([NewEmployee(picture: "employees/maria.jpg")]);

            var result = Assert.IsType<OkObjectResult>(await Controller().GetEmployees());
            var employees = Assert.IsAssignableFrom<IEnumerable<EmployeeDTO>>(result.Value);

            Assert.Equal(string.Empty, employees.Single().Picture);
            _service.Verify(s => s.GetPictureBase64Async(It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task GetEmployees_WithPictures_ResolvesTheStoredPath()
        {
            _repository
                .Setup(r => r.GetAllAsync(It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<Expression<Func<Employee, bool>>>()))
                .ReturnsAsync([NewEmployee(picture: "employees/maria.jpg")]);
            _service.Setup(s => s.GetPictureBase64Async("employees/maria.jpg")).ReturnsAsync("base64");

            var result = Assert.IsType<OkObjectResult>(await Controller().GetEmployees(includePictures: true));
            var employees = Assert.IsAssignableFrom<IEnumerable<EmployeeDTO>>(result.Value);

            Assert.Equal("base64", employees.Single().Picture);
        }

        [Fact]
        public async Task GetEmployeesByFilter_TrimsEveryTermAndStampsTheTenantOnTheProbe()
        {
            Employee? probe = null;
            _employeeRepository
                .Setup(r => r.GetAllAsyncFiltering(
                    It.IsAny<bool>(), It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<Employee>(), It.IsAny<bool?>()))
                .Callback<bool, int?, int?, Employee, bool?>((_, _, _, employee, _) => probe = employee)
                .ReturnsAsync([NewEmployee()]);

            await Controller().GetEmployeesByFilter(
                new EmployeeFilterRequestDTO { Name = "  Maria  ", Email = "  m@e.com ", Position = " Vend ", PhoneNumber = " 9 " },
                descending: true);

            Assert.Equal("Maria", probe!.Name);
            Assert.Equal("m@e.com", probe.Email);
            Assert.Equal("Vend", probe.Position);
            Assert.Equal("9", probe.PhoneNumber);
            Assert.Equal(_enterpriseId, probe.EnterpriseId);
        }

        /// <summary>A null body is a filterless search, not a bad request.</summary>
        [Fact]
        public async Task GetEmployeesByFilter_WithNoFilterAtAll_StillSearches()
        {
            _employeeRepository
                .Setup(r => r.GetAllAsyncFiltering(
                    It.IsAny<bool>(), It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<Employee>(), It.IsAny<bool?>()))
                .ReturnsAsync([NewEmployee()]);

            Assert.IsType<OkObjectResult>(await Controller().GetEmployeesByFilter(null, descending: false));
        }

        [Fact]
        public async Task GetEmployeesByFilter_WithNoMatches_AnswersNoContent()
        {
            _employeeRepository
                .Setup(r => r.GetAllAsyncFiltering(
                    It.IsAny<bool>(), It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<Employee>(), It.IsAny<bool?>()))
                .ReturnsAsync([]);

            Assert.IsType<NoContentResult>(
                await Controller().GetEmployeesByFilter(new EmployeeFilterRequestDTO(), descending: false));
        }

        [Fact]
        public async Task GetEmployeesByFilter_WithoutATenantClaim_IsUnauthorized()
        {
            Assert.IsType<UnauthorizedResult>(
                await Anonymous().GetEmployeesByFilter(new EmployeeFilterRequestDTO(), descending: false));
        }

        [Fact]
        public async Task GetEmployee_FromAnotherTenant_AnswersNoContentRatherThanTheRow()
        {
            var foreign = NewEmployee(Guid.NewGuid());
            _repository.Setup(r => r.GetByIdAsync(foreign.Id)).ReturnsAsync(foreign);

            Assert.IsType<NoContentResult>(await Controller().GetEmployee(foreign.Id));
        }

        [Fact]
        public async Task GetEmployee_ThatWasSoftDeleted_AnswersNoContent()
        {
            var inactive = NewEmployee(isActive: false);
            _repository.Setup(r => r.GetByIdAsync(inactive.Id)).ReturnsAsync(inactive);

            Assert.IsType<NoContentResult>(await Controller().GetEmployee(inactive.Id));
        }

        [Fact]
        public async Task GetEmployee_OwnedByTheCaller_AnswersTheRow()
        {
            var employee = NewEmployee();
            _repository.Setup(r => r.GetByIdAsync(employee.Id)).ReturnsAsync(employee);

            var result = Assert.IsType<OkObjectResult>(await Controller().GetEmployee(employee.Id));
            Assert.Equal(employee.Id, Assert.IsType<EmployeeDTO>(result.Value).Id);
        }

        [Fact]
        public async Task AddEmployee_WhenTheCallerNoLongerBelongsToTheTenant_IsUnauthorized()
        {
            _userRepository.Setup(r => r.GetByIdAsync(_userId))
                .ReturnsAsync(new User { Id = _userId, IsActive = true, EnterpriseId = Guid.NewGuid() });

            Assert.IsType<UnauthorizedResult>(await Controller().AddEmployee(new CreateEmployeeRequestDTO
            {
                Name = "Maria", Email = "m@e.com", PhoneNumber = "0", Position = "Vendedora"
            }));
        }

        [Fact]
        public async Task AddEmployee_WithNoUserClaim_IsUnauthorized()
        {
            Assert.IsType<UnauthorizedResult>(await Controller(withUser: false).AddEmployee(new CreateEmployeeRequestDTO
            {
                Name = "Maria", Email = "m@e.com", PhoneNumber = "0", Position = "Vendedora"
            }));
        }

        [Fact]
        public async Task AddEmployee_StampsTheTenantTrimsTheFieldsAndPassesThePictureSeparately()
        {
            MembershipIsGood();

            Employee? created = null;
            string? picture = null;
            _service.Setup(s => s.CreateAsync(It.IsAny<Employee>(), It.IsAny<string?>()))
                .Callback<Employee, string?>((employee, payload) => { created = employee; picture = payload; })
                .ReturnsAsync((Employee employee, string? _) => employee);

            Assert.IsType<OkObjectResult>(await Controller().AddEmployee(new CreateEmployeeRequestDTO
            {
                Name = "  Maria  ",
                Email = "  maria@example.com  ",
                PhoneNumber = "  000  ",
                Position = "  Vendedora  ",
                Document = "  52998224725  ",
                Picture = "base64-payload"
            }));

            Assert.Equal(_enterpriseId, created!.EnterpriseId);
            Assert.Equal("Maria", created.Name);
            Assert.Equal("52998224725", created.Document);
            Assert.True(created.IsActive);
            Assert.Equal("base64-payload", picture);
        }

        [Fact]
        public async Task UpdateEmployee_OnAnotherTenantsRow_AnswersNoContentAndWritesNothing()
        {
            var foreign = NewEmployee(Guid.NewGuid());
            _repository.Setup(r => r.GetByIdAsync(foreign.Id)).ReturnsAsync(foreign);

            var result = await Controller().UpdateEmployee(new UpdateEmployeeRequestDTO
            {
                Id = foreign.Id, Name = "X", Email = "x@e.com", PhoneNumber = "0", Position = "P", IsActive = true
            });

            Assert.IsType<NoContentResult>(result);
            _service.Verify(s => s.Update(It.IsAny<Employee>()), Times.Never);
        }

        /// <summary>
        /// A termination before the hire date is refused against the **stored** hire date, not the
        /// one in the body — otherwise a request could move both and land somewhere impossible.
        /// </summary>
        [Fact]
        public async Task UpdateEmployee_TerminatedBeforeTheStoredHireDate_IsRefused()
        {
            var employee = NewEmployee();
            _repository.Setup(r => r.GetByIdAsync(employee.Id)).ReturnsAsync(employee);

            var result = await Controller().UpdateEmployee(new UpdateEmployeeRequestDTO
            {
                Id = employee.Id,
                Name = "Maria", Email = "m@e.com", PhoneNumber = "0", Position = "P",
                TerminationDate = new DateTime(2025, 12, 31, 0, 0, 0, DateTimeKind.Utc),
                IsActive = true
            });

            var bad = Assert.IsType<BadRequestObjectResult>(result);
            Assert.Equal("Employee termination date cannot be before the hire date.", bad.Value);
            _service.Verify(s => s.Update(It.IsAny<Employee>()), Times.Never);
        }

        /// <summary>
        /// Null means "leave it alone" on the optional fields — a client that sends only a name
        /// must not blank a salary or a document.
        /// </summary>
        [Fact]
        public async Task UpdateEmployee_WithOnlyTheRequiredFields_KeepsTheOptionalOnesAsStored()
        {
            var employee = NewEmployee();
            employee.Salary = 3000m;
            employee.Document = "52998224725";
            _repository.Setup(r => r.GetByIdAsync(employee.Id)).ReturnsAsync(employee);
            _service.Setup(s => s.Update(It.IsAny<Employee>())).Returns<Employee>(e => e);

            Assert.IsType<OkObjectResult>(await Controller().UpdateEmployee(new UpdateEmployeeRequestDTO
            {
                Id = employee.Id,
                Name = "  Maria Silva  ", Email = " m@e.com ", PhoneNumber = " 111 ", Position = " Gerente ",
                Document = null, Salary = null, HireDate = null, TerminationDate = null,
                IsActive = true
            }));

            Assert.Equal("Maria Silva", employee.Name);
            Assert.Equal("Gerente", employee.Position);
            Assert.Equal(3000m, employee.Salary);
            Assert.Equal("52998224725", employee.Document);
        }

        [Fact]
        public async Task DeleteEmployee_OnAnotherTenantsRow_AnswersNoContentAndDeletesNothing()
        {
            var foreign = NewEmployee(Guid.NewGuid());
            _repository.Setup(r => r.GetByIdAsync(foreign.Id)).ReturnsAsync(foreign);

            Assert.IsType<NoContentResult>(await Controller().DeleteEmployee(foreign.Id));
            _service.Verify(s => s.Delete(It.IsAny<Guid>()), Times.Never);
        }

        /// <summary>The deleted row goes back without its picture: nobody is looking at a photo here.</summary>
        [Fact]
        public async Task DeleteEmployee_AnswersTheRowWithoutItsPicture()
        {
            var employee = NewEmployee(picture: "employees/maria.jpg");
            _repository.Setup(r => r.GetByIdAsync(employee.Id)).ReturnsAsync(employee);
            _service.Setup(s => s.Delete(employee.Id)).Returns(employee);

            var result = Assert.IsType<OkObjectResult>(await Controller().DeleteEmployee(employee.Id));

            Assert.Equal(string.Empty, Assert.IsType<EmployeeDTO>(result.Value).Picture);
            _service.Verify(s => s.GetPictureBase64Async(It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task UploadPicture_WithNoEmployeeId_IsRefused()
        {
            var result = Assert.IsType<BadRequestObjectResult>(
                await Controller().UploadPicture(new UploadEmployeePictureRequestDTO { EmployeeId = Guid.Empty }));

            Assert.Equal("Employee id is required.", result.Value);
        }

        [Fact]
        public async Task UploadPicture_OnAnotherTenantsEmployee_AnswersNoContentAndStoresNothing()
        {
            var foreign = NewEmployee(Guid.NewGuid());
            _repository.Setup(r => r.GetByIdAsync(foreign.Id)).ReturnsAsync(foreign);

            var result = await Controller().UploadPicture(
                new UploadEmployeePictureRequestDTO { EmployeeId = foreign.Id, File = "base64" });

            Assert.IsType<NoContentResult>(result);
            _service.Verify(s => s.UpdatePictureAsync(It.IsAny<Employee>(), It.IsAny<string?>()), Times.Never);
        }

        /// <summary>An empty payload clears the stored picture rather than being rejected.</summary>
        [Fact]
        public async Task UploadPicture_WithAnEmptyPayload_ClearsThePicture()
        {
            var employee = NewEmployee(picture: "employees/maria.jpg");
            _repository.Setup(r => r.GetByIdAsync(employee.Id)).ReturnsAsync(employee);
            _service.Setup(s => s.UpdatePictureAsync(employee, string.Empty))
                .ReturnsAsync(NewEmployee());

            Assert.IsType<OkObjectResult>(await Controller().UploadPicture(
                new UploadEmployeePictureRequestDTO { EmployeeId = employee.Id, File = string.Empty }));

            _service.VerifyAll();
        }

        [Fact]
        public async Task UploadPicture_WithoutATenantClaim_IsUnauthorized()
        {
            Assert.IsType<UnauthorizedResult>(await Anonymous().UploadPicture(
                new UploadEmployeePictureRequestDTO { EmployeeId = Guid.NewGuid(), File = "base64" }));
        }
    }
}
