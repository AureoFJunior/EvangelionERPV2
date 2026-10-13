using AutoMapper;
using EvangelionERPV2.BillsModule.Application.Interface;
using EvangelionERPV2.Shared.DTOs;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Exceptions;
using EvangelionERPV2.Web.Controllers;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace EvangelionERPV2.Test.Security
{
    /// <summary>
    /// A folha row says what a named colleague earns, so every action here is scoped to the tenant
    /// on the token and none of them accepts an enterprise id from the caller.
    /// </summary>
    public class PayrollControllerTests
    {
        private readonly Guid _enterpriseId = Guid.NewGuid();
        private readonly Mock<IPayrollService> _payroll = new();
        private readonly Mock<IVacationPayService> _vacationPay = new();

        private PayrollController Controller() =>
            new PayrollController(_payroll.Object, _vacationPay.Object, Mapper()).WithTenant(_enterpriseId);

        private PayrollController Anonymous() =>
            new PayrollController(_payroll.Object, _vacationPay.Object, Mapper()).WithoutTenant();

        private static IMapper Mapper()
        {
            var mapper = new Mock<IMapper>();
            mapper.Setup(m => m.Map<EnterprisePayrollSettingsDTO>(It.IsAny<EnterprisePayrollSettings>()))
                .Returns<EnterprisePayrollSettings>(s => new EnterprisePayrollSettingsDTO
                {
                    InssRate = s.InssRate,
                    PayrollDueDay = s.PayrollDueDay
                });
            mapper.Setup(m => m.Map<EnterprisePayrollSettings>(It.IsAny<EnterprisePayrollSettingsDTO>()))
                .Returns<EnterprisePayrollSettingsDTO>(d => new EnterprisePayrollSettings
                {
                    InssRate = d.InssRate,
                    PayrollDueDay = d.PayrollDueDay
                });
            return mapper.Object;
        }

        [Fact]
        public async Task GetPayrollSettings_WithoutATenantClaim_IsUnauthorized()
        {
            Assert.IsType<UnauthorizedResult>(await Anonymous().GetPayrollSettings());
        }

        [Fact]
        public async Task GetPayrollSettings_AnswersTheTenantsRates()
        {
            _payroll.Setup(p => p.GetSettingsAsync(_enterpriseId))
                .ReturnsAsync(new EnterprisePayrollSettings { InssRate = 0.20m, PayrollDueDay = 5 });

            var result = Assert.IsType<OkObjectResult>(await Controller().GetPayrollSettings());

            Assert.Equal(0.20m, Assert.IsType<EnterprisePayrollSettingsDTO>(result.Value).InssRate);
        }

        /// <summary>
        /// The enterprise comes from the token, never from the body — the DTO carries rates only,
        /// and the controller supplies the tenant it was minted for.
        /// </summary>
        [Fact]
        public async Task UpdatePayrollSettings_SavesAgainstTheTenantOnTheToken()
        {
            Guid? savedFor = null;
            _payroll.Setup(p => p.UpsertSettingsAsync(It.IsAny<Guid>(), It.IsAny<EnterprisePayrollSettings>()))
                .Callback<Guid, EnterprisePayrollSettings>((id, _) => savedFor = id)
                .ReturnsAsync(new EnterprisePayrollSettings { InssRate = 0.20m, PayrollDueDay = 5 });

            Assert.IsType<OkObjectResult>(await Controller().UpdatePayrollSettings(
                new EnterprisePayrollSettingsDTO { InssRate = 0.20m, PayrollDueDay = 5 }));

            Assert.Equal(_enterpriseId, savedFor);
        }

        [Fact]
        public async Task UpdatePayrollSettings_WithoutATenantClaim_IsUnauthorized()
        {
            Assert.IsType<UnauthorizedResult>(
                await Anonymous().UpdatePayrollSettings(new EnterprisePayrollSettingsDTO()));
        }

        [Fact]
        public async Task UpdatePayrollSettings_WhenTheServiceRefuses_DoesNotEchoTheInnerDetail()
        {
            _payroll.Setup(p => p.UpsertSettingsAsync(_enterpriseId, It.IsAny<EnterprisePayrollSettings>()))
                .ThrowsAsync(new InsertDatabaseException(
                    "outer", new Exception("Violation of CHECK constraint 'CK_Payroll_Secret'")));

            var result = Assert.IsType<BadRequestObjectResult>(
                await Controller().UpdatePayrollSettings(new EnterprisePayrollSettingsDTO()));

            Assert.DoesNotContain("CK_Payroll_Secret", result.Value?.ToString());
        }

        /// <summary>
        /// A payroll run with no period named is this month's, resolved from the clock rather than
        /// left to the caller — which is what makes the plain button on the screen work.
        /// </summary>
        [Fact]
        public async Task GeneratePayroll_WithNoPeriod_UsesTheCurrentMonth()
        {
            var today = DateTime.UtcNow;
            _payroll.Setup(p => p.GeneratePayrollAsync(_enterpriseId, today.Year, today.Month, null))
                .ReturnsAsync([]);

            Assert.IsType<OkObjectResult>(await Controller().GeneratePayroll());
            _payroll.VerifyAll();
        }

        [Fact]
        public async Task GeneratePayroll_WithAPeriodAndAnEmployee_ForwardsBoth()
        {
            var employeeId = Guid.NewGuid();
            _payroll.Setup(p => p.GeneratePayrollAsync(_enterpriseId, 2026, 8, employeeId))
                .ReturnsAsync([new PayrollEmployeeBreakdownDTO()]);

            Assert.IsType<OkObjectResult>(await Controller().GeneratePayroll(2026, 8, employeeId));
            _payroll.VerifyAll();
        }

        [Fact]
        public async Task GeneratePayroll_WithoutATenantClaim_IsUnauthorized()
        {
            Assert.IsType<UnauthorizedResult>(await Anonymous().GeneratePayroll());
        }

        [Fact]
        public async Task GeneratePayroll_InAClosedPeriod_AnswersBadRequest()
        {
            _payroll.Setup(p => p.GeneratePayrollAsync(_enterpriseId, It.IsAny<int>(), It.IsAny<int>(), It.IsAny<Guid?>()))
                .ThrowsAsync(new InsertDatabaseException("The period is closed."));

            Assert.IsType<BadRequestObjectResult>(await Controller().GeneratePayroll(2026, 8));
        }

        [Fact]
        public async Task GetPayroll_WithNoPeriod_ReadsTheCurrentMonth()
        {
            var today = DateTime.UtcNow;
            _payroll.Setup(p => p.GetPayrollAsync(_enterpriseId, today.Year, today.Month)).ReturnsAsync([]);

            Assert.IsType<OkObjectResult>(await Controller().GetPayroll());
            _payroll.VerifyAll();
        }

        [Fact]
        public async Task GetPayroll_WithoutATenantClaim_IsUnauthorized()
        {
            Assert.IsType<UnauthorizedResult>(await Anonymous().GetPayroll());
        }

        [Fact]
        public async Task GenerateVacationPay_ScopesTheAccrualToTheCallersTenant()
        {
            var accrualId = Guid.NewGuid();
            _vacationPay.Setup(v => v.GenerateAsync(_enterpriseId, accrualId)).ReturnsAsync([]);

            Assert.IsType<OkObjectResult>(await Controller().GenerateVacationPay(accrualId));
            _vacationPay.VerifyAll();
        }

        /// <summary>
        /// An accrual id belonging to another tenant is refused by the service, and the message
        /// that reaches the caller is the one this codebase wrote.
        /// </summary>
        [Fact]
        public async Task GenerateVacationPay_ForAnotherTenantsAccrual_AnswersBadRequestWithoutDetail()
        {
            var accrualId = Guid.NewGuid();
            _vacationPay.Setup(v => v.GenerateAsync(_enterpriseId, accrualId))
                .ThrowsAsync(new InsertDatabaseException("outer", new Exception("row 91 of EmployeeVacationAccrual")));

            var result = Assert.IsType<BadRequestObjectResult>(await Controller().GenerateVacationPay(accrualId));

            Assert.DoesNotContain("EmployeeVacationAccrual", result.Value?.ToString());
        }

        [Fact]
        public async Task GetVacationPay_ScopesTheAccrualToTheCallersTenant()
        {
            var accrualId = Guid.NewGuid();
            _vacationPay.Setup(v => v.GetAsync(_enterpriseId, accrualId)).ReturnsAsync([]);

            Assert.IsType<OkObjectResult>(await Controller().GetVacationPay(accrualId));
            _vacationPay.VerifyAll();
        }

        [Fact]
        public async Task GetVacationPay_WithoutATenantClaim_IsUnauthorized()
        {
            Assert.IsType<UnauthorizedResult>(await Anonymous().GetVacationPay(Guid.NewGuid()));
        }

        [Fact]
        public async Task GenerateVacationPay_WithoutATenantClaim_IsUnauthorized()
        {
            Assert.IsType<UnauthorizedResult>(await Anonymous().GenerateVacationPay(Guid.NewGuid()));
        }
    }
}
