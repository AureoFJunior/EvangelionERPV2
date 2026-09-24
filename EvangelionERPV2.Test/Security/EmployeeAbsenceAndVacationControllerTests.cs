using System.Linq.Expressions;
using AutoMapper;
using EvangelionERPV2.EmployeeModule.Application.Interface;
using EvangelionERPV2.Shared.DTOs;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Enums;
using EvangelionERPV2.Shared.Exceptions;
using EvangelionERPV2.Shared.Repositories;
using EvangelionERPV2.Web.Controllers;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace EvangelionERPV2.Test.Security
{
    /// <summary>
    /// An absence row can carry a CID, which is health data under the LGPD. The tenant comparison
    /// after every read is what keeps one company's medical certificates out of another's list.
    /// </summary>
    public class EmployeeAbsenceControllerTests
    {
        private readonly Guid _enterpriseId = Guid.NewGuid();
        private readonly Guid _userId = Guid.NewGuid();
        private readonly Mock<IEmployeeAbsenceService<EmployeeAbsence>> _service = new();
        private readonly Mock<IRepository<EmployeeAbsence>> _repository = new();
        private readonly Mock<IRepository<Employee>> _employeeRepository = new();
        private readonly Mock<IRepository<User>> _userRepository = new();

        private EmployeeAbsenceController Controller(bool withUser = true) =>
            new EmployeeAbsenceController(
                    _service.Object, _repository.Object, _employeeRepository.Object, _userRepository.Object, Mapper())
                .WithTenant(_enterpriseId, withUser ? _userId : null);

        private EmployeeAbsenceController Anonymous() =>
            new EmployeeAbsenceController(
                    _service.Object, _repository.Object, _employeeRepository.Object, _userRepository.Object, Mapper())
                .WithoutTenant();

        private static IMapper Mapper()
        {
            var mapper = new Mock<IMapper>();
            mapper.Setup(m => m.Map<EmployeeAbsenceDTO>(It.IsAny<EmployeeAbsence>()))
                .Returns<EmployeeAbsence>(a => new EmployeeAbsenceDTO { Id = a.Id, Type = a.Type });
            mapper.Setup(m => m.Map<IEnumerable<EmployeeAbsenceDTO>>(It.IsAny<IEnumerable<EmployeeAbsence>>()))
                .Returns<IEnumerable<EmployeeAbsence>>(all => all.Select(a => new EmployeeAbsenceDTO { Id = a.Id }));
            return mapper.Object;
        }

        private EmployeeAbsence NewAbsence(Guid? enterpriseId = null, bool isActive = true) => new()
        {
            Id = Guid.NewGuid(),
            EmployeeId = Guid.NewGuid(),
            Type = (int)EnumEmployeeAbsenceType.Falta,
            StartDate = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
            EndDate = new DateTime(2026, 9, 2, 0, 0, 0, DateTimeKind.Utc),
            EnterpriseId = enterpriseId ?? _enterpriseId,
            IsActive = isActive
        };

        private void MembershipIsGood() =>
            _userRepository.Setup(r => r.GetByIdAsync(_userId))
                .ReturnsAsync(new User { Id = _userId, IsActive = true, EnterpriseId = _enterpriseId });

        private void EmployeeBelongsToTenant(Guid employeeId, Guid? enterpriseId = null) =>
            _employeeRepository.Setup(r => r.GetByIdAsync(employeeId))
                .ReturnsAsync(new Employee
                {
                    Id = employeeId,
                    EnterpriseId = enterpriseId ?? _enterpriseId,
                    IsActive = true
                });

        [Fact]
        public async Task GetEmployeeAbsences_WithoutATenantClaim_IsUnauthorized()
        {
            Assert.IsType<UnauthorizedResult>(await Anonymous().GetEmployeeAbsences());
        }

        [Fact]
        public async Task GetEmployeeAbsences_ScopesToLiveRowsOfTheCallersTenant()
        {
            Expression<Func<EmployeeAbsence, bool>>? captured = null;
            _repository
                .Setup(r => r.GetAllAsync(It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<Expression<Func<EmployeeAbsence, bool>>>()))
                .Callback<int?, int?, Expression<Func<EmployeeAbsence, bool>>?>((_, _, p) => captured = p)
                .ReturnsAsync([NewAbsence()]);

            Assert.IsType<OkObjectResult>(await Controller().GetEmployeeAbsences());

            var matches = captured!.Compile();
            Assert.True(matches(NewAbsence()));
            Assert.False(matches(NewAbsence(Guid.NewGuid())));
            Assert.False(matches(NewAbsence(isActive: false)));
        }

        /// <summary>
        /// The employee filter narrows within the tenant; it never replaces the tenant clause, so
        /// an id from another company still matches nothing.
        /// </summary>
        [Fact]
        public async Task GetEmployeeAbsences_FilteredByEmployee_StillCarriesTheTenantClause()
        {
            var employeeId = Guid.NewGuid();
            Expression<Func<EmployeeAbsence, bool>>? captured = null;
            _repository
                .Setup(r => r.GetAllAsync(It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<Expression<Func<EmployeeAbsence, bool>>>()))
                .Callback<int?, int?, Expression<Func<EmployeeAbsence, bool>>?>((_, _, p) => captured = p)
                .ReturnsAsync([]);

            await Controller().GetEmployeeAbsences(employeeId: employeeId);

            var matches = captured!.Compile();
            var mine = NewAbsence();
            mine.EmployeeId = employeeId;
            var theirs = NewAbsence(Guid.NewGuid());
            theirs.EmployeeId = employeeId;

            Assert.True(matches(mine));
            Assert.False(matches(theirs));
            Assert.False(matches(NewAbsence()));
        }

        [Fact]
        public async Task GetEmployeeAbsence_FromAnotherTenant_AnswersNoContent()
        {
            var foreign = NewAbsence(Guid.NewGuid());
            _repository.Setup(r => r.GetByIdAsync(foreign.Id)).ReturnsAsync(foreign);

            Assert.IsType<NoContentResult>(await Controller().GetEmployeeAbsence(foreign.Id));
        }

        [Fact]
        public async Task GetEmployeeAbsence_OwnedByTheCaller_AnswersTheRow()
        {
            var absence = NewAbsence();
            _repository.Setup(r => r.GetByIdAsync(absence.Id)).ReturnsAsync(absence);

            Assert.IsType<OkObjectResult>(await Controller().GetEmployeeAbsence(absence.Id));
        }

        /// <summary>
        /// Recording an absence against somebody else's employee answers as if they do not exist —
        /// the employee id is guessable and the answer must not confirm one.
        /// </summary>
        [Fact]
        public async Task AddEmployeeAbsence_AgainstAnotherTenantsEmployee_AnswersNoContent()
        {
            MembershipIsGood();
            var employeeId = Guid.NewGuid();
            EmployeeBelongsToTenant(employeeId, Guid.NewGuid());

            var result = await Controller().AddEmployeeAbsence(new CreateEmployeeAbsenceRequestDTO
            {
                EmployeeId = employeeId,
                Type = (int)EnumEmployeeAbsenceType.Falta,
                StartDate = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
                EndDate = new DateTime(2026, 9, 2, 0, 0, 0, DateTimeKind.Utc)
            });

            Assert.IsType<NoContentResult>(result);
            _service.Verify(s => s.CreateAsync(It.IsAny<EmployeeAbsence>(), It.IsAny<string?>()), Times.Never);
        }

        [Fact]
        public async Task AddEmployeeAbsence_WhenTheCallerNoLongerBelongsToTheTenant_IsUnauthorized()
        {
            _userRepository.Setup(r => r.GetByIdAsync(_userId))
                .ReturnsAsync(new User { Id = _userId, IsActive = true, EnterpriseId = Guid.NewGuid() });

            Assert.IsType<UnauthorizedResult>(await Controller().AddEmployeeAbsence(
                new CreateEmployeeAbsenceRequestDTO { EmployeeId = Guid.NewGuid() }));
        }

        /// <summary>
        /// A falta is unjustified until somebody says otherwise; an atestado is justified until
        /// somebody rejects it. That default decides whether the day cuts pay.
        /// </summary>
        [Theory]
        [InlineData((int)EnumEmployeeAbsenceType.Falta, false)]
        [InlineData((int)EnumEmployeeAbsenceType.Atestado, true)]
        [InlineData((int)EnumEmployeeAbsenceType.Advertencia, true)]
        public async Task AddEmployeeAbsence_DefaultsIsJustifiedFromTheType(int type, bool expected)
        {
            MembershipIsGood();
            var employeeId = Guid.NewGuid();
            EmployeeBelongsToTenant(employeeId);

            EmployeeAbsence? created = null;
            _service.Setup(s => s.CreateAsync(It.IsAny<EmployeeAbsence>(), It.IsAny<string?>()))
                .Callback<EmployeeAbsence, string?>((absence, _) => created = absence)
                .ReturnsAsync((EmployeeAbsence absence, string? _) => absence);

            await Controller().AddEmployeeAbsence(new CreateEmployeeAbsenceRequestDTO
            {
                EmployeeId = employeeId,
                Type = type,
                StartDate = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
                EndDate = new DateTime(2026, 9, 2, 0, 0, 0, DateTimeKind.Utc),
                IsJustified = null
            });

            Assert.Equal(expected, created!.IsJustified);
        }

        [Fact]
        public async Task AddEmployeeAbsence_WithAnExplicitIsJustified_KeepsIt()
        {
            MembershipIsGood();
            var employeeId = Guid.NewGuid();
            EmployeeBelongsToTenant(employeeId);

            EmployeeAbsence? created = null;
            _service.Setup(s => s.CreateAsync(It.IsAny<EmployeeAbsence>(), It.IsAny<string?>()))
                .Callback<EmployeeAbsence, string?>((absence, _) => created = absence)
                .ReturnsAsync((EmployeeAbsence absence, string? _) => absence);

            await Controller().AddEmployeeAbsence(new CreateEmployeeAbsenceRequestDTO
            {
                EmployeeId = employeeId,
                Type = (int)EnumEmployeeAbsenceType.Falta,
                StartDate = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
                EndDate = new DateTime(2026, 9, 2, 0, 0, 0, DateTimeKind.Utc),
                IsJustified = true
            });

            Assert.True(created!.IsJustified);
        }

        /// <summary>
        /// The CID never lands on the entity the controller builds — it is handed to the service as
        /// a separate argument, which is what lets it be encrypted rather than stored in the clear.
        /// </summary>
        [Fact]
        public async Task AddEmployeeAbsence_PassesTheCidSeparatelyFromTheRow()
        {
            MembershipIsGood();
            var employeeId = Guid.NewGuid();
            EmployeeBelongsToTenant(employeeId);

            string? cid = null;
            _service.Setup(s => s.CreateAsync(It.IsAny<EmployeeAbsence>(), It.IsAny<string?>()))
                .Callback<EmployeeAbsence, string?>((_, plainCid) => cid = plainCid)
                .ReturnsAsync((EmployeeAbsence absence, string? _) => absence);

            await Controller().AddEmployeeAbsence(new CreateEmployeeAbsenceRequestDTO
            {
                EmployeeId = employeeId,
                Type = (int)EnumEmployeeAbsenceType.Atestado,
                StartDate = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
                EndDate = new DateTime(2026, 9, 2, 0, 0, 0, DateTimeKind.Utc),
                Cid = "J11",
                Reason = "  Gripe  "
            });

            Assert.Equal("J11", cid);
        }

        [Fact]
        public async Task UpdateEmployeeAbsence_OnAnotherTenantsRow_AnswersNoContentAndWritesNothing()
        {
            var foreign = NewAbsence(Guid.NewGuid());
            _repository.Setup(r => r.GetByIdAsync(foreign.Id)).ReturnsAsync(foreign);

            var result = await Controller().UpdateEmployeeAbsence(new UpdateEmployeeAbsenceRequestDTO
            {
                Id = foreign.Id,
                Type = (int)EnumEmployeeAbsenceType.Falta,
                StartDate = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
                EndDate = new DateTime(2026, 9, 2, 0, 0, 0, DateTimeKind.Utc)
            });

            Assert.IsType<NoContentResult>(result);
            _service.Verify(s => s.Update(It.IsAny<EmployeeAbsence>(), It.IsAny<string?>()), Times.Never);
        }

        /// <summary>A null <c>IsJustified</c> keeps what was decided before, rather than resetting it.</summary>
        [Fact]
        public async Task UpdateEmployeeAbsence_WithNoIsJustified_KeepsTheStoredDecision()
        {
            var absence = NewAbsence();
            absence.IsJustified = true;
            _repository.Setup(r => r.GetByIdAsync(absence.Id)).ReturnsAsync(absence);
            _service.Setup(s => s.Update(It.IsAny<EmployeeAbsence>(), It.IsAny<string?>()))
                .Returns<EmployeeAbsence, string?>((a, _) => a);

            Assert.IsType<OkObjectResult>(await Controller().UpdateEmployeeAbsence(new UpdateEmployeeAbsenceRequestDTO
            {
                Id = absence.Id,
                Type = (int)EnumEmployeeAbsenceType.Falta,
                StartDate = new DateTime(2026, 9, 3, 0, 0, 0, DateTimeKind.Utc),
                EndDate = new DateTime(2026, 9, 4, 0, 0, 0, DateTimeKind.Utc),
                IsJustified = null
            }));

            Assert.True(absence.IsJustified);
            Assert.Equal(new DateTime(2026, 9, 3, 0, 0, 0, DateTimeKind.Utc), absence.StartDate);
        }

        [Fact]
        public async Task DeleteEmployeeAbsence_OnAnotherTenantsRow_AnswersNoContentAndDeletesNothing()
        {
            var foreign = NewAbsence(Guid.NewGuid());
            _repository.Setup(r => r.GetByIdAsync(foreign.Id)).ReturnsAsync(foreign);

            Assert.IsType<NoContentResult>(await Controller().DeleteEmployeeAbsence(foreign.Id));
            _service.Verify(s => s.Delete(It.IsAny<Guid>()), Times.Never);
        }

        [Fact]
        public async Task DeleteEmployeeAbsence_OwnedByTheCaller_DeletesIt()
        {
            var absence = NewAbsence();
            _repository.Setup(r => r.GetByIdAsync(absence.Id)).ReturnsAsync(absence);
            _service.Setup(s => s.Delete(absence.Id)).Returns(absence);

            Assert.IsType<OkObjectResult>(await Controller().DeleteEmployeeAbsence(absence.Id));
        }
    }

    /// <summary>
    /// Every write here is a CLT calculation with money behind it, so the accrual is resolved by the
    /// service against the tenant rather than trusted from the body.
    /// </summary>
    public class EmployeeVacationControllerTests
    {
        private readonly Guid _enterpriseId = Guid.NewGuid();
        private readonly Guid _userId = Guid.NewGuid();
        private readonly Mock<IEmployeeVacationService<EmployeeVacationAccrual>> _service = new();
        private readonly Mock<IRepository<EmployeeVacationAccrual>> _repository = new();
        private readonly Mock<IRepository<Employee>> _employeeRepository = new();
        private readonly Mock<IRepository<User>> _userRepository = new();

        private EmployeeVacationController Controller(bool withUser = true) =>
            new EmployeeVacationController(
                    _service.Object, _repository.Object, _employeeRepository.Object, _userRepository.Object, Mapper())
                .WithTenant(_enterpriseId, withUser ? _userId : null);

        private EmployeeVacationController Anonymous() =>
            new EmployeeVacationController(
                    _service.Object, _repository.Object, _employeeRepository.Object, _userRepository.Object, Mapper())
                .WithoutTenant();

        private static IMapper Mapper()
        {
            var mapper = new Mock<IMapper>();
            mapper.Setup(m => m.Map<EmployeeVacationAccrualDTO>(It.IsAny<EmployeeVacationAccrual>()))
                .Returns<EmployeeVacationAccrual>(a => new EmployeeVacationAccrualDTO { Id = a.Id });
            return mapper.Object;
        }

        private EmployeeVacationAccrual NewAccrual(Guid? enterpriseId = null, bool isActive = true) => new()
        {
            Id = Guid.NewGuid(),
            EmployeeId = Guid.NewGuid(),
            EnterpriseId = enterpriseId ?? _enterpriseId,
            IsActive = isActive
        };

        private void MembershipIsGood() =>
            _userRepository.Setup(r => r.GetByIdAsync(_userId))
                .ReturnsAsync(new User { Id = _userId, IsActive = true, EnterpriseId = _enterpriseId });

        [Fact]
        public async Task GetVacationAccruals_WithoutATenantClaim_IsUnauthorized()
        {
            Assert.IsType<UnauthorizedResult>(await Anonymous().GetVacationAccruals());
        }

        [Fact]
        public async Task GetVacationAccruals_ScopesToLiveRowsOfTheCallersTenant()
        {
            Expression<Func<EmployeeVacationAccrual, bool>>? captured = null;
            _repository
                .Setup(r => r.GetAllAsync(It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<Expression<Func<EmployeeVacationAccrual, bool>>>()))
                .Callback<int?, int?, Expression<Func<EmployeeVacationAccrual, bool>>?>((_, _, p) => captured = p)
                .ReturnsAsync([NewAccrual()]);

            Assert.IsType<OkObjectResult>(await Controller().GetVacationAccruals());

            var matches = captured!.Compile();
            Assert.True(matches(NewAccrual()));
            Assert.False(matches(NewAccrual(Guid.NewGuid())));
            Assert.False(matches(NewAccrual(isActive: false)));
        }

        [Fact]
        public async Task RefreshVacationAccruals_ForAnotherTenantsEmployee_AnswersNoContent()
        {
            MembershipIsGood();
            var employeeId = Guid.NewGuid();
            _employeeRepository.Setup(r => r.GetByIdAsync(employeeId))
                .ReturnsAsync(new Employee { Id = employeeId, EnterpriseId = Guid.NewGuid(), IsActive = true });

            Assert.IsType<NoContentResult>(await Controller().RefreshVacationAccruals(employeeId));
            _service.Verify(s => s.RefreshAsync(It.IsAny<Guid>(), It.IsAny<Guid>()), Times.Never);
        }

        [Fact]
        public async Task RefreshVacationAccruals_ForOwnEmployee_RebuildsTheLedger()
        {
            MembershipIsGood();
            var employeeId = Guid.NewGuid();
            _employeeRepository.Setup(r => r.GetByIdAsync(employeeId))
                .ReturnsAsync(new Employee { Id = employeeId, EnterpriseId = _enterpriseId, IsActive = true });
            _service.Setup(s => s.RefreshAsync(employeeId, _enterpriseId)).ReturnsAsync([NewAccrual()]);

            Assert.IsType<OkObjectResult>(await Controller().RefreshVacationAccruals(employeeId));
            _service.VerifyAll();
        }

        [Fact]
        public async Task RefreshVacationAccruals_WithNoUserClaim_IsUnauthorized()
        {
            Assert.IsType<UnauthorizedResult>(await Controller(withUser: false).RefreshVacationAccruals(Guid.NewGuid()));
        }

        [Fact]
        public async Task ScheduleVacation_ResolvesTheAccrualAgainstTheCallersTenant()
        {
            MembershipIsGood();
            var accrual = NewAccrual();
            var startDate = new DateTime(2026, 12, 1, 0, 0, 0, DateTimeKind.Utc);

            _service.Setup(s => s.ScheduleAsync(accrual.Id, _enterpriseId, startDate, 10)).ReturnsAsync(accrual);

            Assert.IsType<OkObjectResult>(await Controller().ScheduleVacation(new ScheduleVacationRequestDTO
            {
                AccrualId = accrual.Id,
                StartDate = startDate,
                Days = 10
            }));

            _service.VerifyAll();
        }

        /// <summary>
        /// An accrual id from another tenant is not found rather than forbidden — the id is
        /// guessable, and a 403 would confirm it exists.
        /// </summary>
        [Fact]
        public async Task ScheduleVacation_AgainstAnotherTenantsAccrual_AnswersNoContent()
        {
            MembershipIsGood();
            _service.Setup(s => s.ScheduleAsync(It.IsAny<Guid>(), _enterpriseId, It.IsAny<DateTime>(), It.IsAny<int>()))
                .ThrowsAsync(new NotFoundDatabaseException("no such accrual"));

            Assert.IsType<NoContentResult>(await Controller().ScheduleVacation(new ScheduleVacationRequestDTO
            {
                AccrualId = Guid.NewGuid(),
                StartDate = new DateTime(2026, 12, 1, 0, 0, 0, DateTimeKind.Utc),
                Days = 10
            }));
        }

        /// <summary>
        /// The art. 134 §1 split rules need the accrual's other booked periods, so the service is
        /// where they are enforced — and its refusal is a 400 the user can act on.
        /// </summary>
        [Fact]
        public async Task ScheduleVacation_ThatBreaksTheSplitRules_AnswersBadRequest()
        {
            MembershipIsGood();
            _service.Setup(s => s.ScheduleAsync(It.IsAny<Guid>(), _enterpriseId, It.IsAny<DateTime>(), It.IsAny<int>()))
                .ThrowsAsync(new InsertDatabaseException("One stretch must be at least 14 days (CLT art. 134 §1)."));

            var result = Assert.IsType<BadRequestObjectResult>(
                await Controller().ScheduleVacation(new ScheduleVacationRequestDTO
                {
                    AccrualId = Guid.NewGuid(),
                    StartDate = new DateTime(2026, 12, 1, 0, 0, 0, DateTimeKind.Utc),
                    Days = 5
                }));

            Assert.Contains("art. 134", result.Value?.ToString());
        }

        [Fact]
        public async Task ScheduleVacation_WhenTheCallerNoLongerBelongsToTheTenant_IsUnauthorized()
        {
            _userRepository.Setup(r => r.GetByIdAsync(_userId))
                .ReturnsAsync(new User { Id = _userId, IsActive = true, EnterpriseId = Guid.NewGuid() });

            Assert.IsType<UnauthorizedResult>(
                await Controller().ScheduleVacation(new ScheduleVacationRequestDTO { AccrualId = Guid.NewGuid(), Days = 10 }));
        }

        [Fact]
        public async Task SellVacationDays_ResolvesTheAccrualAgainstTheCallersTenant()
        {
            MembershipIsGood();
            var accrual = NewAccrual();
            _service.Setup(s => s.SellDaysAsync(accrual.Id, _enterpriseId, 10, null)).ReturnsAsync(accrual);

            Assert.IsType<OkObjectResult>(await Controller().SellVacationDays(
                new SellVacationDaysRequestDTO { AccrualId = accrual.Id, Days = 10 }));

            _service.VerifyAll();
        }

        /// <summary>
        /// The art. 143 third-of-the-entitlement cap depends on the accrual, so the edge's flat
        /// ten-day ceiling is not the whole rule — the service's refusal has to reach the user.
        /// </summary>
        [Fact]
        public async Task SellVacationDays_PastTheArt143Cap_AnswersBadRequest()
        {
            MembershipIsGood();
            _service.Setup(s => s.SellDaysAsync(It.IsAny<Guid>(), _enterpriseId, It.IsAny<int>(), null))
                .ThrowsAsync(new InsertDatabaseException("An abono cannot exceed a third of the entitlement (CLT art. 143)."));

            var result = Assert.IsType<BadRequestObjectResult>(await Controller().SellVacationDays(
                new SellVacationDaysRequestDTO { AccrualId = Guid.NewGuid(), Days = 10 }));

            Assert.Contains("art. 143", result.Value?.ToString());
        }

        [Fact]
        public async Task SellVacationDays_AgainstAnotherTenantsAccrual_AnswersNoContent()
        {
            MembershipIsGood();
            _service.Setup(s => s.SellDaysAsync(It.IsAny<Guid>(), _enterpriseId, It.IsAny<int>(), null))
                .ThrowsAsync(new NotFoundDatabaseException("no such accrual"));

            Assert.IsType<NoContentResult>(await Controller().SellVacationDays(
                new SellVacationDaysRequestDTO { AccrualId = Guid.NewGuid(), Days = 5 }));
        }

        [Fact]
        public async Task SellVacationDays_WithoutATenantClaim_IsUnauthorized()
        {
            Assert.IsType<UnauthorizedResult>(await Anonymous().SellVacationDays(new SellVacationDaysRequestDTO()));
        }
    }
}
