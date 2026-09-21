using AutoMapper;
using EvangelionERPV2.BillsModule.Application.Interface;
using EvangelionERPV2.OrderModule.Application.Interface;
using EvangelionERPV2.Shared.DTOs;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Enums;
using EvangelionERPV2.Shared.Exceptions;
using EvangelionERPV2.Shared.Repositories;
using EvangelionERPV2.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Serilog;
using System.Security.Claims;

namespace EvangelionERPV2.Web.Controllers
{
    [Authorize]
    [Route("api/v{version:apiVersion}/[controller]/[action]")]
    [ApiVersion("1.0")]
    public class BillsController : Controller
    {
        private const int MaxStatementWindowInDays = 366;

        private readonly IBillsService<Bill> _billService;
        private readonly IOrderService<Order> _orderService;
        private readonly IReceivablesReportService _receivablesReportService;
        private readonly IAccountingExportService _accountingExportService;
        private readonly IRepository<User> _userRepository;
        private readonly IMapper _mapper;

        public BillsController(
            IBillsService<Bill> billService,
            IOrderService<Order> orderService,
            IReceivablesReportService receivablesReportService,
            IAccountingExportService accountingExportService,
            IRepository<User> userRepository,
            IMapper mapper)
        {
            _billService = billService;
            _orderService = orderService;
            _receivablesReportService = receivablesReportService;
            _accountingExportService = accountingExportService;
            _userRepository = userRepository;
            _mapper = mapper;
        }

        /// <summary>
        /// The carteira split by how late it is, plus the same split per customer.
        /// </summary>
        [Authorize(Policy = "rbac:" + RbacPermissions.Bills.Read)]
        [HttpGet]
        [ProducesResponseType(typeof(ReceivablesAgingDTO), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        public async Task<IActionResult> Aging()
        {
            if (!TryGetEnterpriseId(out var enterpriseId))
                return Unauthorized();

            return Ok(await _receivablesReportService.GetAgingAsync(enterpriseId, HttpContext.RequestAborted));
        }

        /// <summary>Who to chase, worst first.</summary>
        [Authorize(Policy = "rbac:" + RbacPermissions.Bills.Read)]
        [HttpGet]
        [ProducesResponseType(typeof(IReadOnlyList<DelinquentCustomerDTO>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        public async Task<IActionResult> Delinquent(int minimumDaysOverdue = 1)
        {
            if (!TryGetEnterpriseId(out var enterpriseId))
                return Unauthorized();

            return Ok(await _receivablesReportService.GetDelinquentAsync(
                enterpriseId, minimumDaysOverdue, HttpContext.RequestAborted));
        }

        /// <summary>
        /// Extrato: one customer's orders, payments and refunds over a window, with a running balance.
        /// </summary>
        [Authorize(Policy = "rbac:" + RbacPermissions.Bills.Read)]
        [HttpGet("{customerId}")]
        [ProducesResponseType(typeof(CustomerStatementDTO), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> Statement(Guid customerId, DateTime? from = null, DateTime? to = null)
        {
            if (!TryGetEnterpriseId(out var enterpriseId))
                return Unauthorized();

            var toUtc = (to ?? DateTime.UtcNow).Date;
            var fromUtc = (from ?? toUtc.AddDays(-90)).Date;

            if ((toUtc - fromUtc).TotalDays > MaxStatementWindowInDays)
                return BadRequest("Statement window is too wide.");

            var statement = await _receivablesReportService.GetStatementAsync(
                enterpriseId, customerId, fromUtc, toUtc, HttpContext.RequestAborted);

            return statement == null ? NotFound() : Ok(statement);
        }

        [Authorize(Policy = "rbac:" + RbacPermissions.Bills.Read)]
        [HttpGet("{orderId}")]
        [ProducesResponseType(typeof(BillDTO), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetByOrder(Guid orderId)
        {
            try
            {
                if (!TryGetEnterpriseId(out var enterpriseId))
                    return Unauthorized();
                var order = await _orderService.GetByIdAsync(orderId, enterpriseId);
                if (order == null)
                    return NoContent();

                var bill = await _billService.GetByOrderIdAsync(orderId, enterpriseId);
                if (bill == null)
                    return NoContent();

                var dto = _mapper.Map<BillDTO>(bill);
                return Ok(dto);
            }
            catch (Exception)
            {
                throw;
            }
        }

        [Authorize(Policy = "rbac:" + RbacPermissions.Bills.Generate)]
        [HttpPost("{orderId}")]
        [ProducesResponseType(typeof(BillDTO), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> Generate(Guid orderId)
        {
            try
            {
                if (!TryGetEnterpriseId(out var enterpriseId))
                    return Unauthorized();
                var order = await _orderService.GetByIdAsync(orderId, enterpriseId);
                if (order == null)
                    return NoContent();

                var bill = await _billService.GenerateAsync(orderId, enterpriseId);
                if (bill == null)
                    return NoContent();

                var dto = _mapper.Map<BillDTO>(bill);
                return Ok(dto);
            }
            catch (NotFoundDatabaseException exnf)
            {
                Log.Logger.Debug("Order not found for bill generation. OrderId={OrderId} ErrorType={ErrorType}", orderId, exnf.GetType().Name);
                return NoContent();
            }
            catch (Exception)
            {
                throw;
            }
        }

        [Authorize(Policy = "rbac:" + RbacPermissions.Bills.DownloadPdf)]
        [HttpGet("{orderId}")]
        [Produces("application/pdf")]
        [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> Pdf(Guid orderId)
        {
            try
            {
                if (!TryGetEnterpriseId(out var enterpriseId))
                    return Unauthorized();

                var order = await _orderService.GetByIdAsync(orderId, enterpriseId);
                if (order == null)
                    return NoContent();

                var pdfBytes = await _billService.GetPdfAsync(orderId, enterpriseId);
                if (pdfBytes == null || pdfBytes.Length == 0)
                    return NoContent();

                return File(pdfBytes, "application/pdf", $"bill-{orderId}.pdf");
            }
            catch (NotFoundDatabaseException exnf)
            {
                Log.Logger.Debug("Order not found for bill PDF generation. OrderId={OrderId} ErrorType={ErrorType}", orderId, exnf.GetType().Name);
                return NoContent();
            }
            catch (Exception)
            {
                throw;
            }
        }

        /// <summary>
        /// Every boleto ever issued for the order, superseded ones included — the only way to find
        /// the id of a title a payer is still holding after a reissue.
        /// </summary>
        [Authorize(Policy = "rbac:" + RbacPermissions.Bills.Read)]
        [HttpGet("{orderId}")]
        [ProducesResponseType(typeof(IEnumerable<BillDTO>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> History(Guid orderId)
        {
            if (!TryGetEnterpriseId(out var enterpriseId))
                return Unauthorized();

            var bills = await _billService.GetHistoryAsync(orderId, enterpriseId);
            if (bills.Count == 0)
                return NoContent();

            return Ok(_mapper.Map<IEnumerable<BillDTO>>(bills));
        }

        /// <summary>
        /// Marks one boleto settled. By bill id, not order id: after a reissue the payer may settle
        /// the superseded title, and that payment still has to land on the right row.
        /// </summary>
        [Authorize(Policy = "rbac:" + RbacPermissions.Bills.Settle)]
        [HttpPost("{billId}")]
        [ProducesResponseType(typeof(BillDTO), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> Settle(Guid billId, [FromBody] SettleBillRequestDTO request)
        {
            if (!TryGetEnterpriseId(out var enterpriseId))
                return Unauthorized();

            try
            {
                var bill = await _billService.SettleAsync(
                    billId, enterpriseId, request.PaidAmount, request.PaidAt, request.SettlementProtocol);

                if (bill == null)
                    return NoContent();

                return Ok(_mapper.Map<BillDTO>(bill));
            }
            catch (InsertDatabaseException exception)
            {
                Log.Logger.Warning("Bill settlement refused. BillId={BillId} ErrorType={ErrorType}", billId, exception.GetType().Name);
                return BadRequest(exception.Message);
            }
        }

        /// <summary>
        /// Cancels the order's live boleto and issues a replacement from the order's current dates.
        /// Fix the order's payment date first — this endpoint deliberately takes no date of its own,
        /// so the boleto and the receivable can never disagree about when the money is due.
        /// </summary>
        [Authorize(Policy = "rbac:" + RbacPermissions.Bills.Reissue)]
        [HttpPost("{orderId}")]
        [ProducesResponseType(typeof(BillDTO), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> Reissue(Guid orderId)
        {
            if (!TryGetEnterpriseId(out var enterpriseId))
                return Unauthorized();

            try
            {
                var bill = await _billService.ReissueAsync(orderId, enterpriseId);
                if (bill == null)
                    return NoContent();

                return Ok(_mapper.Map<BillDTO>(bill));
            }
            catch (InsertDatabaseException exception)
            {
                Log.Logger.Warning("Bill reissue refused. OrderId={OrderId} ErrorType={ErrorType}", orderId, exception.GetType().Name);
                return BadRequest(exception.Message);
            }
            catch (NotFoundDatabaseException exnf)
            {
                Log.Logger.Debug("Order not found for bill reissue. OrderId={OrderId} ErrorType={ErrorType}", orderId, exnf.GetType().Name);
                return NoContent();
            }
        }

        /// <summary>
        /// One month of movement as a zip of four CSVs, ready to hand to the accountant.
        /// </summary>
        [Authorize(Policy = "rbac:" + RbacPermissions.Accounting.Export)]
        [HttpGet]
        [Produces("application/zip")]
        [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        public async Task<IActionResult> AccountingPackage(int year, int month)
        {
            if (!TryGetEnterpriseId(out var enterpriseId))
                return Unauthorized();

            try
            {
                var package = await _accountingExportService.BuildMonthlyAsync(
                    enterpriseId, year, month, HttpContext.RequestAborted);

                return File(package, "application/zip", $"contabilidade-{year:0000}-{month:00}.zip");
            }
            catch (ArgumentException ex)
            {
                Log.Logger.Warning(
                    "Invalid accounting export period. Year={Year} Month={Month} ErrorType={ErrorType}",
                    year, month, ex.GetType().Name);

                return BadRequest("Invalid period.");
            }
        }

        private bool TryGetEnterpriseId(out Guid enterpriseId)
        {
            var claimValue = User.FindFirst(ClaimTypes.GroupSid)?.Value;
            return Guid.TryParse(claimValue, out enterpriseId) && enterpriseId != Guid.Empty;
        }
    }
}
