using Asp.Versioning;
using EvangelionERPV2.StorefrontModule.Application.DTOs;
using EvangelionERPV2.StorefrontModule.Application.Interface;
using EvangelionERPV2.StorefrontModule.Application.Services;
using EvangelionERPV2.UserModule.Application.Token;
using EvangelionERPV2.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Serilog;
using System.Security.Claims;

namespace EvangelionERPV2.Web.Controllers
{
    /// <summary>
    /// What Limen's back end (Storefront.Api) talks to, as one service account — and the two screens
    /// ERP staff use to put their shop on it. SPEC-ecommerce-storefront.md §4.
    /// </summary>
    [Authorize]
    [Route("api/v{version:apiVersion}/[controller]/[action]")]
    [ApiVersion("1.0")]
    public class StorefrontController : Controller
    {
        private const int MaxLoginRequestBodySizeInBytes = 4 * 1024;
        private const int MaxListingRequestBodySizeInBytes = 16 * 1024;
        private const int MaxListingImageRequestBodySizeInBytes = 8 * 1024 * 1024;
        private const int MaxPublishRequestBodySizeInBytes = 32 * 1024;

        private const int MaxEnterprisePageSize = 100;
        private const int MaxProductPageSize = 200;
        private const int MaxQueryLength = 100;

        private static readonly FailedAttemptLimiter _loginLimiter = new(TimeSpan.FromMinutes(15), 5);

        private const int MaxCheckoutRequestBodySizeInBytes = 32 * 1024;

        private readonly IStorefrontService _storefrontService;
        private readonly IStorefrontCheckoutService _checkoutService;
        private readonly IStorefrontCategoryService _categoryService;
        private readonly StorefrontPromotionService _promotionService;
        private readonly PasswordLoginService _passwordLogin;
        private readonly TokenService _tokenService;
        private readonly IConfiguration _configuration;

        public StorefrontController(
            IStorefrontService storefrontService,
            IStorefrontCheckoutService checkoutService,
            IStorefrontCategoryService categoryService,
            StorefrontPromotionService promotionService,
            PasswordLoginService passwordLogin,
            TokenService tokenService,
            IConfiguration configuration)
        {
            _storefrontService = storefrontService;
            _checkoutService = checkoutService;
            _categoryService = categoryService;
            _promotionService = promotionService;
            _passwordLogin = passwordLogin;
            _tokenService = tokenService;
            _configuration = configuration;
        }

        /// <summary>
        /// Logs the storefront's service account in. Only a service account of the platform enterprise
        /// (<c>Storefront:PlatformEnterpriseId</c>) gets a token, and that token reaches nothing but
        /// <c>api/v1/Storefront/*</c>. No refresh token: the caller logs in again when it expires.
        /// </summary>
        [HttpPost]
        [AllowAnonymous]
        [RequestSizeLimit(MaxLoginRequestBodySizeInBytes)]
        [ProducesResponseType(typeof(StorefrontLoginResponseDTO), 200)]
        [ProducesResponseType(400)]
        [ProducesResponseType(401)]
        [ProducesResponseType(429)]
        public async Task<IActionResult> Login([FromBody] StorefrontLoginRequestDTO request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.UserName) || string.IsNullOrWhiteSpace(request.Password))
                return BadRequest("userName and password are required.");

            var key = FailedAttemptLimiter.StableKey(PasswordLoginService.NormalizeUserName(request.UserName).ToLowerInvariant());
            if (_loginLimiter.IsLimited(key))
                return StatusCode(StatusCodes.Status429TooManyRequests);

            var login = await _passwordLogin.AuthenticateAsync(request.UserName, request.Password);
            var user = login.User;

            var accepted = user != null
                && user.IsServiceAccount
                && user.IsActive == true
                && Guid.TryParse(_configuration["Storefront:PlatformEnterpriseId"], out var platformId)
                && user.EnterpriseId == platformId;

            Log.Logger.Information("Storefront service login. Accepted={Accepted}", accepted);

            if (!accepted)
            {
                _loginLimiter.RegisterFailure(key);
                return Unauthorized();
            }

            _loginLimiter.Clear(key);

            return Ok(new StorefrontLoginResponseDTO
            {
                Token = _tokenService.GenerateToken(BuildClaims(user!))
            });
        }

        /// <summary>The claim list <c>TokenService.GenerateToken(User, bool)</c> builds, plus the storefront scope.</summary>
        private static IEnumerable<Claim> BuildClaims(Shared.Entities.User user)
        {
            var userId = user.Id.ToString();

            return
            [
                new Claim(ClaimTypes.Name, userId),
                new Claim(ClaimTypes.GivenName, user.FirstName),
                new Claim(ClaimTypes.Surname, user.LastName),
                new Claim(ClaimTypes.NameIdentifier, userId),
                new Claim(ClaimTypes.Sid, userId),
                new Claim("uid", userId),
                new Claim(ClaimTypes.GroupSid, user.EnterpriseId.ToString()!),
                new Claim(StorefrontTokenScope.ClaimType, StorefrontTokenScope.ClaimValue)
            ];
        }

        #region Catalog (service account)

        [HttpGet("{page:int}/{size:int}")]
        [Authorize(Policy = "rbac:" + RbacPermissions.Storefront.CatalogRead)]
        [ProducesResponseType(typeof(StorefrontPageDTO<StorefrontEnterpriseSummaryDTO>), 200)]
        public async Task<IActionResult> Enterprises(int page, int size) =>
            Ok(await _storefrontService.GetEnterprisesAsync(Math.Max(page, 1), Math.Clamp(size, 1, MaxEnterprisePageSize)));

        [HttpGet("{slug}")]
        [ActionName("Enterprise")]
        [Authorize(Policy = "rbac:" + RbacPermissions.Storefront.CatalogRead)]
        [ProducesResponseType(typeof(StorefrontEnterpriseProfileDTO), 200)]
        [ProducesResponseType(404)]
        public async Task<IActionResult> GetEnterprise(string slug)
        {
            var enterprise = await _storefrontService.GetEnterpriseAsync(slug);
            return enterprise == null ? NotFound() : Ok(enterprise);
        }

        [HttpGet("{page:int}/{size:int}")]
        [Authorize(Policy = "rbac:" + RbacPermissions.Storefront.CatalogRead)]
        [ProducesResponseType(typeof(StorefrontPageDTO<StorefrontProductSummaryDTO>), 200)]
        [ProducesResponseType(400)]
        public async Task<IActionResult> Products(
            int page,
            int size,
            [FromQuery] string? q,
            [FromQuery] string? enterpriseSlug,
            [FromQuery] decimal? minPrice,
            [FromQuery] decimal? maxPrice,
            [FromQuery] bool available = false,
            [FromQuery] StorefrontProductSort sort = StorefrontProductSort.Relevance,
            [FromQuery] string? category = null,
            [FromQuery] bool onSale = false,
            [FromQuery] bool freeShipping = false,
            [FromQuery] bool facets = false)
        {
            if (q?.Length > MaxQueryLength || category?.Length > MaxQueryLength)
                return BadRequest(ControllerResponseSanitizer.InvalidRequestPayloadMessage);

            return Ok(await _storefrontService.GetProductsAsync(new StorefrontProductQuery(
                q, enterpriseSlug, minPrice, maxPrice, available, sort,
                Math.Max(page, 1), Math.Clamp(size, 1, MaxProductPageSize), category, onSale, freeShipping, facets)));
        }

        [HttpGet("{id:guid}")]
        [ActionName("Product")]
        [Authorize(Policy = "rbac:" + RbacPermissions.Storefront.CatalogRead)]
        [ProducesResponseType(typeof(StorefrontProductDetailDTO), 200)]
        [ProducesResponseType(404)]
        public async Task<IActionResult> GetProduct(Guid id)
        {
            var product = await _storefrontService.GetProductAsync(id);
            return product == null ? NotFound() : Ok(product);
        }

        /// <summary>The seller's original picture. Storefront.Api resizes it; browsers never get it as-is.</summary>
        [HttpGet("{id:guid}")]
        [Authorize(Policy = "rbac:" + RbacPermissions.Storefront.CatalogRead)]
        [ProducesResponseType(200)]
        [ProducesResponseType(404)]
        public async Task<IActionResult> ProductPicture(Guid id)
        {
            var picture = await _storefrontService.GetPictureAsync(id);
            return picture == null ? NotFound() : File(picture.Content, picture.ContentType);
        }

        [HttpGet("{imageId:guid}")]
        [ActionName("ProductImage")]
        [Authorize(Policy = "rbac:" + RbacPermissions.Storefront.CatalogRead)]
        [ProducesResponseType(200)]
        [ProducesResponseType(404)]
        public async Task<IActionResult> ProductImage(Guid imageId)
        {
            var image = await _storefrontService.GetProductImageAsync(imageId);
            return image == null ? NotFound() : File(image.Content, image.ContentType);
        }

        /// <summary>A listed shop's uploaded logo, original bytes; Storefront.Api resizes it.</summary>
        [HttpGet("{slug}")]
        [Authorize(Policy = "rbac:" + RbacPermissions.Storefront.CatalogRead)]
        [ProducesResponseType(200)]
        [ProducesResponseType(404)]
        public Task<IActionResult> EnterpriseLogo(string slug) => EnterpriseImage(slug, StorefrontImageKind.Logo);

        /// <summary>A listed shop's 16:5 cover banner, original bytes.</summary>
        [HttpGet("{slug}")]
        [Authorize(Policy = "rbac:" + RbacPermissions.Storefront.CatalogRead)]
        [ProducesResponseType(200)]
        [ProducesResponseType(404)]
        public Task<IActionResult> EnterpriseCover(string slug) => EnterpriseImage(slug, StorefrontImageKind.Cover);

        private async Task<IActionResult> EnterpriseImage(string slug, StorefrontImageKind kind)
        {
            var image = await _storefrontService.GetEnterpriseImageAsync(slug, kind);
            return image == null ? NotFound() : File(image.Content, image.ContentType);
        }

        /// <summary>A published family's own photo, as <see cref="ProductPicture"/> does for one row.</summary>
        [HttpGet("{id:guid}")]
        [Authorize(Policy = "rbac:" + RbacPermissions.Storefront.CatalogRead)]
        [ProducesResponseType(200)]
        [ProducesResponseType(404)]
        public async Task<IActionResult> FamilyPicture(Guid id)
        {
            var picture = await _storefrontService.GetFamilyPictureAsync(id);
            return picture == null ? NotFound() : File(picture.Content, picture.ContentType);
        }

        /// <summary>The active tree with published card counts; Limen hides nodes counting zero.</summary>
        [HttpGet]
        [Authorize(Policy = "rbac:" + RbacPermissions.Storefront.CatalogRead)]
        [ProducesResponseType(typeof(IReadOnlyList<StorefrontPublicCategoryDTO>), 200)]
        public async Task<IActionResult> Categories() => Ok(await _categoryService.GetPublicTreeAsync());

        /// <summary>A category page; an old slug answers with <c>redirectTo</c>.</summary>
        [HttpGet("{slug}")]
        [Authorize(Policy = "rbac:" + RbacPermissions.Storefront.CatalogRead)]
        [ProducesResponseType(typeof(StorefrontCategoryPageDTO), 200)]
        [ProducesResponseType(404)]
        public async Task<IActionResult> CategoryBySlug(string slug) =>
            slug.Length <= 60 && await _categoryService.GetPublicPageAsync(slug) is { } page ? Ok(page) : NotFound();

        #endregion

        #region Checkout (service account)

        [HttpPost]
        [RequestSizeLimit(MaxCheckoutRequestBodySizeInBytes)]
        [Authorize(Policy = "rbac:" + RbacPermissions.Storefront.Checkout)]
        [ProducesResponseType(typeof(StorefrontQuoteDTO), 200)]
        [ProducesResponseType(400)]
        public async Task<IActionResult> Quote([FromBody] StorefrontQuoteRequestDTO request) =>
            Ok(await _checkoutService.QuoteAsync(request.Lines));

        /// <summary>201 with the checkout; 409 with the new quote when the price moved; 503 while no payment method is connected.</summary>
        [HttpPost]
        [ActionName("Checkout")]
        [RequestSizeLimit(MaxCheckoutRequestBodySizeInBytes)]
        [Authorize(Policy = "rbac:" + RbacPermissions.Storefront.Checkout)]
        [ProducesResponseType(typeof(StorefrontCheckoutDTO), 201)]
        [ProducesResponseType(typeof(StorefrontQuoteDTO), 409)]
        [ProducesResponseType(400)]
        [ProducesResponseType(503)]
        public async Task<IActionResult> CreateCheckout([FromBody] StorefrontCheckoutRequestDTO request)
        {
            if (!_checkoutService.SimulationEnabled)
                return StatusCode(StatusCodes.Status503ServiceUnavailable, new { message = "Pagamentos ainda não estão disponíveis." });

            var result = await _checkoutService.CreateAsync(request);
            return result.Checkout == null
                ? Conflict(result.ChangedQuote)
                : StatusCode(StatusCodes.Status201Created, result.Checkout);
        }

        [HttpGet("{id:guid}")]
        [ActionName("Checkout")]
        [Authorize(Policy = "rbac:" + RbacPermissions.Storefront.Checkout)]
        [ProducesResponseType(typeof(StorefrontCheckoutDTO), 200)]
        [ProducesResponseType(404)]
        public async Task<IActionResult> GetCheckout(Guid id) =>
            await _checkoutService.GetAsync(id) is { } checkout ? Ok(checkout) : NotFound();

        [HttpPost("{id:guid}")]
        [Authorize(Policy = "rbac:" + RbacPermissions.Storefront.Checkout)]
        [ProducesResponseType(typeof(StorefrontCheckoutDTO), 200)]
        [ProducesResponseType(404)]
        public async Task<IActionResult> RenewPix(Guid id) =>
            await _checkoutService.RenewPixAsync(id) is { } checkout ? Ok(checkout) : NotFound();

        /// <summary>Test mode only (<c>Storefront:SimulatePayments</c>): settles a checkout as the payment provider would.</summary>
        [HttpPost("{id:guid}")]
        [Authorize(Policy = "rbac:" + RbacPermissions.Storefront.Checkout)]
        [ProducesResponseType(typeof(StorefrontCheckoutDTO), 200)]
        [ProducesResponseType(404)]
        public async Task<IActionResult> Simulate(Guid id, [FromBody] StorefrontSimulateRequestDTO request) =>
            await _checkoutService.SimulateAsync(id, request.Outcome) is { } checkout ? Ok(checkout) : NotFound();

        #endregion

        #region Purchases (service account)

        [HttpGet("{shopperId:guid}")]
        [Authorize(Policy = "rbac:" + RbacPermissions.Storefront.OrdersRead)]
        [ProducesResponseType(typeof(IReadOnlyList<StorefrontPurchaseDTO>), 200)]
        public async Task<IActionResult> Purchases(Guid shopperId) =>
            Ok(await _checkoutService.ListPurchasesAsync(shopperId));

        [HttpGet("{checkoutId:guid}")]
        [Authorize(Policy = "rbac:" + RbacPermissions.Storefront.OrdersRead)]
        [ProducesResponseType(typeof(StorefrontPurchaseDTO), 200)]
        [ProducesResponseType(404)]
        public async Task<IActionResult> Purchase(Guid checkoutId) =>
            await _checkoutService.GetPurchaseAsync(checkoutId) is { } purchase ? Ok(purchase) : NotFound();

        [HttpGet("{shopperId:guid}")]
        [Authorize(Policy = "rbac:" + RbacPermissions.Storefront.OrdersRead)]
        [ProducesResponseType(typeof(IReadOnlyList<StorefrontPurchaseContactDTO>), 200)]
        public async Task<IActionResult> PurchaseContacts(Guid shopperId) =>
            Ok(await _checkoutService.ListPurchaseContactsAsync(shopperId));

        [HttpGet("{checkoutId:guid}")]
        [Authorize(Policy = "rbac:" + RbacPermissions.Storefront.OrdersRead)]
        [ProducesResponseType(typeof(StorefrontOwnerDTO), 200)]
        public async Task<IActionResult> Owner(Guid checkoutId) =>
            Ok(new StorefrontOwnerDTO(await _checkoutService.OwnerOfAsync(checkoutId)));

        [HttpPost("{checkoutId:guid}")]
        [Authorize(Policy = "rbac:" + RbacPermissions.Storefront.OrdersWrite)]
        [ProducesResponseType(200)]
        [ProducesResponseType(409)]
        public async Task<IActionResult> AttachShopper(Guid checkoutId, [FromBody] StorefrontAttachRequestDTO request) =>
            await _checkoutService.AttachShopperAsync(checkoutId, request.ShopperId) ? Ok() : Conflict();

        [HttpPost]
        [Authorize(Policy = "rbac:" + RbacPermissions.Storefront.OrdersWrite)]
        [ProducesResponseType(200)]
        public async Task<IActionResult> AttachGuestPurchases([FromBody] StorefrontAttachByEmailRequestDTO request) =>
            Ok(new { attached = await _checkoutService.AttachGuestPurchasesAsync(request.Email, request.ShopperId) });

        [HttpPost("{shopperId:guid}")]
        [Authorize(Policy = "rbac:" + RbacPermissions.Storefront.OrdersWrite)]
        [ProducesResponseType(204)]
        public async Task<IActionResult> DetachShopper(Guid shopperId)
        {
            await _checkoutService.DetachShopperAsync(shopperId);
            return NoContent();
        }

        /// <summary>The 7-day withdrawal. 404 when the order is not in that checkout.</summary>
        [HttpPost]
        [Authorize(Policy = "rbac:" + RbacPermissions.Storefront.OrdersWrite)]
        [ProducesResponseType(typeof(StorefrontOrderDTO), 200)]
        [ProducesResponseType(404)]
        public async Task<IActionResult> OrderWithdrawal([FromBody] StorefrontOrderRefDTO request)
        {
            if (!Guid.TryParse(User.FindFirst("uid")?.Value, out var actor))
                return Unauthorized();

            return await _checkoutService.RequestWithdrawalAsync(request.CheckoutId, request.OrderNumber, actor) is { } order ? Ok(order) : NotFound();
        }

        /// <summary>Test mode only: moves an order along as its store would.</summary>
        [HttpPost]
        [Authorize(Policy = "rbac:" + RbacPermissions.Storefront.OrdersRead)]
        [ProducesResponseType(typeof(StorefrontOrderDTO), 200)]
        [ProducesResponseType(404)]
        public async Task<IActionResult> SimulateOrderStep([FromBody] StorefrontSimulateStepRequestDTO request) =>
            await _checkoutService.SimulateStepAsync(request.CheckoutId, request.OrderNumber, request.Step) is { } order
                ? Ok(order)
                : NotFound();

        /// <summary>The authorized NFe XML of one order — the shopper is its recipient.</summary>
        [HttpGet("{checkoutId:guid}/{orderNumber}")]
        [Authorize(Policy = "rbac:" + RbacPermissions.Storefront.OrdersRead)]
        [ProducesResponseType(200)]
        [ProducesResponseType(404)]
        public async Task<IActionResult> OrderInvoice(Guid checkoutId, string orderNumber) =>
            await _checkoutService.GetInvoiceAsync(checkoutId, orderNumber) is { } invoice
                ? File(invoice.Xml, "application/xml", $"NFe{invoice.AccessKey}.xml")
                : NotFound();

        #endregion

        #region Listing (ERP staff)

        [HttpGet]
        [Authorize(Policy = "rbac:" + RbacPermissions.Storefront.Manage)]
        [ProducesResponseType(typeof(StorefrontListingDTO), 200)]
        [ProducesResponseType(401)]
        [ProducesResponseType(403)]
        public async Task<IActionResult> Listing()
        {
            if (!TryGetEnterpriseId(out var enterpriseId))
                return Unauthorized();

            return Ok(await _storefrontService.GetListingAsync(enterpriseId));
        }

        [HttpPut]
        [RequestSizeLimit(MaxListingRequestBodySizeInBytes)]
        [Authorize(Policy = "rbac:" + RbacPermissions.Storefront.Manage)]
        [ProducesResponseType(typeof(StorefrontListingDTO), 200)]
        [ProducesResponseType(400)]
        [ProducesResponseType(401)]
        [ProducesResponseType(403)]
        public async Task<IActionResult> SetListing([FromBody] SetStorefrontListingRequestDTO request)
        {
            if (request == null)
                return BadRequest(ControllerResponseSanitizer.InvalidRequestPayloadMessage);

            if (!TryGetEnterpriseId(out var enterpriseId))
                return Unauthorized();

            try
            {
                return Ok(await _storefrontService.SetListingAsync(enterpriseId, request));
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        /// <summary>Replaces the caller's shop logo or cover. PNG, JPEG or WebP, up to 5 MB.</summary>
        [HttpPut]
        [RequestSizeLimit(MaxListingImageRequestBodySizeInBytes)]
        [Authorize(Policy = "rbac:" + RbacPermissions.Storefront.Manage)]
        [ProducesResponseType(typeof(StorefrontListingDTO), 200)]
        [ProducesResponseType(400)]
        [ProducesResponseType(401)]
        [ProducesResponseType(403)]
        public async Task<IActionResult> UploadListingImage([FromBody] UploadStorefrontImageRequestDTO request)
        {
            if (!TryParseImageKind(request?.Kind, out var kind))
                return BadRequest(new { message = "kind deve ser logo ou cover." });

            if (!TryGetEnterpriseId(out var enterpriseId))
                return Unauthorized();

            try
            {
                return Ok(await _storefrontService.SetListingImageAsync(enterpriseId, kind, request!.File));
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        /// <summary>The caller's own shop image, for the settings preview.</summary>
        [HttpGet("{kind}")]
        [Authorize(Policy = "rbac:" + RbacPermissions.Storefront.Manage)]
        [ProducesResponseType(200)]
        [ProducesResponseType(404)]
        public async Task<IActionResult> ListingImage(string kind)
        {
            if (!TryParseImageKind(kind, out var parsed))
                return NotFound();

            if (!TryGetEnterpriseId(out var enterpriseId))
                return Unauthorized();

            var image = await _storefrontService.GetListingImageAsync(enterpriseId, parsed);
            return image == null ? NotFound() : File(image.Content, image.ContentType);
        }

        [HttpDelete("{kind}")]
        [Authorize(Policy = "rbac:" + RbacPermissions.Storefront.Manage)]
        [ProducesResponseType(typeof(StorefrontListingDTO), 200)]
        [ProducesResponseType(400)]
        public async Task<IActionResult> RemoveListingImage(string kind)
        {
            if (!TryParseImageKind(kind, out var parsed))
                return BadRequest(new { message = "kind deve ser logo ou cover." });

            if (!TryGetEnterpriseId(out var enterpriseId))
                return Unauthorized();

            return Ok(await _storefrontService.RemoveListingImageAsync(enterpriseId, parsed));
        }

        private static bool TryParseImageKind(string? value, out StorefrontImageKind kind) =>
            Enum.TryParse(value, ignoreCase: true, out kind) && Enum.IsDefined(kind)
            && !int.TryParse(value, out _);

        [HttpPut]
        [RequestSizeLimit(MaxPublishRequestBodySizeInBytes)]
        [Authorize(Policy = "rbac:" + RbacPermissions.Storefront.Manage)]
        [ProducesResponseType(200)]
        [ProducesResponseType(400)]
        [ProducesResponseType(401)]
        [ProducesResponseType(403)]
        public async Task<IActionResult> SetPublished([FromBody] SetStorefrontPublishedRequestDTO request)
        {
            if (request?.ProductIds == null)
                return BadRequest(ControllerResponseSanitizer.InvalidRequestPayloadMessage);

            if (!TryGetEnterpriseId(out var enterpriseId))
                return Unauthorized();

            try
            {
                var changed = await _storefrontService.SetPublishedAsync(enterpriseId, request.ProductIds.Distinct().ToList(), request.Published);
                return Ok(new { changed });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        #endregion

        #region Promotions (ERP staff)

        /// <summary>A product's "de/por" and the "de" prices its history allows (SPEC-limen-pricing-rules §2).</summary>
        [HttpGet("{productId:guid}")]
        [Authorize(Policy = "rbac:" + RbacPermissions.Storefront.Manage)]
        [ProducesResponseType(typeof(StorefrontPromotionDTO), 200)]
        [ProducesResponseType(401)]
        [ProducesResponseType(404)]
        public async Task<IActionResult> Promotion(Guid productId)
        {
            if (!TryGetEnterpriseId(out var enterpriseId))
                return Unauthorized();

            return await _promotionService.GetAsync(enterpriseId, productId) is { } promotion ? Ok(promotion) : NotFound();
        }

        /// <summary>Starts or changes a promotion; 400 names the rule that refused it.</summary>
        [HttpPut]
        [ActionName("Promotion")]
        [RequestSizeLimit(MaxListingRequestBodySizeInBytes)]
        [Authorize(Policy = "rbac:" + RbacPermissions.Storefront.Manage)]
        [ProducesResponseType(typeof(StorefrontPromotionDTO), 200)]
        [ProducesResponseType(400)]
        [ProducesResponseType(401)]
        [ProducesResponseType(404)]
        public async Task<IActionResult> SetPromotion([FromBody] SetStorefrontPromotionRequestDTO request)
        {
            if (!TryGetEnterpriseId(out var enterpriseId) || !Guid.TryParse(User.FindFirst("uid")?.Value, out var userId))
                return Unauthorized();

            try
            {
                return await _promotionService.SetAsync(enterpriseId, userId, request) is { } promotion ? Ok(promotion) : NotFound();
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpPut("{productId:guid}/End")]
        [ActionName("Promotion")]
        [Authorize(Policy = "rbac:" + RbacPermissions.Storefront.Manage)]
        [ProducesResponseType(typeof(StorefrontPromotionDTO), 200)]
        [ProducesResponseType(401)]
        [ProducesResponseType(404)]
        public async Task<IActionResult> EndPromotion(Guid productId)
        {
            if (!TryGetEnterpriseId(out var enterpriseId))
                return Unauthorized();

            return await _promotionService.EndAsync(enterpriseId, productId) is { } promotion ? Ok(promotion) : NotFound();
        }

        #endregion

        #region Categories (ERP staff)

        /// <summary>The tree with this tenant's product counts; the Limen team also sees inactive nodes.</summary>
        [HttpGet]
        [Authorize(Policy = "rbac:" + RbacPermissions.Storefront.CategoriesRead)]
        [ProducesResponseType(typeof(StorefrontCategoryTreeDTO), 200)]
        [ProducesResponseType(401)]
        public async Task<IActionResult> CategoryTree()
        {
            if (!TryGetEnterpriseId(out var enterpriseId))
                return Unauthorized();

            var platform = IsPlatformTeam(enterpriseId);
            return Ok(new StorefrontCategoryTreeDTO(platform, await _categoryService.GetTreeAsync(enterpriseId, includeInactive: platform)));
        }

        [HttpPost]
        [ActionName("Category")]
        [RequestSizeLimit(MaxListingRequestBodySizeInBytes)]
        [Authorize(Policy = "rbac:" + RbacPermissions.Storefront.CategoriesManage)]
        [ProducesResponseType(typeof(StorefrontCategoryNodeDTO), 200)]
        [ProducesResponseType(400)]
        [ProducesResponseType(403)]
        public Task<IActionResult> CreateCategory([FromBody] SaveStorefrontCategoryRequestDTO request) =>
            ManageCategoryAsync(async () => await _categoryService.CreateAsync(request));

        [HttpPut("{id:guid}")]
        [ActionName("Category")]
        [RequestSizeLimit(MaxListingRequestBodySizeInBytes)]
        [Authorize(Policy = "rbac:" + RbacPermissions.Storefront.CategoriesManage)]
        [ProducesResponseType(typeof(StorefrontCategoryNodeDTO), 200)]
        [ProducesResponseType(400)]
        [ProducesResponseType(403)]
        [ProducesResponseType(404)]
        public Task<IActionResult> UpdateCategory(Guid id, [FromBody] SaveStorefrontCategoryRequestDTO request) =>
            ManageCategoryAsync(() => _categoryService.UpdateAsync(id, request));

        [HttpPut("{id:guid}/Move")]
        [ActionName("Category")]
        [RequestSizeLimit(MaxListingRequestBodySizeInBytes)]
        [Authorize(Policy = "rbac:" + RbacPermissions.Storefront.CategoriesManage)]
        [ProducesResponseType(typeof(StorefrontCategoryNodeDTO), 200)]
        [ProducesResponseType(400)]
        [ProducesResponseType(403)]
        [ProducesResponseType(404)]
        public Task<IActionResult> MoveCategory(Guid id, [FromBody] MoveStorefrontCategoryRequestDTO request) =>
            ManageCategoryAsync(() => _categoryService.MoveAsync(id, request));

        [HttpPut("{id:guid}/Deactivate")]
        [ActionName("Category")]
        [Authorize(Policy = "rbac:" + RbacPermissions.Storefront.CategoriesManage)]
        [ProducesResponseType(typeof(StorefrontCategoryNodeDTO), 200)]
        [ProducesResponseType(403)]
        [ProducesResponseType(404)]
        public Task<IActionResult> DeactivateCategory(Guid id) =>
            ManageCategoryAsync(() => _categoryService.SetActiveAsync(id, active: false));

        [HttpPut("{id:guid}/Activate")]
        [ActionName("Category")]
        [Authorize(Policy = "rbac:" + RbacPermissions.Storefront.CategoriesManage)]
        [ProducesResponseType(typeof(StorefrontCategoryNodeDTO), 200)]
        [ProducesResponseType(403)]
        [ProducesResponseType(404)]
        public Task<IActionResult> ActivateCategory(Guid id) =>
            ManageCategoryAsync(() => _categoryService.SetActiveAsync(id, active: true));

        /// <summary>Files the caller's products under a leaf; a variant family moves as one.</summary>
        [HttpPut]
        [RequestSizeLimit(MaxPublishRequestBodySizeInBytes)]
        [Authorize(Policy = "rbac:" + RbacPermissions.Products.Update)]
        [ProducesResponseType(200)]
        [ProducesResponseType(400)]
        [ProducesResponseType(401)]
        public async Task<IActionResult> ProductCategory([FromBody] SetProductCategoryRequestDTO request)
        {
            if (request?.ProductIds == null)
                return BadRequest(ControllerResponseSanitizer.InvalidRequestPayloadMessage);

            if (!TryGetEnterpriseId(out var enterpriseId))
                return Unauthorized();

            try
            {
                return Ok(new { changed = await _categoryService.AssignAsync(enterpriseId, request.ProductIds.Distinct().ToList(), request.CategoryId) });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        /// <summary>The tree is the Limen team's: an Admin of any other enterprise holds the permission but not this.</summary>
        private async Task<IActionResult> ManageCategoryAsync(Func<Task<StorefrontCategoryNodeDTO?>> action)
        {
            if (!TryGetEnterpriseId(out var enterpriseId) || !IsPlatformTeam(enterpriseId))
                return Forbid();

            try
            {
                return await action() is { } node ? Ok(node) : NotFound();
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        private bool IsPlatformTeam(Guid enterpriseId) =>
            Guid.TryParse(_configuration["Storefront:PlatformEnterpriseId"], out var platformId) && enterpriseId == platformId;

        #endregion

        private bool TryGetEnterpriseId(out Guid enterpriseId)
        {
            var claimValue = User.FindFirst(ClaimTypes.GroupSid)?.Value;
            return Guid.TryParse(claimValue, out enterpriseId) && enterpriseId != Guid.Empty;
        }
    }
}
