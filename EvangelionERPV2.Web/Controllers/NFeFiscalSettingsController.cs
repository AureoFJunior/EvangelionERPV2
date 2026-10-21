using Asp.Versioning;
using System.Security.Claims;
using AutoMapper;
using EvangelionERPV2.NFeModule.Application.Interface;
using EvangelionERPV2.NFeModule.Infra.Certificates;
using EvangelionERPV2.Shared.DTOs;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Exceptions;
using EvangelionERPV2.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Serilog;

namespace EvangelionERPV2.Web.Controllers
{
    /// <summary>
    /// Per-tenant fiscal profile. Admin-only — see RbacPermissions.NFe.SettingsRead/SettingsUpdate,
    /// which are absent from every role array on purpose.
    /// </summary>
    [Authorize]
    [Route("api/v{version:apiVersion}/[controller]/[action]")]
    [ApiVersion("1.0")]
    public class NFeFiscalSettingsController : Controller
    {
        private const int MaxFiscalSettingsRequestBodySizeInBytes = 64 * 1024;

        private const int MaxCertificateUploadSizeInBytes = 256 * 1024;

        private readonly INFeFiscalSettingsService _fiscalSettingsService;
        private readonly INFeCertificateService _certificateService;
        private readonly IEnterpriseCscProvider _cscProvider;
        private readonly IMapper _mapper;

        public NFeFiscalSettingsController(
            INFeFiscalSettingsService fiscalSettingsService,
            INFeCertificateService certificateService,
            IEnterpriseCscProvider cscProvider,
            IMapper mapper)
        {
            _fiscalSettingsService = fiscalSettingsService;
            _certificateService = certificateService;
            _cscProvider = cscProvider;
            _mapper = mapper;
        }

        /// <summary>
        /// Sets the NFC-e CSC and its id. Write-only.
        /// </summary>
        [Authorize(Policy = RbacPolicies.NFe.CertificateManage)]
        [HttpPut]
        [RequestSizeLimit(MaxFiscalSettingsRequestBodySizeInBytes)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        public async Task<IActionResult> UpdateCsc([FromBody] UpdateNFCeCscRequestDTO request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Csc))
                return BadRequest("A CSC is required.");

            if (request.CscId <= 0)
                return BadRequest("A CSC id is required.");

            if (!TryGetEnterpriseId(out var enterpriseId))
                return Unauthorized();

            try
            {
                await _cscProvider.StoreAsync(enterpriseId, request.Csc, HttpContext.RequestAborted);

                var settings = await _fiscalSettingsService.GetSettingsAsync(enterpriseId);
                settings.CscId = request.CscId;
                await _fiscalSettingsService.UpsertSettingsAsync(enterpriseId, settings);

                return NoContent();
            }
            catch (InvalidOperationException ex)
            {
                Log.Logger.Error("NFCe CSC update rejected. EnterpriseId={EnterpriseId}", enterpriseId);
                return BadRequest(ex.Message);
            }
        }

        [Authorize(Policy = RbacPolicies.NFe.SettingsRead)]
        [HttpGet]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        public async Task<IActionResult> GetFiscalSettings()
        {
            if (!TryGetEnterpriseId(out var enterpriseId))
                return Unauthorized();

            var settings = await _fiscalSettingsService.GetSettingsAsync(enterpriseId);
            return Ok(_mapper.Map<EnterpriseFiscalSettingsDTO>(settings));
        }

        [Authorize(Policy = RbacPolicies.NFe.SettingsUpdate)]
        [HttpPut]
        [RequestSizeLimit(MaxFiscalSettingsRequestBodySizeInBytes)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        public async Task<IActionResult> UpdateFiscalSettings([FromBody] EnterpriseFiscalSettingsDTO request)
        {
            try
            {
                if (!ModelState.IsValid || request == null)
                    return BadRequest(ControllerResponseSanitizer.InvalidRequestPayloadMessage);

                if (!TryGetEnterpriseId(out var enterpriseId))
                    return Unauthorized();

                var settings = _mapper.Map<EnterpriseFiscalSettings>(request);
                var updated = await _fiscalSettingsService.UpsertSettingsAsync(enterpriseId, settings);
                return Ok(_mapper.Map<EnterpriseFiscalSettingsDTO>(updated));
            }
            catch (InsertDatabaseException ex)
            {
                Log.Logger.Error("Invalid fiscal settings payload. ErrorType={ErrorType}", ex.GetType().Name);
                return BadRequest(GetSafeInsertErrorMessage(ex));
            }
        }

        [Authorize(Policy = RbacPolicies.NFe.CertificateManage)]
        [HttpPost]
        [RequestSizeLimit(MaxCertificateUploadSizeInBytes)]
        [ProducesResponseType(typeof(NFeCertificateStatusDTO), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        public async Task<IActionResult> UploadCertificate(
            [FromForm] UploadNFeCertificateRequestDTO request)
        {
            try
            {
                var file = request?.File;

                if (file == null || file.Length == 0)
                    return BadRequest("Certificate file is required.");

                if (file.Length > MaxCertificateUploadSizeInBytes)
                    return BadRequest("Certificate file is too large.");

                if (!TryGetEnterpriseId(out var enterpriseId))
                    return Unauthorized();

                using var buffer = new MemoryStream();
                await file.CopyToAsync(buffer);

                var status = await _certificateService.UploadAsync(
                    enterpriseId, buffer.ToArray(), request?.Password ?? string.Empty, HttpContext.RequestAborted);

                return Ok(status);
            }
            catch (NFeCertificateException ex)
            {
                Log.Logger.Error("Certificate upload rejected. ErrorType={ErrorType}", ex.GetType().Name);
                return BadRequest(ex.Message);
            }
            catch (InsertDatabaseException ex)
            {
                Log.Logger.Error("Invalid certificate upload. ErrorType={ErrorType}", ex.GetType().Name);
                return BadRequest(GetSafeInsertErrorMessage(ex));
            }
        }

        [Authorize(Policy = RbacPolicies.NFe.CertificateRead)]
        [HttpGet]
        [ProducesResponseType(typeof(NFeCertificateStatusDTO), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        public async Task<IActionResult> GetCertificateStatus()
        {
            if (!TryGetEnterpriseId(out var enterpriseId))
                return Unauthorized();

            var status = await _certificateService.GetStatusAsync(enterpriseId, HttpContext.RequestAborted);
            return Ok(status);
        }

        [Authorize(Policy = RbacPolicies.NFe.CertificateManage)]
        [HttpDelete]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        public async Task<IActionResult> DeleteCertificate()
        {
            if (!TryGetEnterpriseId(out var enterpriseId))
                return Unauthorized();

            await _certificateService.DeleteAsync(enterpriseId, HttpContext.RequestAborted);
            return NoContent();
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
