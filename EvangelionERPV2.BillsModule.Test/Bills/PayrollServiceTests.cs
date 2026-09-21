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
    public class PayrollServiceTests
    {
        [Fact]
        public async Task GeneratePayrollAsync_WithDefaultRates_ShouldSplitAllComponents()
        {
            var enterpriseId = Guid.NewGuid();
            var employee = BuildEmployee(enterpriseId, 3000m, "Rei");

            var (sut, stores) = BuildSut(employees: [employee]);

            var result = (await sut.GeneratePayrollAsync(enterpriseId, 2026, 7)).ToList();

            var breakdown = Assert.Single(result);
            Assert.Equal(employee.Id, breakdown.EmployeeId);
            Assert.Equal("Rei", breakdown.EmployeeName);
            Assert.Equal(3000m, breakdown.GrossSalary);
            Assert.Equal(250.00m, breakdown.ThirteenthSalaryProvision);
            Assert.Equal(333.33m, breakdown.VacationProvision);
            Assert.Equal(716.67m, breakdown.InssAmount);
            Assert.Equal(71.67m, breakdown.RatAmount);
            Assert.Equal(207.83m, breakdown.ThirdPartyAmount);
            Assert.Equal(286.67m, breakdown.FgtsAmount);
            Assert.Equal(4866.17m, breakdown.TotalCost);
            Assert.Equal(new DateTime(2026, 8, 5), breakdown.DueDate.Date);

            var bill = Assert.Single(stores.createdBills);
            Assert.Equal(breakdown.TotalCost, bill.Amount);
            Assert.Equal((int)EnumPayableBillType.Payroll, bill.BillType);
            Assert.Contains("Rei", bill.Description);
            Assert.Equal(enterpriseId, bill.EnterpriseId);

            var detail = Assert.Single(stores.details);
            Assert.Equal(bill.Id, detail.PayableBillId);
            Assert.Equal(2026, detail.ReferenceYear);
            Assert.Equal(7, detail.ReferenceMonth);
        }

        [Fact]
        public async Task GeneratePayrollAsync_ShouldCreateOneBillPerEmployee()
        {
            var enterpriseId = Guid.NewGuid();
            var employees = new List<Employee>
            {
                BuildEmployee(enterpriseId, 3000m, "Asuka"),
                BuildEmployee(enterpriseId, 1200m, "Shinji")
            };

            var (sut, stores) = BuildSut(employees: employees);

            var result = (await sut.GeneratePayrollAsync(enterpriseId, 2026, 7)).ToList();

            Assert.Equal(2, result.Count);
            Assert.Equal(2, stores.createdBills.Count);
            Assert.Equal(2, stores.details.Count);
            Assert.Equal(["Asuka", "Shinji"], result.Select(x => x.EmployeeName).ToArray());
        }

        [Fact]
        public async Task GeneratePayrollAsync_WithEmployeeId_ShouldGenerateOnlyForThatEmployee()
        {
            var enterpriseId = Guid.NewGuid();
            var target = BuildEmployee(enterpriseId, 3000m, "Target");
            var other = BuildEmployee(enterpriseId, 1200m, "Other");

            var (sut, stores) = BuildSut(employees: [target, other]);

            var result = (await sut.GeneratePayrollAsync(enterpriseId, 2026, 7, target.Id)).ToList();

            var breakdown = Assert.Single(result);
            Assert.Equal(target.Id, breakdown.EmployeeId);
            var bill = Assert.Single(stores.createdBills);
            Assert.Contains("Target", bill.Description);
            Assert.Single(stores.details);
        }

        [Fact]
        public async Task GeneratePayrollAsync_WithEmployeeIdAlreadyBilled_ShouldReturnExistingWithoutCreating()
        {
            var enterpriseId = Guid.NewGuid();
            var employee = BuildEmployee(enterpriseId, 3000m);

            var existingBill = new PayableBill
            {
                Id = Guid.NewGuid(),
                EnterpriseId = enterpriseId,
                BillType = (int)EnumPayableBillType.Payroll,
                DueDate = new DateTime(2026, 8, 5, 0, 0, 0, DateTimeKind.Utc),
                IsActive = true
            };
            var existingDetail = new PayrollBillDetail
            {
                Id = Guid.NewGuid(),
                EnterpriseId = enterpriseId,
                EmployeeId = employee.Id,
                PayableBillId = existingBill.Id,
                ReferenceYear = 2026,
                ReferenceMonth = 7,
                IsActive = true
            };

            var (sut, stores) = BuildSut(
                employees: [employee],
                payableBills: [existingBill],
                details: [existingDetail]);

            var result = (await sut.GeneratePayrollAsync(enterpriseId, 2026, 7, employee.Id)).ToList();

            Assert.Single(result);
            Assert.Equal(existingBill.Id, result[0].PayableBillId);
            Assert.Empty(stores.createdBills);
        }

        [Fact]
        public async Task GeneratePayrollAsync_WithIneligibleEmployeeId_ShouldThrow()
        {
            var enterpriseId = Guid.NewGuid();
            var eligible = BuildEmployee(enterpriseId, 3000m);
            var noSalary = BuildEmployee(enterpriseId, 0m);

            var (sut, _) = BuildSut(employees: [eligible, noSalary]);

            var ex = await Assert.ThrowsAsync<InsertDatabaseException>(
                () => sut.GeneratePayrollAsync(enterpriseId, 2026, 7, noSalary.Id));

            Assert.Equal("Employee is not eligible for this payroll period.", ex.Message);
        }

        [Fact]
        public async Task GeneratePayrollAsync_WithEmployeeIdFromOtherEnterprise_ShouldThrow()
        {
            var enterpriseId = Guid.NewGuid();
            var foreign = BuildEmployee(Guid.NewGuid(), 3000m);

            var (sut, _) = BuildSut(employees: [foreign]);

            await Assert.ThrowsAsync<InsertDatabaseException>(
                () => sut.GeneratePayrollAsync(enterpriseId, 2026, 7, foreign.Id));
        }

        [Fact]
        public async Task GeneratePayrollAsync_WithSimplesRates_ShouldOnlyApplyFgts()
        {
            var enterpriseId = Guid.NewGuid();
            var employee = BuildEmployee(enterpriseId, 1200m);
            var settings = new EnterprisePayrollSettings
            {
                Id = Guid.NewGuid(),
                EnterpriseId = enterpriseId,
                InssRate = 0m,
                RatRate = 0m,
                ThirdPartyRate = 0m,
                FgtsRate = 0.08m,
                PayrollDueDay = 10,
                IsActive = true
            };

            var (sut, _) = BuildSut(employees: [employee], settings: [settings]);

            var breakdown = Assert.Single(await sut.GeneratePayrollAsync(enterpriseId, 2026, 1));

            Assert.Equal(100.00m, breakdown.ThirteenthSalaryProvision);
            Assert.Equal(133.33m, breakdown.VacationProvision);
            Assert.Equal(0m, breakdown.InssAmount);
            Assert.Equal(0m, breakdown.RatAmount);
            Assert.Equal(0m, breakdown.ThirdPartyAmount);
            Assert.Equal(114.67m, breakdown.FgtsAmount);
            Assert.Equal(1548.00m, breakdown.TotalCost);
            Assert.Equal(new DateTime(2026, 2, 10), breakdown.DueDate.Date);
        }

        [Fact]
        public async Task GeneratePayrollAsync_WhenEmployeeAlreadyBilled_ShouldOnlyCreateMissingOnes()
        {
            var enterpriseId = Guid.NewGuid();
            var billed = BuildEmployee(enterpriseId, 3000m, "Billed");
            var pending = BuildEmployee(enterpriseId, 1200m, "Pending");

            var existingBill = new PayableBill
            {
                Id = Guid.NewGuid(),
                EnterpriseId = enterpriseId,
                BillType = (int)EnumPayableBillType.Payroll,
                DueDate = new DateTime(2026, 8, 5, 0, 0, 0, DateTimeKind.Utc),
                Amount = 4866.17m,
                IsActive = true
            };
            var existingDetail = new PayrollBillDetail
            {
                Id = Guid.NewGuid(),
                EnterpriseId = enterpriseId,
                EmployeeId = billed.Id,
                PayableBillId = existingBill.Id,
                ReferenceYear = 2026,
                ReferenceMonth = 7,
                GrossSalary = 3000m,
                TotalCost = 4866.17m,
                IsActive = true
            };

            var (sut, stores) = BuildSut(
                employees: [billed, pending],
                payableBills: [existingBill],
                details: [existingDetail]);

            var result = (await sut.GeneratePayrollAsync(enterpriseId, 2026, 7)).ToList();

            Assert.Equal(2, result.Count);
            var createdBill = Assert.Single(stores.createdBills);
            Assert.Contains("Pending", createdBill.Description);
            Assert.Equal(existingBill.Id, result.Single(x => x.EmployeeId == billed.Id).PayableBillId);
        }

        [Fact]
        public async Task GeneratePayrollAsync_WhenPreviousBillWasDeleted_ShouldRegenerate()
        {
            var enterpriseId = Guid.NewGuid();
            var employee = BuildEmployee(enterpriseId, 3000m);

            var deletedBill = new PayableBill
            {
                Id = Guid.NewGuid(),
                EnterpriseId = enterpriseId,
                BillType = (int)EnumPayableBillType.Payroll,
                DueDate = new DateTime(2026, 8, 5, 0, 0, 0, DateTimeKind.Utc),
                IsActive = false
            };
            var orphanDetail = new PayrollBillDetail
            {
                Id = Guid.NewGuid(),
                EnterpriseId = enterpriseId,
                EmployeeId = employee.Id,
                PayableBillId = deletedBill.Id,
                ReferenceYear = 2026,
                ReferenceMonth = 7,
                IsActive = true
            };

            var (sut, stores) = BuildSut(
                employees: [employee],
                payableBills: [deletedBill],
                details: [orphanDetail]);

            var result = (await sut.GeneratePayrollAsync(enterpriseId, 2026, 7)).ToList();

            Assert.Single(result);
            Assert.Single(stores.createdBills);
        }

        [Fact]
        public async Task GeneratePayrollAsync_ShouldExcludeTerminatedAndNotYetHiredEmployees()
        {
            var enterpriseId = Guid.NewGuid();
            var active = BuildEmployee(enterpriseId, 2000m, "Active");
            var terminatedBeforeMonth = BuildEmployee(enterpriseId, 5000m);
            terminatedBeforeMonth.TerminationDate = new DateTime(2026, 6, 30, 0, 0, 0, DateTimeKind.Utc);
            var hiredAfterMonth = BuildEmployee(enterpriseId, 7000m);
            hiredAfterMonth.HireDate = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc);
            var otherEnterprise = BuildEmployee(Guid.NewGuid(), 9000m);

            var (sut, _) = BuildSut(
                employees: [active, terminatedBeforeMonth, hiredAfterMonth, otherEnterprise]);

            var result = (await sut.GeneratePayrollAsync(enterpriseId, 2026, 7)).ToList();

            var breakdown = Assert.Single(result);
            Assert.Equal(active.Id, breakdown.EmployeeId);
        }

        [Fact]
        public async Task GeneratePayrollAsync_WithoutEligibleEmployees_ShouldThrow()
        {
            var (sut, _) = BuildSut();

            await Assert.ThrowsAsync<InsertDatabaseException>(
                () => sut.GeneratePayrollAsync(Guid.NewGuid(), 2026, 7));
        }

        [Fact]
        public async Task GeneratePayrollAsync_WhenEmployeeRepositoryThrowsOnEmpty_ShouldStillReportNoEligibleEmployees()
        {
            var (sut, _) = BuildSut(employeeRepositoryThrowsWhenEmpty: true);

            var ex = await Assert.ThrowsAsync<InsertDatabaseException>(
                () => sut.GeneratePayrollAsync(Guid.NewGuid(), 2026, 7));

            Assert.Equal("No active employees with salary were found for this payroll period.", ex.Message);
        }

        [Fact]
        public async Task GeneratePayrollAsync_WithDecemberPeriod_ShouldRollDueDateIntoJanuary()
        {
            var enterpriseId = Guid.NewGuid();
            var (sut, _) = BuildSut(employees: [BuildEmployee(enterpriseId, 3000m)]);

            var breakdown = Assert.Single(await sut.GeneratePayrollAsync(enterpriseId, 2026, 12));

            Assert.Equal(new DateTime(2027, 1, 5), breakdown.DueDate.Date);
        }

        [Fact]
        public async Task GeneratePayrollAsync_RunTwice_ShouldNotBillTheSameEmployeeAgain()
        {
            var enterpriseId = Guid.NewGuid();
            var employee = BuildEmployee(enterpriseId, 3000m);
            var (sut, stores) = BuildSut(employees: [employee]);

            var first = (await sut.GeneratePayrollAsync(enterpriseId, 2026, 7)).ToList();
            var second = (await sut.GeneratePayrollAsync(enterpriseId, 2026, 7)).ToList();

            Assert.Single(stores.createdBills);
            Assert.Single(stores.details);
            Assert.Equal(first.Single().PayableBillId, second.Single().PayableBillId);
        }

        [Fact]
        public async Task GeneratePayrollAsync_WhenOneEmployeeFails_ShouldNotCommitAnyBillWithoutItsDetail()
        {
            var enterpriseId = Guid.NewGuid();
            var first = BuildEmployee(enterpriseId, 3000m, "Aaa");
            var second = BuildEmployee(enterpriseId, 1200m, "Bbb");

            var (sut, stores) = BuildSut(
                employees: [first, second],
                failBillCreation: bill => bill.Description.Contains("Bbb"));

            await Assert.ThrowsAsync<InsertDatabaseException>(
                () => sut.GeneratePayrollAsync(enterpriseId, 2026, 7));

            Assert.Empty(stores.createdBills);
            Assert.Empty(stores.details);
        }

        [Fact]
        public async Task GeneratePayrollAsync_AfterPartialFailure_ShouldBillEachEmployeeExactlyOnce()
        {
            var enterpriseId = Guid.NewGuid();
            var first = BuildEmployee(enterpriseId, 3000m, "Aaa");
            var second = BuildEmployee(enterpriseId, 1200m, "Bbb");

            var failNext = true;
            var (sut, stores) = BuildSut(
                employees: [first, second],
                failBillCreation: bill => failNext && bill.Description.Contains("Bbb"));

            await Assert.ThrowsAsync<InsertDatabaseException>(
                () => sut.GeneratePayrollAsync(enterpriseId, 2026, 7));

            failNext = false;
            var result = (await sut.GeneratePayrollAsync(enterpriseId, 2026, 7)).ToList();

            Assert.Equal(2, result.Count);
            Assert.Equal(2, stores.createdBills.Count);
            Assert.Equal(2, stores.details.Count);
            Assert.Equal(3000m + 1200m, stores.details.Sum(x => x.GrossSalary));
        }

        [Fact]
        public async Task GeneratePayrollAsync_ShouldCreateBillsInsideTheAmbientTransaction()
        {
            var enterpriseId = Guid.NewGuid();
            var (sut, stores) = BuildSut(employees: [BuildEmployee(enterpriseId, 3000m)]);

            await sut.GeneratePayrollAsync(enterpriseId, 2026, 7);

            stores.payableBillService.Verify(
                x => x.CreateAsync(It.IsAny<PayableBill>(), true), Times.Once);
        }

        [Fact]
        public async Task GeneratePayrollAsync_WithHireDateOnTheFirstDay_ShouldBillTheFullMonth()
        {
            var enterpriseId = Guid.NewGuid();
            var employee = BuildEmployee(enterpriseId, 3000m);
            employee.HireDate = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc);

            var (sut, _) = BuildSut(employees: [employee]);

            var breakdown = Assert.Single(await sut.GeneratePayrollAsync(enterpriseId, 2026, 7));

            Assert.Equal(3000m, breakdown.GrossSalary);
            Assert.Equal(4866.17m, breakdown.TotalCost);
        }

        [Fact]
        public async Task GeneratePayrollAsync_WithMidMonthHire_ShouldProrateByWorkedDays()
        {
            var enterpriseId = Guid.NewGuid();
            var employee = BuildEmployee(enterpriseId, 3000m);
            employee.HireDate = new DateTime(2026, 7, 16, 0, 0, 0, DateTimeKind.Utc);

            var (sut, _) = BuildSut(employees: [employee]);

            var breakdown = Assert.Single(await sut.GeneratePayrollAsync(enterpriseId, 2026, 7));

            Assert.Equal(1548.39m, breakdown.GrossSalary);
            Assert.Equal(129.03m, breakdown.ThirteenthSalaryProvision);
            Assert.Equal(172.04m, breakdown.VacationProvision);
            Assert.Equal(369.89m, breakdown.InssAmount);
            Assert.Equal(36.99m, breakdown.RatAmount);
            Assert.Equal(107.27m, breakdown.ThirdPartyAmount);
            Assert.Equal(147.96m, breakdown.FgtsAmount);
            Assert.Equal(2511.57m, breakdown.TotalCost);
        }

        [Fact]
        public async Task GeneratePayrollAsync_WithHireOnTheLastDay_ShouldBillASingleDay()
        {
            var enterpriseId = Guid.NewGuid();
            var employee = BuildEmployee(enterpriseId, 3000m);
            employee.HireDate = new DateTime(2026, 7, 31, 0, 0, 0, DateTimeKind.Utc);

            var (sut, _) = BuildSut(employees: [employee]);

            var breakdown = Assert.Single(await sut.GeneratePayrollAsync(enterpriseId, 2026, 7));

            Assert.Equal(96.77m, breakdown.GrossSalary);
            Assert.Equal(156.96m, breakdown.TotalCost);
        }

        [Fact]
        public async Task GeneratePayrollAsync_WithMidMonthTermination_ShouldProrateByWorkedDays()
        {
            var enterpriseId = Guid.NewGuid();
            var employee = BuildEmployee(enterpriseId, 3000m);
            employee.TerminationDate = new DateTime(2026, 7, 15, 0, 0, 0, DateTimeKind.Utc);

            var (sut, _) = BuildSut(employees: [employee]);

            var breakdown = Assert.Single(await sut.GeneratePayrollAsync(enterpriseId, 2026, 7));

            Assert.Equal(1451.61m, breakdown.GrossSalary);
            Assert.Equal(2354.59m, breakdown.TotalCost);
        }

        [Fact]
        public async Task GeneratePayrollAsync_WithTerminationOnTheFirstDay_ShouldBillASingleDay()
        {
            var enterpriseId = Guid.NewGuid();
            var employee = BuildEmployee(enterpriseId, 3000m);
            employee.TerminationDate = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc);

            var (sut, _) = BuildSut(employees: [employee]);

            var breakdown = Assert.Single(await sut.GeneratePayrollAsync(enterpriseId, 2026, 7));

            Assert.Equal(96.77m, breakdown.GrossSalary);
            Assert.Equal(156.96m, breakdown.TotalCost);
        }

        [Fact]
        public async Task GeneratePayrollAsync_WithLeapFebruary_ShouldProrateOver29Days()
        {
            var enterpriseId = Guid.NewGuid();
            var employee = BuildEmployee(enterpriseId, 3000m);
            employee.HireDate = new DateTime(2028, 2, 15, 0, 0, 0, DateTimeKind.Utc);

            var (sut, _) = BuildSut(employees: [employee]);

            var breakdown = Assert.Single(await sut.GeneratePayrollAsync(enterpriseId, 2028, 2));

            Assert.Equal(1551.72m, breakdown.GrossSalary);
            Assert.Equal(2516.98m, breakdown.TotalCost);
        }

        [Fact]
        public async Task GeneratePayrollAsync_WithHalfCentValues_ShouldRoundAwayFromZero()
        {
            var enterpriseId = Guid.NewGuid();
            var (sut, _) = BuildSut(employees: [BuildEmployee(enterpriseId, 3000.06m)]);

            var breakdown = Assert.Single(await sut.GeneratePayrollAsync(enterpriseId, 2026, 7));

            Assert.Equal(250.01m, breakdown.ThirteenthSalaryProvision);
            Assert.Equal(4866.27m, breakdown.TotalCost);
        }

        [Theory]
        [InlineData(1999, 1)]
        [InlineData(2101, 1)]
        [InlineData(2026, 0)]
        [InlineData(2026, 13)]
        public async Task GeneratePayrollAsync_WithInvalidPeriod_ShouldThrow(int year, int month)
        {
            var (sut, _) = BuildSut(employees: [BuildEmployee(Guid.NewGuid(), 1000m)]);

            await Assert.ThrowsAsync<InsertDatabaseException>(
                () => sut.GeneratePayrollAsync(Guid.NewGuid(), year, month));
        }

        [Fact]
        public async Task GetPayrollAsync_ShouldReturnStoredBreakdown()
        {
            var enterpriseId = Guid.NewGuid();
            var employee = BuildEmployee(enterpriseId, 3000m, "Misato");
            employee.IsActive = false;

            var bill = new PayableBill
            {
                Id = Guid.NewGuid(),
                EnterpriseId = enterpriseId,
                BillType = (int)EnumPayableBillType.Payroll,
                DueDate = new DateTime(2026, 8, 5, 0, 0, 0, DateTimeKind.Utc),
                IsPaid = true,
                IsActive = true
            };
            var detail = new PayrollBillDetail
            {
                Id = Guid.NewGuid(),
                EnterpriseId = enterpriseId,
                EmployeeId = employee.Id,
                PayableBillId = bill.Id,
                ReferenceYear = 2026,
                ReferenceMonth = 7,
                GrossSalary = 3000m,
                ThirteenthSalaryProvision = 250m,
                VacationProvision = 333.33m,
                InssAmount = 716.67m,
                RatAmount = 71.67m,
                ThirdPartyAmount = 207.83m,
                FgtsAmount = 286.67m,
                TotalCost = 4866.17m,
                IsActive = true
            };

            var (sut, _) = BuildSut(employees: [employee], payableBills: [bill], details: [detail]);

            var result = (await sut.GetPayrollAsync(enterpriseId, 2026, 7)).ToList();

            var breakdown = Assert.Single(result);
            Assert.Equal("Misato", breakdown.EmployeeName);
            Assert.Equal(4866.17m, breakdown.TotalCost);
            Assert.True(breakdown.IsPaid);
        }

        [Fact]
        public async Task GetPayrollAsync_WithoutData_ShouldReturnEmpty()
        {
            var (sut, _) = BuildSut();

            var result = await sut.GetPayrollAsync(Guid.NewGuid(), 2026, 7);

            Assert.Empty(result);
        }

        [Fact]
        public async Task GetSettingsAsync_WithoutStoredSettings_ShouldReturnBrazilianDefaults()
        {
            var enterpriseId = Guid.NewGuid();
            var (sut, _) = BuildSut();

            var settings = await sut.GetSettingsAsync(enterpriseId);

            Assert.Equal(0.20m, settings.InssRate);
            Assert.Equal(0.02m, settings.RatRate);
            Assert.Equal(0.058m, settings.ThirdPartyRate);
            Assert.Equal(0.08m, settings.FgtsRate);
            Assert.Equal(5, settings.PayrollDueDay);
            Assert.Equal(enterpriseId, settings.EnterpriseId);
        }

        [Fact]
        public async Task UpsertSettingsAsync_WithoutExisting_ShouldCreate()
        {
            var enterpriseId = Guid.NewGuid();
            var (sut, stores) = BuildSut();

            var updated = await sut.UpsertSettingsAsync(enterpriseId, new EnterprisePayrollSettings
            {
                InssRate = 0m,
                RatRate = 0m,
                ThirdPartyRate = 0m,
                FgtsRate = 0.08m,
                PayrollDueDay = 7
            });

            Assert.Equal(enterpriseId, updated.EnterpriseId);
            Assert.Equal(0.08m, updated.FgtsRate);
            Assert.Equal(7, updated.PayrollDueDay);
            stores.settingsRepo.Verify(x => x.CreateAsync(It.IsAny<EnterprisePayrollSettings>()), Times.Once);
            stores.settingsRepo.Verify(x => x.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task UpsertSettingsAsync_WithExisting_ShouldUpdateInPlace()
        {
            var enterpriseId = Guid.NewGuid();
            var stored = new EnterprisePayrollSettings
            {
                Id = Guid.NewGuid(),
                EnterpriseId = enterpriseId,
                IsActive = true
            };

            var (sut, stores) = BuildSut(settings: [stored]);

            var updated = await sut.UpsertSettingsAsync(enterpriseId, new EnterprisePayrollSettings
            {
                InssRate = 0.20m,
                RatRate = 0.03m,
                ThirdPartyRate = 0.058m,
                FgtsRate = 0.08m,
                PayrollDueDay = 5
            });

            Assert.Equal(stored.Id, updated.Id);
            Assert.Equal(0.03m, updated.RatRate);
            stores.settingsRepo.Verify(x => x.CreateAsync(It.IsAny<EnterprisePayrollSettings>()), Times.Never);
            stores.settingsRepo.Verify(x => x.Update(It.IsAny<EnterprisePayrollSettings>()), Times.Once);
            stores.settingsRepo.Verify(x => x.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
        }

        [Theory]
        [InlineData(-0.1, 0.02, 0.058, 0.08, 5)]
        [InlineData(1.5, 0.02, 0.058, 0.08, 5)]
        [InlineData(0.2, 0.02, 0.058, 0.08, 0)]
        [InlineData(0.2, 0.02, 0.058, 0.08, 31)]
        public async Task UpsertSettingsAsync_WithInvalidValues_ShouldThrow(
            double inss, double rat, double thirdParty, double fgts, int dueDay)
        {
            var (sut, _) = BuildSut();

            await Assert.ThrowsAsync<InsertDatabaseException>(
                () => sut.UpsertSettingsAsync(Guid.NewGuid(), new EnterprisePayrollSettings
                {
                    InssRate = (decimal)inss,
                    RatRate = (decimal)rat,
                    ThirdPartyRate = (decimal)thirdParty,
                    FgtsRate = (decimal)fgts,
                    PayrollDueDay = dueDay
                }));
        }

        #region Tenant

        [Fact]
        public async Task GeneratePayrollAsync_WritesTheTenantOnTheDetail()
        {
            var enterpriseId = Guid.NewGuid();
            var employee = BuildEmployee(enterpriseId, 3000m);

            var (sut, stores) = BuildSut(employees: [employee]);

            await sut.GeneratePayrollAsync(enterpriseId, 2026, 7);

            var detail = Assert.Single(stores.details);
            Assert.Equal(enterpriseId, detail.EnterpriseId);
        }

        [Fact]
        public async Task GetPayrollAsync_SkipsADetailBelongingToAnotherTenant()
        {
            var enterpriseId = Guid.NewGuid();
            var employee = BuildEmployee(enterpriseId, 3000m);

            var bill = new PayableBill
            {
                Id = Guid.NewGuid(),
                EnterpriseId = enterpriseId,
                BillType = (int)EnumPayableBillType.Payroll,
                DueDate = new DateTime(2026, 8, 5, 0, 0, 0, DateTimeKind.Utc),
                Amount = 4866.17m,
                IsActive = true
            };

            var foreignDetail = new PayrollBillDetail
            {
                Id = Guid.NewGuid(),
                EnterpriseId = Guid.NewGuid(),
                EmployeeId = employee.Id,
                PayableBillId = bill.Id,
                ReferenceYear = 2026,
                ReferenceMonth = 7,
                IsActive = true
            };

            var (sut, _) = BuildSut(
                employees: [employee],
                payableBills: [bill],
                details: [foreignDetail]);

            Assert.Empty(await sut.GetPayrollAsync(enterpriseId, 2026, 7));
        }

        #endregion

        #region Absences

        /// <summary>
        /// The whole point of M10: before absences existed this employee was paid a full month no
        /// matter how many days they missed.
        /// </summary>
        [Fact]
        public async Task GeneratePayrollAsync_WithUnjustifiedFalta_ShouldReduceTheAmount()
        {
            var enterpriseId = Guid.NewGuid();
            var employee = BuildEmployee(enterpriseId, 3000m);
            var falta = BuildAbsence(employee, EnumEmployeeAbsenceType.Falta, 6, 10, isJustified: false);

            var (sut, stores) = BuildSut(employees: [employee], absences: [falta]);

            var breakdown = Assert.Single(await sut.GeneratePayrollAsync(enterpriseId, 2026, 7));

            Assert.Equal(2516.13m, breakdown.GrossSalary);
            Assert.Equal(4081.31m, breakdown.TotalCost);
            Assert.True(breakdown.TotalCost < 4866.17m);

            var bill = Assert.Single(stores.createdBills);
            Assert.Equal(4081.31m, bill.Amount);
        }

        /// <summary>
        /// Lei 605/49 art. 6 §1: a valid atestado is a falta justificada and the day is still owed.
        /// </summary>
        [Fact]
        public async Task GeneratePayrollAsync_WithJustifiedAtestado_ShouldNotReduceTheAmount()
        {
            var enterpriseId = Guid.NewGuid();
            var employee = BuildEmployee(enterpriseId, 3000m);
            var atestado = BuildAbsence(employee, EnumEmployeeAbsenceType.Atestado, 6, 8, isJustified: true);

            var (sut, _) = BuildSut(employees: [employee], absences: [atestado]);

            var breakdown = Assert.Single(await sut.GeneratePayrollAsync(enterpriseId, 2026, 7));

            Assert.Equal(3000m, breakdown.GrossSalary);
            Assert.Equal(4866.17m, breakdown.TotalCost);
        }

        /// <summary>
        /// The other half of the same rule: the type does not decide, the stored IsJustified does —
        /// so an atestado HR rejected costs the days like any other falta.
        /// </summary>
        [Fact]
        public async Task GeneratePayrollAsync_WithRejectedAtestado_ShouldReduceTheAmount()
        {
            var enterpriseId = Guid.NewGuid();
            var employee = BuildEmployee(enterpriseId, 3000m);
            var atestado = BuildAbsence(employee, EnumEmployeeAbsenceType.Atestado, 6, 8, isJustified: false);

            var (sut, _) = BuildSut(employees: [employee], absences: [atestado]);

            var breakdown = Assert.Single(await sut.GeneratePayrollAsync(enterpriseId, 2026, 7));

            Assert.Equal(2709.68m, breakdown.GrossSalary);
            Assert.Equal(4395.26m, breakdown.TotalCost);
        }

        /// <summary>
        /// An advertência is a disciplinary record, not unworked time — even one stored unjustified
        /// and spanning days must not touch pay. The falta behind it, if any, is its own row.
        /// </summary>
        [Fact]
        public async Task GeneratePayrollAsync_WithAdvertencia_ShouldNotReduceTheAmount()
        {
            var enterpriseId = Guid.NewGuid();
            var employee = BuildEmployee(enterpriseId, 3000m);
            var advertencia = BuildAbsence(employee, EnumEmployeeAbsenceType.Advertencia, 6, 10, isJustified: false);

            var (sut, _) = BuildSut(employees: [employee], absences: [advertencia]);

            var breakdown = Assert.Single(await sut.GeneratePayrollAsync(enterpriseId, 2026, 7));

            Assert.Equal(3000m, breakdown.GrossSalary);
            Assert.Equal(4866.17m, breakdown.TotalCost);
        }

        /// <summary>
        /// The double-payment guard. Férias days are paid separately and up front by
        /// <c>VacationPayService</c>, so the competência must stop paying ordinary salary for them —
        /// see the blockquote in docs/employee/SPEC-hr-module.md §4 P5.
        /// </summary>
        [Fact]
        public async Task GeneratePayrollAsync_WithScheduledFerias_ShouldNotPayThoseDaysAgain()
        {
            var enterpriseId = Guid.NewGuid();
            var employee = BuildEmployee(enterpriseId, 3000m);

            var ferias = BuildAbsence(employee, EnumEmployeeAbsenceType.Vacation, 6, 15, isJustified: true);

            var (sut, _) = BuildSut(employees: [employee], absences: [ferias]);

            var breakdown = Assert.Single(await sut.GeneratePayrollAsync(enterpriseId, 2026, 7));

            Assert.Equal(2032.26m, breakdown.GrossSalary);

            var feriasSalaryPortion = Math.Round(3000m * 10 / 31, 2, MidpointRounding.AwayFromZero);
            Assert.Equal(3000m, breakdown.GrossSalary + feriasSalaryPortion);
        }

        [Fact]
        public async Task GeneratePayrollAsync_WithOverlappingFaltas_ShouldCountEachDayOnce()
        {
            var enterpriseId = Guid.NewGuid();
            var employee = BuildEmployee(enterpriseId, 3000m);
            var first = BuildAbsence(employee, EnumEmployeeAbsenceType.Falta, 6, 10, isJustified: false);
            var second = BuildAbsence(employee, EnumEmployeeAbsenceType.Falta, 8, 12, isJustified: false);

            var (sut, _) = BuildSut(employees: [employee], absences: [first, second]);

            var breakdown = Assert.Single(await sut.GeneratePayrollAsync(enterpriseId, 2026, 7));

            Assert.Equal(2322.58m, breakdown.GrossSalary);
            Assert.Equal(3767.35m, breakdown.TotalCost);
        }

        [Fact]
        public async Task GeneratePayrollAsync_WithFaltaSpanningTheMonthBoundary_ShouldOnlyCountItsDaysInTheMonth()
        {
            var enterpriseId = Guid.NewGuid();
            var employee = BuildEmployee(enterpriseId, 3000m);
            var falta = new EmployeeAbsence
            {
                Id = Guid.NewGuid(),
                EmployeeId = employee.Id,
                EnterpriseId = enterpriseId,
                Type = (int)EnumEmployeeAbsenceType.Falta,
                StartDate = new DateTime(2026, 6, 28, 0, 0, 0, DateTimeKind.Utc),
                EndDate = new DateTime(2026, 7, 3, 0, 0, 0, DateTimeKind.Utc),
                IsJustified = false,
                IsActive = true
            };

            var (sut, _) = BuildSut(employees: [employee], absences: [falta]);

            var breakdown = Assert.Single(await sut.GeneratePayrollAsync(enterpriseId, 2026, 7));

            Assert.Equal(2709.68m, breakdown.GrossSalary);
            Assert.Equal(4395.26m, breakdown.TotalCost);
        }

        [Fact]
        public async Task GeneratePayrollAsync_WithFaltaBeforeTheHireDate_ShouldNotReduceTheProratedDays()
        {
            var enterpriseId = Guid.NewGuid();
            var employee = BuildEmployee(enterpriseId, 3000m);
            employee.HireDate = new DateTime(2026, 7, 15, 0, 0, 0, DateTimeKind.Utc);
            var falta = BuildAbsence(employee, EnumEmployeeAbsenceType.Falta, 1, 10, isJustified: false);

            var (sut, _) = BuildSut(employees: [employee], absences: [falta]);

            var breakdown = Assert.Single(await sut.GeneratePayrollAsync(enterpriseId, 2026, 7));

            Assert.Equal(1645.16m, breakdown.GrossSalary);
            Assert.Equal(2668.54m, breakdown.TotalCost);
        }

        [Fact]
        public async Task GeneratePayrollAsync_WithAnotherEmployeesFalta_ShouldNotReduceThisOne()
        {
            var enterpriseId = Guid.NewGuid();
            var paid = BuildEmployee(enterpriseId, 3000m, "Paid");
            var absent = BuildEmployee(enterpriseId, 3000m, "Absent");
            var falta = BuildAbsence(absent, EnumEmployeeAbsenceType.Falta, 6, 10, isJustified: false);

            var (sut, _) = BuildSut(employees: [paid, absent], absences: [falta]);

            var results = (await sut.GeneratePayrollAsync(enterpriseId, 2026, 7)).ToList();

            Assert.Equal(2516.13m, results.Single(x => x.EmployeeName == "Absent").GrossSalary);
            Assert.Equal(3000m, results.Single(x => x.EmployeeName == "Paid").GrossSalary);
        }

        [Fact]
        public async Task GeneratePayrollAsync_WithSoftDeletedFalta_ShouldNotReduceTheAmount()
        {
            var enterpriseId = Guid.NewGuid();
            var employee = BuildEmployee(enterpriseId, 3000m);
            var falta = BuildAbsence(employee, EnumEmployeeAbsenceType.Falta, 6, 10, isJustified: false);
            falta.IsActive = false;

            var (sut, _) = BuildSut(employees: [employee], absences: [falta]);

            var breakdown = Assert.Single(await sut.GeneratePayrollAsync(enterpriseId, 2026, 7));

            Assert.Equal(3000m, breakdown.GrossSalary);
        }

        private static EmployeeAbsence BuildAbsence(
            Employee employee,
            EnumEmployeeAbsenceType type,
            int startDay,
            int endDay,
            bool isJustified)
        {
            return new EmployeeAbsence
            {
                Id = Guid.NewGuid(),
                EmployeeId = employee.Id,
                EnterpriseId = employee.EnterpriseId,
                Type = (int)type,
                StartDate = new DateTime(2026, 7, startDay, 0, 0, 0, DateTimeKind.Utc),
                EndDate = new DateTime(2026, 7, endDay, 0, 0, 0, DateTimeKind.Utc),
                IsJustified = isJustified,
                IsActive = true
            };
        }

        #endregion

        private static Employee BuildEmployee(Guid enterpriseId, decimal salary, string name = "Employee")
        {
            return new Employee
            {
                Id = Guid.NewGuid(),
                Name = name,
                EnterpriseId = enterpriseId,
                Salary = salary,
                HireDate = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                IsActive = true
            };
        }

        private static (
            PayrollService sut,
            (Mock<IRepository<EnterprisePayrollSettings>> settingsRepo,
             List<PayableBill> createdBills,
             List<PayrollBillDetail> details,
             Mock<IPayableBillService> payableBillService) stores)
            BuildSut(
                List<Employee>? employees = null,
                List<EmployeeAbsence>? absences = null,
                List<EnterprisePayrollSettings>? settings = null,
                List<PayableBill>? payableBills = null,
                List<PayrollBillDetail>? details = null,
                bool employeeRepositoryThrowsWhenEmpty = false,
                Func<PayableBill, bool>? failBillCreation = null)
        {
            var detailStore = details ?? [];
            var payableBillStore = payableBills ?? [];
            var settingsRepo = CreateRepositoryMock(settings ?? []);
            var employeeRepo = CreateRepositoryMock(employees ?? [], employeeRepositoryThrowsWhenEmpty);
            var absenceRepo = CreateRepositoryMock(absences ?? [], throwsWhenEmpty: true);
            var payableBillRepo = CreateRepositoryMock(payableBillStore);
            var detailRepo = CreateRepositoryMock(detailStore);

            var createdBills = new List<PayableBill>();
            var payableBillService = new Mock<IPayableBillService>();
            payableBillService
                .Setup(x => x.CreateAsync(It.IsAny<PayableBill>(), It.IsAny<bool>(), It.IsAny<bool>()))
                .ReturnsAsync((PayableBill bill, bool _, bool __) =>
                {
                    if (failBillCreation?.Invoke(bill) == true)
                        throw new InsertDatabaseException("Unexpected error while creating payable bill.");

                    bill.Id = Guid.NewGuid();
                    bill.IsActive = true;
                    createdBills.Add(bill);
                    payableBillStore.Add(bill);
                    return bill;
                });

            detailRepo
                .Setup(x => x.ExecuteInTransactionAsync(It.IsAny<Func<Task>>(), It.IsAny<CancellationToken>()))
                .Returns(async (Func<Task> operation, CancellationToken _) =>
                {
                    var detailSnapshot = detailStore.ToList();
                    var billSnapshot = payableBillStore.ToList();
                    var createdSnapshot = createdBills.ToList();

                    try
                    {
                        await operation();
                    }
                    catch
                    {
                        detailStore.Clear();
                        detailStore.AddRange(detailSnapshot);
                        payableBillStore.Clear();
                        payableBillStore.AddRange(billSnapshot);
                        createdBills.Clear();
                        createdBills.AddRange(createdSnapshot);
                        throw;
                    }
                });

            var sut = new PayrollService(
                settingsRepo.Object,
                employeeRepo.Object,
                absenceRepo.Object,
                payableBillRepo.Object,
                detailRepo.Object,
                payableBillService.Object);

            return (sut, (settingsRepo, createdBills, detailStore, payableBillService));
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

            repo.Setup(x => x.CreateAsync(It.IsAny<TEntity>()))
                .ReturnsAsync((TEntity entity) =>
                {
                    records.Add(entity);
                    return entity;
                });

            repo.Setup(x => x.Update(It.IsAny<TEntity>()))
                .Returns((TEntity entity) => entity);

            repo.Setup(x => x.CommitAsync(It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            repo.Setup(x => x.ExecuteInTransactionAsync(It.IsAny<Func<Task>>(), It.IsAny<CancellationToken>()))
                .Returns((Func<Task> operation, CancellationToken _) => operation());

            return repo;
        }
    }
}
