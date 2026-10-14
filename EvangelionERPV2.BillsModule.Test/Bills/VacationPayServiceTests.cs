using System.Linq.Expressions;
using EvangelionERPV2.BillsModule.Application.Interface;
using EvangelionERPV2.BillsModule.Application.Services;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Enums;
using EvangelionERPV2.Shared.Exceptions;
using EvangelionERPV2.Shared.Repositories;
using Moq;

namespace EvangelionERPV2.BillsModule.Test
{
    /// <summary>
    /// Férias pay is money leaving the company on a legal deadline, so every test here is about an
    /// amount or a date being exactly right rather than merely present.
    /// </summary>
    public class VacationPayServiceTests
    {
        private static readonly DateTime FeriasStart = new(2026, 7, 6, 0, 0, 0, DateTimeKind.Utc);

        #region Amounts

        [Fact]
        public async Task GenerateAsync_ForAWholeMonth_PaysSalaryPlusTheConstitutionalThird()
        {
            var enterpriseId = Guid.NewGuid();
            var (accrual, employee) = BuildAccrual(enterpriseId, salary: 3000m);
            var stretch = BuildStretch(employee, FeriasStart, days: 31);

            var (sut, stores) = BuildSut(accrual, employee, [stretch]);

            var payment = Assert.Single(await sut.GenerateAsync(enterpriseId, accrual.Id));

            Assert.Equal(3000m, payment.SalaryPortion);
            Assert.Equal(1000m, payment.ConstitutionalThird);
            Assert.Equal(0m, payment.AbonoAmount);
            Assert.Equal(4000m, payment.TotalAmount);

            var bill = Assert.Single(stores.createdBills);
            Assert.Equal(4000m, bill.Amount);
            Assert.Equal((int)EnumPayableBillType.Vacation, bill.BillType);
            Assert.Equal(accrual.Id, bill.EmployeeVacationAccrualId);
            Assert.Equal(enterpriseId, bill.EnterpriseId);
            Assert.Contains(employee.Name, bill.Description);
        }

        [Fact]
        public async Task GenerateAsync_ForASplitPeriod_PaysOnlyThatStretch()
        {
            var enterpriseId = Guid.NewGuid();
            var (accrual, employee) = BuildAccrual(enterpriseId, salary: 3100m);
            var stretch = BuildStretch(employee, FeriasStart, days: 10);

            var (sut, _) = BuildSut(accrual, employee, [stretch]);

            var payment = Assert.Single(await sut.GenerateAsync(enterpriseId, accrual.Id));

            Assert.Equal(1000m, payment.SalaryPortion);
            Assert.Equal(333.33m, payment.ConstitutionalThird);
            Assert.Equal(1333.33m, payment.TotalAmount);
            Assert.Equal(10, payment.Days);
        }

        [Fact]
        public async Task GenerateAsync_WithAbono_PaysTheSoldDaysWithTheirOwnThird()
        {
            var enterpriseId = Guid.NewGuid();
            var (accrual, employee) = BuildAccrual(enterpriseId, salary: 3100m);
            accrual.SoldDays = 10;
            var stretch = BuildStretch(employee, FeriasStart, days: 20);

            var (sut, _) = BuildSut(accrual, employee, [stretch]);

            var payment = Assert.Single(await sut.GenerateAsync(enterpriseId, accrual.Id));

            Assert.Equal(2000m, payment.SalaryPortion);
            Assert.Equal(666.67m, payment.ConstitutionalThird);
            Assert.Equal(10, payment.SoldDays);
            Assert.Equal(1000m, payment.AbonoAmount);

            Assert.Equal(333.33m, payment.AbonoConstitutionalThird);
            Assert.Equal(4000m, payment.TotalAmount);
        }

        [Fact]
        public async Task GenerateAsync_WithoutSoldDays_PaysNoAbono()
        {
            var enterpriseId = Guid.NewGuid();
            var (accrual, employee) = BuildAccrual(enterpriseId, salary: 3100m);
            var stretch = BuildStretch(employee, FeriasStart, days: 20);

            var (sut, _) = BuildSut(accrual, employee, [stretch]);

            var payment = Assert.Single(await sut.GenerateAsync(enterpriseId, accrual.Id));

            Assert.Equal(0m, payment.AbonoAmount);
            Assert.Equal(0m, payment.AbonoConstitutionalThird);
        }

        [Fact]
        public async Task GenerateAsync_AttachesTheAbonoToOneStretchOnly()
        {
            var enterpriseId = Guid.NewGuid();
            var (accrual, employee) = BuildAccrual(enterpriseId, salary: 3100m);
            accrual.SoldDays = 10;

            var first = BuildStretch(employee, FeriasStart, days: 14);
            var second = BuildStretch(employee, FeriasStart.AddMonths(2), days: 6);

            var (sut, _) = BuildSut(accrual, employee, [first, second]);

            var payments = (await sut.GenerateAsync(enterpriseId, accrual.Id)).ToList();

            Assert.Equal(2, payments.Count);

            Assert.Equal(1, payments.Count(x => x.AbonoAmount > 0));
            Assert.Equal(10, payments.Single(x => x.AbonoAmount > 0).SoldDays);
        }

        [Fact]
        public async Task GenerateAsync_WithDaysSoldAfterTheFirstBill_BillsThemOnTheNextStretch()
        {
            var enterpriseId = Guid.NewGuid();
            var (accrual, employee) = BuildAccrual(enterpriseId, salary: 3000m);

            var first = BuildStretch(employee, FeriasStart, days: 14);
            var second = BuildStretch(employee, FeriasStart.AddMonths(3), days: 16);

            accrual.SoldDays = 7;

            var alreadyBilled = new PayableBill
            {
                Id = Guid.NewGuid(),
                EnterpriseId = enterpriseId,
                BillType = (int)EnumPayableBillType.Vacation,
                EmployeeVacationAccrualId = accrual.Id,
                DueDate = first.StartDate.AddDays(-2),
                Amount = 1806.45m,
                AbonoDaysSettled = 4,
                IsActive = true
            };

            var (sut, stores) = BuildSut(accrual, employee, [first, second], payableBills: [alreadyBilled]);

            await sut.GenerateAsync(enterpriseId, accrual.Id);

            var created = Assert.Single(stores.createdBills);
            Assert.Equal(second.StartDate.AddDays(-2).Date, created.DueDate.Date);
            Assert.Equal(3, created.AbonoDaysSettled);
        }

        [Fact]
        public async Task GenerateAsync_WithALegacyBillWhoseStretchWasCancelled_DoesNotPayTheAbonoTwice()
        {
            var enterpriseId = Guid.NewGuid();
            var (accrual, employee) = BuildAccrual(enterpriseId, salary: 3100m);
            accrual.SoldDays = 10;

            var cancelledStretchBill = new PayableBill
            {
                Id = Guid.NewGuid(),
                EnterpriseId = enterpriseId,
                BillType = (int)EnumPayableBillType.Vacation,
                EmployeeVacationAccrualId = accrual.Id,
                DueDate = FeriasStart.AddMonths(-1).AddDays(-2),
                Amount = 4000m,
                GrossAmount = 4000m,
                AbonoDaysSettled = null,
                IsActive = true
            };

            var rescheduled = BuildStretch(employee, FeriasStart, days: 20);

            var (sut, stores) = BuildSut(
                accrual, employee, [rescheduled], payableBills: [cancelledStretchBill]);

            await sut.GenerateAsync(enterpriseId, accrual.Id);

            var created = Assert.Single(stores.createdBills);
            Assert.Equal(0, created.AbonoDaysSettled);
            Assert.Equal(2000m + 666.67m, created.Amount);
        }

        [Fact]
        public async Task GenerateAsync_WithEveryDaySettledByAnEarlierBill_BillsNoAbonoAgain()
        {
            var enterpriseId = Guid.NewGuid();
            var (accrual, employee) = BuildAccrual(enterpriseId, salary: 3000m);

            var first = BuildStretch(employee, FeriasStart, days: 14);
            var second = BuildStretch(employee, FeriasStart.AddMonths(3), days: 16);

            accrual.SoldDays = 10;

            var alreadyBilled = new PayableBill
            {
                Id = Guid.NewGuid(),
                EnterpriseId = enterpriseId,
                BillType = (int)EnumPayableBillType.Vacation,
                EmployeeVacationAccrualId = accrual.Id,
                DueDate = first.StartDate.AddDays(-2),
                Amount = 1806.45m,
                AbonoDaysSettled = 10,
                IsActive = true
            };

            var (sut, stores) = BuildSut(accrual, employee, [first, second], payableBills: [alreadyBilled]);

            await sut.GenerateAsync(enterpriseId, accrual.Id);

            var created = Assert.Single(stores.createdBills);
            Assert.Equal(0, created.AbonoDaysSettled);
        }

        #endregion

        #region Due date

        [Fact]
        public async Task GenerateAsync_SetsTheDueDateTwoDaysBeforeTheLeaveStarts()
        {
            var enterpriseId = Guid.NewGuid();
            var (accrual, employee) = BuildAccrual(enterpriseId, salary: 3000m);
            var stretch = BuildStretch(employee, FeriasStart, days: 30);

            var (sut, stores) = BuildSut(accrual, employee, [stretch]);

            var payment = Assert.Single(await sut.GenerateAsync(enterpriseId, accrual.Id));

            Assert.Equal(FeriasStart.AddDays(-2).Date, payment.DueDate.Date);
            Assert.Equal(FeriasStart.AddDays(-2).Date, Assert.Single(stores.createdBills).DueDate.Date);
        }

        [Fact]
        public async Task GenerateAsync_ForASplitPeriod_GivesEachStretchItsOwnDueDate()
        {
            var enterpriseId = Guid.NewGuid();
            var (accrual, employee) = BuildAccrual(enterpriseId, salary: 3000m);

            var first = BuildStretch(employee, FeriasStart, days: 14);
            var second = BuildStretch(employee, FeriasStart.AddMonths(3), days: 16);

            var (sut, stores) = BuildSut(accrual, employee, [first, second]);

            var payments = (await sut.GenerateAsync(enterpriseId, accrual.Id)).ToList();

            Assert.Equal(2, stores.createdBills.Count);
            Assert.Equal(first.StartDate.AddDays(-2).Date, payments[0].DueDate.Date);
            Assert.Equal(second.StartDate.AddDays(-2).Date, payments[1].DueDate.Date);
        }

        #endregion

        #region Idempotency

        [Fact]
        public async Task GenerateAsync_RunTwice_CreatesOneBill()
        {
            var enterpriseId = Guid.NewGuid();
            var (accrual, employee) = BuildAccrual(enterpriseId, salary: 3000m);
            var stretch = BuildStretch(employee, FeriasStart, days: 30);

            var (sut, stores) = BuildSut(accrual, employee, [stretch]);

            var first = Assert.Single(await sut.GenerateAsync(enterpriseId, accrual.Id));
            var second = Assert.Single(await sut.GenerateAsync(enterpriseId, accrual.Id));

            Assert.Single(stores.createdBills);
            Assert.Equal(first.PayableBillId, second.PayableBillId);
        }

        [Fact]
        public async Task GenerateAsync_WithOneStretchAlreadyBilled_BillsOnlyTheOther()
        {
            var enterpriseId = Guid.NewGuid();
            var (accrual, employee) = BuildAccrual(enterpriseId, salary: 3000m);

            var first = BuildStretch(employee, FeriasStart, days: 14);
            var second = BuildStretch(employee, FeriasStart.AddMonths(3), days: 16);

            var existing = new PayableBill
            {
                Id = Guid.NewGuid(),
                EnterpriseId = enterpriseId,
                BillType = (int)EnumPayableBillType.Vacation,
                EmployeeVacationAccrualId = accrual.Id,
                DueDate = first.StartDate.AddDays(-2),
                Amount = 1806.45m,
                IsActive = true
            };

            var (sut, stores) = BuildSut(accrual, employee, [first, second], payableBills: [existing]);

            var payments = (await sut.GenerateAsync(enterpriseId, accrual.Id)).ToList();

            Assert.Equal(2, payments.Count);
            Assert.Single(stores.createdBills);
            Assert.Equal(second.StartDate.AddDays(-2).Date, stores.createdBills[0].DueDate.Date);
        }

        #endregion

        #region Guards

        [Fact]
        public async Task GenerateAsync_WithNothingScheduled_Throws()
        {
            var enterpriseId = Guid.NewGuid();
            var (accrual, employee) = BuildAccrual(enterpriseId, salary: 3000m);

            var (sut, _) = BuildSut(accrual, employee, []);

            await Assert.ThrowsAsync<InsertDatabaseException>(() => sut.GenerateAsync(enterpriseId, accrual.Id));
        }

        [Fact]
        public async Task GenerateAsync_ForAnotherTenantsAccrual_Throws()
        {
            var (accrual, employee) = BuildAccrual(Guid.NewGuid(), salary: 3000m);
            var stretch = BuildStretch(employee, FeriasStart, days: 30);

            var (sut, _) = BuildSut(accrual, employee, [stretch]);

            await Assert.ThrowsAsync<NotFoundDatabaseException>(
                () => sut.GenerateAsync(Guid.NewGuid(), accrual.Id));
        }

        [Fact]
        public async Task GenerateAsync_WithoutASalaryOnRecord_Throws()
        {
            var enterpriseId = Guid.NewGuid();
            var (accrual, employee) = BuildAccrual(enterpriseId, salary: 3000m);
            employee.Salary = null;
            var stretch = BuildStretch(employee, FeriasStart, days: 30);

            var (sut, _) = BuildSut(accrual, employee, [stretch]);

            await Assert.ThrowsAsync<InsertDatabaseException>(() => sut.GenerateAsync(enterpriseId, accrual.Id));
        }

        #endregion

        #region Read back

        [Fact]
        public async Task GetAsync_ReturnsTheIssuedPayments()
        {
            var enterpriseId = Guid.NewGuid();
            var (accrual, employee) = BuildAccrual(enterpriseId, salary: 3000m);
            var stretch = BuildStretch(employee, FeriasStart, days: 31);

            var (sut, _) = BuildSut(accrual, employee, [stretch]);
            await sut.GenerateAsync(enterpriseId, accrual.Id);

            var payment = Assert.Single(await sut.GetAsync(enterpriseId, accrual.Id));

            Assert.Equal(4000m, payment.TotalAmount);
            Assert.Equal(employee.Name, payment.EmployeeName);
        }

        [Fact]
        public async Task GetAsync_WithoutAnyPayment_ReturnsEmpty()
        {
            var enterpriseId = Guid.NewGuid();
            var (accrual, employee) = BuildAccrual(enterpriseId, salary: 3000m);

            var (sut, _) = BuildSut(accrual, employee, [BuildStretch(employee, FeriasStart, days: 30)]);

            Assert.Empty(await sut.GetAsync(enterpriseId, accrual.Id));
        }

        #endregion

        #region Provision netting

        [Fact]
        public async Task GenerateAsync_NetsTheBillByTheProvisionAlreadyAccruedForIt()
        {
            var enterpriseId = Guid.NewGuid();
            var (accrual, employee) = BuildAccrual(enterpriseId, salary: 3000m);
            var stretch = BuildStretch(employee, FeriasStart, days: 31);

            var (sut, stores) = BuildSut(accrual, employee, [stretch], competencias:
            [
                BuildCompetencia(employee, 2025, 1, 1000m),
                BuildCompetencia(employee, 2025, 2, 500m)
            ]);

            var payment = Assert.Single(await sut.GenerateAsync(enterpriseId, accrual.Id));

            Assert.Equal(4000m, payment.TotalAmount);
            Assert.Equal(1500m, payment.ProvisionOffset);
            Assert.Equal(2500m, payment.NetAmount);

            var bill = Assert.Single(stores.createdBills);
            Assert.Equal(2500m, bill.Amount);
            Assert.Equal(4000m, bill.GrossAmount);
        }

        [Fact]
        public async Task GenerateAsync_WhenTheProvisionCoversTheWholeBill_NetsItToZeroAndNoFurther()
        {
            var enterpriseId = Guid.NewGuid();
            var (accrual, employee) = BuildAccrual(enterpriseId, salary: 3000m);
            var stretch = BuildStretch(employee, FeriasStart, days: 31);
            var competencia = BuildCompetencia(employee, 2025, 1, 9000m);

            var (sut, stores) = BuildSut(accrual, employee, [stretch], competencias: [competencia]);

            var payment = Assert.Single(await sut.GenerateAsync(enterpriseId, accrual.Id));

            Assert.Equal(0m, payment.NetAmount);
            Assert.Equal(4000m, payment.ProvisionOffset);
            Assert.Equal(0m, Assert.Single(stores.createdBills).Amount);

            Assert.Equal(4000m, competencia.VacationProvisionConsumed);
        }

        [Fact]
        public async Task GenerateAsync_RunTwice_NetsTheBillOnlyOnce()
        {
            var enterpriseId = Guid.NewGuid();
            var (accrual, employee) = BuildAccrual(enterpriseId, salary: 3000m);
            var stretch = BuildStretch(employee, FeriasStart, days: 31);

            var (sut, stores) = BuildSut(accrual, employee, [stretch], competencias:
            [
                BuildCompetencia(employee, 2025, 1, 1500m)
            ]);

            await sut.GenerateAsync(enterpriseId, accrual.Id);
            var payment = Assert.Single(await sut.GenerateAsync(enterpriseId, accrual.Id));

            Assert.Equal(2500m, payment.NetAmount);
            Assert.Equal(2500m, Assert.Single(stores.billStore).Amount);
        }

        [Fact]
        public async Task GenerateAsync_SplitFerias_NetsTheEarliestDueBillFirst()
        {
            var enterpriseId = Guid.NewGuid();
            var (accrual, employee) = BuildAccrual(enterpriseId, salary: 3000m);

            var first = BuildStretch(employee, FeriasStart, days: 31);
            var second = BuildStretch(employee, FeriasStart.AddMonths(2), days: 30);

            var (sut, _) = BuildSut(accrual, employee, [first, second], competencias:
            [
                BuildCompetencia(employee, 2025, 1, 5000m)
            ]);

            var payments = (await sut.GenerateAsync(enterpriseId, accrual.Id))
                .OrderBy(x => x.DueDate)
                .ToList();

            Assert.Equal(0m, payments[0].NetAmount);
            Assert.Equal(3000m, payments[1].NetAmount);
            Assert.Equal(1000m, payments[1].ProvisionOffset);
        }

        [Fact]
        public async Task GetAsync_AfterNetting_StillReportsTheAbonoItSettled()
        {
            var enterpriseId = Guid.NewGuid();
            var (accrual, employee) = BuildAccrual(enterpriseId, salary: 3100m);
            accrual.SoldDays = 10;
            var stretch = BuildStretch(employee, FeriasStart, days: 20);

            var (sut, _) = BuildSut(accrual, employee, [stretch], competencias:
            [
                BuildCompetencia(employee, 2025, 1, 3000m)
            ]);

            await sut.GenerateAsync(enterpriseId, accrual.Id);

            var payment = Assert.Single(await sut.GetAsync(enterpriseId, accrual.Id));

            Assert.Equal(10, payment.SoldDays);
            Assert.Equal(1000m, payment.AbonoAmount);
            Assert.Equal(4000m, payment.TotalAmount);
            Assert.Equal(1000m, payment.NetAmount);
        }

        #endregion

        private static (EmployeeVacationAccrual Accrual, Employee Employee) BuildAccrual(Guid enterpriseId, decimal salary)
        {
            var employee = new Employee
            {
                Id = Guid.NewGuid(),
                Name = "Rei",
                EnterpriseId = enterpriseId,
                Salary = salary,
                HireDate = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                IsActive = true
            };

            var accrual = new EmployeeVacationAccrual
            {
                Id = Guid.NewGuid(),
                EmployeeId = employee.Id,
                EnterpriseId = enterpriseId,
                AcquisitionStart = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                AcquisitionEnd = new DateTime(2025, 12, 31, 0, 0, 0, DateTimeKind.Utc),
                ConcessionDeadline = new DateTime(2026, 12, 31, 0, 0, 0, DateTimeKind.Utc),
                EntitledDays = 30,
                Status = (int)EnumVacationAccrualStatus.Available,
                IsActive = true
            };

            return (accrual, employee);
        }

        private static EmployeeAbsence BuildStretch(Employee employee, DateTime start, int days) =>
            new()
            {
                Id = Guid.NewGuid(),
                EmployeeId = employee.Id,
                EnterpriseId = employee.EnterpriseId,
                Type = (int)EnumEmployeeAbsenceType.Vacation,
                StartDate = start,
                EndDate = start.AddDays(days - 1),
                IsJustified = true,
                IsActive = true
            };

        /// <summary>
        /// A payroll competência inside the período aquisitivo, carrying the provision a férias
        /// payment draws down.
        /// </summary>
        private static PayrollBillDetail BuildCompetencia(Employee employee, int year, int month, decimal provision) =>
            new()
            {
                Id = Guid.NewGuid(),
                EmployeeId = employee.Id,
                ReferenceYear = year,
                ReferenceMonth = month,
                VacationProvision = provision,
                IsActive = true
            };

        private static (
            VacationPayService sut,
            (List<PayableBill> createdBills, List<PayableBill> billStore) stores)
            BuildSut(
                EmployeeVacationAccrual accrual,
                Employee employee,
                List<EmployeeAbsence> stretches,
                List<PayableBill>? payableBills = null,
                List<PayrollBillDetail>? competencias = null)
        {
            var billStore = payableBills ?? [];

            var accrualRepo = CreateRepositoryMock(new List<EmployeeVacationAccrual> { accrual });
            var employeeRepo = CreateRepositoryMock(new List<Employee> { employee });
            var absenceRepo = CreateRepositoryMock(stretches, throwsWhenEmpty: true);
            var billRepo = CreateRepositoryMock(billStore);
            var detailRepo = CreateRepositoryMock(competencias ?? []);

            var createdBills = new List<PayableBill>();
            var payableBillService = new Mock<IPayableBillService>();
            payableBillService
                .Setup(x => x.CreateAsync(It.IsAny<PayableBill>(), It.IsAny<bool>(), It.IsAny<bool>()))
                .ReturnsAsync((PayableBill bill, bool _, bool __) =>
                {
                    bill.Id = Guid.NewGuid();
                    bill.IsActive = true;
                    createdBills.Add(bill);
                    billStore.Add(bill);
                    return bill;
                });

            var drawdown = new VacationProvisionDrawdownService(
                accrualRepo.Object, billRepo.Object, detailRepo.Object);

            var sut = new VacationPayService(
                accrualRepo.Object,
                employeeRepo.Object,
                absenceRepo.Object,
                billRepo.Object,
                payableBillService.Object,
                drawdown);

            return (sut, (createdBills, billStore));
        }

        private static Mock<IRepository<TEntity>> CreateRepositoryMock<TEntity>(List<TEntity> records, bool throwsWhenEmpty = false)
            where TEntity : BaseEntity
        {
            var repo = new Mock<IRepository<TEntity>>();

            repo.Setup(x => x.GetAllAsync(It.IsAny<Expression<Func<TEntity, bool>>?>()))
                .ReturnsAsync((Expression<Func<TEntity, bool>>? predicate) =>
                {
                    var result = predicate == null ? records.ToList() : records.Where(predicate.Compile()).ToList();

                    if (throwsWhenEmpty && result.Count == 0)
                        throw new NotFoundDatabaseException();

                    return result;
                });

            repo.Setup(x => x.GetByIdAsync(It.IsAny<Guid>()))
                .Returns((Guid id) => Task.FromResult<TEntity>(records.FirstOrDefault(x => x.Id == id)!));

            repo.Setup(x => x.CreateAsync(It.IsAny<TEntity>()))
                .ReturnsAsync((TEntity entity) =>
                {
                    records.Add(entity);
                    return entity;
                });

            repo.Setup(x => x.Update(It.IsAny<TEntity>()))
                .Returns((TEntity entity) => entity);

            repo.Setup(x => x.UpdateRange(It.IsAny<IEnumerable<TEntity>>()))
                .Returns((IEnumerable<TEntity> entities) => entities);

            repo.Setup(x => x.CommitAsync(It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            repo.Setup(x => x.ExecuteInTransactionAsync(It.IsAny<Func<Task>>(), It.IsAny<CancellationToken>()))
                .Returns((Func<Task> operation, CancellationToken _) => operation());

            return repo;
        }
    }
}
