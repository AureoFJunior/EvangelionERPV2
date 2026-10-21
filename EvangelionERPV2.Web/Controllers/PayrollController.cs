using Asp.Versioning;
using System.Security.Claims;
using AutoMapper;
using EvangelionERPV2.BillsModule.Application.Interface;
using EvangelionERPV2.Shared.DTOs;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Exceptions;
using EvangelionERPV2.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Serilog;

namespace EvangelionERPV2.Web.Controllers
{
    [Authorize]
    [Route("api/v{version:apiVersion}/[controller]/[action]")]
    [ApiVersion("1.0")]
    public class PayrollController : Controller
    {
        private const int MaxPayrollWriteRequestBodySizeInBytes = 64 * 1024;

        private readonly IPayrollService _payrollService;
        private readonly IVacationPayService _vacationPayService;
        private readonly IMapper _mapper;

        public PayrollController(
            IPayrollService payrollService,
            IVacationPayService vacationPayService,
            IMapper mapper)
        {
            _payrollService = payrollService;
            _vacationPayService = vacationPayService;
            _mapper = mapper;
        }

        [Authorize(Policy = "rbac:" + RbacPermissions.Payroll.Read)]
        [HttpGet]
        [ProducesResponseType(typeof(EnterprisePayrollSettingsDTO), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetPayrollSettings()
        {
            try
            {
                if (!TryGetEnterpriseId(out var enterpriseId))
                    return Unauthorized();

                var settings = await _payrollService.GetSettingsAsync(enterpriseId);
                return Ok(_mapper.Map<EnterprisePayrollSettingsDTO>(settings));
            }
            catch (Exception)
            {
                throw;
            }
        }

        [Authorize(Policy = "rbac:" + RbacPermissions.Payroll.UpdateSettings)]
        [HttpPut]
        [RequestSizeLimit(MaxPayrollWriteRequestBodySizeInBytes)]
        public async Task<IActionResult> UpdatePayrollSettings([FromBody] EnterprisePayrollSettingsDTO request)
        {
            try
            {
                if (!ModelState.IsValid || request == null)
                    return BadRequest(ControllerResponseSanitizer.InvalidRequestPayloadMessage);

                if (!TryGetEnterpriseId(out var enterpriseId))
                    return Unauthorized();

                var settings = _mapper.Map<EnterprisePayrollSettings>(request);
                var updated = await _payrollService.UpsertSettingsAsync(enterpriseId, settings);
                return Ok(_mapper.Map<EnterprisePayrollSettingsDTO>(updated));
            }
            catch (InsertDatabaseException ex)
            {
                Log.Logger.Error("Invalid payroll settings payload. ErrorType={ErrorType}", ex.GetType().Name);
                return BadRequest(GetSafeInsertErrorMessage(ex));
            }
            catch (Exception)
            {
                throw;
            }
        }

        [Authorize(Policy = "rbac:" + RbacPermissions.Payroll.Generate)]
        [HttpPost]
        public async Task<IActionResult> GeneratePayroll(
            [FromQuery] int? year = null,
            [FromQuery] int? month = null,
            [FromQuery] Guid? employeeId = null)
        {
            try
            {
                if (!TryGetEnterpriseId(out var enterpriseId))
                    return Unauthorized();

                var today = DateTime.UtcNow;
                var breakdown = await _payrollService.GeneratePayrollAsync(
                    enterpriseId,
                    year ?? today.Year,
                    month ?? today.Month,
                    employeeId);

                return Ok(breakdown);
            }
            catch (InsertDatabaseException ex)
            {
                Log.Logger.Error("Invalid payroll generation. ErrorType={ErrorType}", ex.GetType().Name);
                return BadRequest(GetSafeInsertErrorMessage(ex));
            }
            catch (Exception)
            {
                throw;
            }
        }

        [Authorize(Policy = "rbac:" + RbacPermissions.Payroll.Read)]
        [HttpGet]
        public async Task<IActionResult> GetPayroll([FromQuery] int? year = null, [FromQuery] int? month = null)
        {
            try
            {
                if (!TryGetEnterpriseId(out var enterpriseId))
                    return Unauthorized();

                var today = DateTime.UtcNow;
                var breakdown = await _payrollService.GetPayrollAsync(
                    enterpriseId,
                    year ?? today.Year,
                    month ?? today.Month);

                return Ok(breakdown);
            }
            catch (InsertDatabaseException ex)
            {
                Log.Logger.Error("Invalid payroll query. ErrorType={ErrorType}", ex.GetType().Name);
                return BadRequest(GetSafeInsertErrorMessage(ex));
            }
            catch (Exception)
            {
                throw;
            }
        }

        [Authorize(Policy = "rbac:" + RbacPermissions.Payroll.Generate)]
        [HttpPost]
        [ProducesResponseType(typeof(IEnumerable<VacationPayDTO>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GenerateVacationPay([FromQuery] Guid accrualId)
        {
            try
            {
                if (!TryGetEnterpriseId(out var enterpriseId))
                    return Unauthorized();

                var payments = await _vacationPayService.GenerateAsync(enterpriseId, accrualId);
                return Ok(payments);
            }
            catch (InsertDatabaseException ex)
            {
                Log.Logger.Error("Invalid vacation pay generation. ErrorType={ErrorType}", ex.GetType().Name);
                return BadRequest(GetSafeInsertErrorMessage(ex));
            }
            catch (Exception)
            {
                throw;
            }
        }

        [Authorize(Policy = "rbac:" + RbacPermissions.Payroll.Read)]
        [HttpGet]
        [ProducesResponseType(typeof(IEnumerable<VacationPayDTO>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetVacationPay([FromQuery] Guid accrualId)
        {
            try
            {
                if (!TryGetEnterpriseId(out var enterpriseId))
                    return Unauthorized();

                var payments = await _vacationPayService.GetAsync(enterpriseId, accrualId);
                return Ok(payments);
            }
            catch (InsertDatabaseException ex)
            {
                Log.Logger.Error("Invalid vacation pay query. ErrorType={ErrorType}", ex.GetType().Name);
                return BadRequest(GetSafeInsertErrorMessage(ex));
            }
            catch (Exception)
            {
                throw;
            }
        }

        private bool TryGetEnterpriseId(out Guid enterpriseId)
        {
            var claimValue = User.FindFirst(ClaimTypes.GroupSid)?.Value;
            return Guid.TryParse(claimValue, out enterpriseId) && enterpriseId != Guid.Empty;
        }

        private static string GetSafeInsertErrorMessage(InsertDatabaseException ex)
        {
            if (ex.InnerException != null)
                return "An internal error occurred. Please try again later.";

            var message = ex.Message?.Trim() ?? string.Empty;
            return string.IsNullOrWhiteSpace(message)
                ? "An internal error occurred. Please try again later."
                : message;
        }
    }
}
