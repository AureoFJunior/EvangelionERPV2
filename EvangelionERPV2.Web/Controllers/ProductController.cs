using Microsoft.AspNetCore.Mvc;
using EvangelionERPV2.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using AutoMapper;
using Serilog;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.ProductModule.Application.Interface;
using EvangelionERPV2.Shared.DTOs;
using EvangelionERPV2.Shared.Exceptions;
using EvangelionERPV2.Shared.Enums;
using EvangelionERPV2.Shared.Repositories;
using EvangelionERPV2.Shared.Utils;
using System.Linq;
using System.Security.Claims;

namespace EvangelionERPV2.Web.Controllers
{
    [Authorize]
    [Route("api/v{version:apiVersion}/[controller]/[action]")]
    [ApiVersion("1.0")]
    public class ProductController : Controller
    {
        private const int MaxImageUploadRequestBodySizeInBytes = 8 * 1024 * 1024;
        private const int DefaultPageNumber = 1;
        private const int DefaultPageSize = 50;
        private const int MaxPageSize = 200;
        private const int MaxPageSizeWithPictures = 50;

        private readonly IProductService<Product> _productService;
        private readonly EvangelionERPV2.Shared.Repositories.IRepository<Product> _repository;
        private readonly IRepository<User> _userRepository;
        private readonly IMapper _mapper;
        private readonly IAuthorizationService _authorizationService;

        // Memoized for the lifetime of this request (controllers are per-request): ToProductDtosAsync
        // maps a page of products in parallel, and evaluating the policy per row would read the
        // caller's user row from the database once per product.
        private bool? _canReadCost;

        public ProductController(IProductService<Product> productService,
            EvangelionERPV2.Shared.Repositories.IRepository<Product> repository,
            IRepository<User> userRepository,
            IMapper mapper,
            IAuthorizationService authorizationService)
        {
            _productService = productService;
            _repository = repository;
            _userRepository = userRepository;
            _mapper = mapper;
            _authorizationService = authorizationService;
        }

        /// <summary>
        /// Return all the products (also works with pagination).
        /// </summary>
        /// <param name="pageNumber">Number of the current page</param>
        /// <param name="pageSize">Size of the desired page</param>
        /// <returns></returns>
        [Authorize(Policy = "rbac:" + RbacPermissions.Products.Read)]
        [HttpGet("{pageNumber?}/{pageSize?}")]
        [ProducesResponseType(typeof(IEnumerable<ProductDTO>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetProducts(int? pageNumber = null, int? pageSize = null, [FromQuery] bool includePictures = true)
        {

            if (!ModelState.IsValid) return BadRequest(ControllerResponseSanitizer.InvalidRequestPayloadMessage);

            if (!TryGetEnterpriseId(out var enterpriseId))
                return Unauthorized();
            var (normalizedPageNumber, normalizedPageSize) = PaginationExtensions.NormalizePagination(pageNumber, pageSize, MaxPageSize);
            if (includePictures && normalizedPageSize > MaxPageSizeWithPictures)
                normalizedPageSize = MaxPageSizeWithPictures;

            IEnumerable<Product> products = await _repository.GetAllAsync(
                normalizedPageNumber,
                normalizedPageSize,
                x => x.EnterpriseId != null && (x.EnterpriseId != default(Guid) && x.EnterpriseId == enterpriseId));
            if (products == null)
                return NoContent();

            return Ok(await ToProductDtosAsync(products, includePictures));
        }

        /// <summary>
        /// Return all the products that matches the filter (also works with pagination).
        /// </summary>
        /// <param name="descending">Order by type.</param>
        /// <param name="pageNumber">Number of the current page</param>
        /// <param name="pageSize">Size of the desired page</param>
        /// <param name="product">Object used to filter data.</param>
        /// <returns></returns>
        [Authorize(Policy = "rbac:" + RbacPermissions.Products.Read)]
        [HttpPost("{descending}/{pageNumber?}/{pageSize?}")]
        [RequestSizeLimit(MaxImageUploadRequestBodySizeInBytes)]
        [ProducesResponseType(typeof(IEnumerable<ProductDTO>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetProductsByFilter([FromBody] ProductFilterRequestDTO? filter, bool descending, int? pageNumber = null, int? pageSize = null, [FromQuery] bool includePictures = true)
        {
            try
            {
                if (!ModelState.IsValid)
                    return BadRequest(ControllerResponseSanitizer.InvalidRequestPayloadMessage);

                if (!TryGetEnterpriseId(out var enterpriseId))
                    return Unauthorized();
                filter ??= new ProductFilterRequestDTO();
                var nameFilter = filter.Name?.Trim();
                var isActiveFilter = filter.IsActive;
                var (normalizedPageNumber, normalizedPageSize) = PaginationExtensions.NormalizePagination(pageNumber, pageSize, MaxPageSize);
                if (includePictures && normalizedPageSize > MaxPageSizeWithPictures)
                    normalizedPageSize = MaxPageSizeWithPictures;

                // string.Contains(..., StringComparison) has no SQL Server translation; this
                // predicate is executed by the provider, so it must use EF.Functions.Like.
                var escapedNameFilter = SharedFunctions.EscapeLikePattern(nameFilter ?? string.Empty);

                var products = await _repository.GetAllAsync(
                    normalizedPageNumber,
                    normalizedPageSize,
                    x =>
                        x.EnterpriseId == enterpriseId &&
                        (!isActiveFilter.HasValue || x.IsActive == isActiveFilter.Value) &&
                        (string.IsNullOrWhiteSpace(nameFilter) || EF.Functions.Like(x.Name, $"%{escapedNameFilter}%", "\\")));

                if (products == null || !products.Any())
                    return NoContent();

                IEnumerable<ProductDTO> productDTO = await ToProductDtosAsync(products, includePictures);
                return Ok(productDTO);
            }
            catch (NotFoundDatabaseException exnf)
            {
                Log.Logger.Debug("Products not found. ErrorType={ErrorType}", exnf.GetType().Name);
                return NoContent();
            }
            catch (Exception)
            {
                throw;
            }
        }

        /// <summary>
        /// Get a product.
        /// </summary>
        /// <param name="id">Id of the product</param>
        /// <returns>The product that match with the id parameter.</returns>
        [Authorize(Policy = "rbac:" + RbacPermissions.Products.Read)]
        [HttpGet("{id}")]
        [ProducesResponseType(typeof(ProductDTO), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetProduct(Guid id)
        {
            try
            {
                if (!TryGetEnterpriseId(out var enterpriseId))
                    return Unauthorized();
                Product product = await _repository.GetByIdAsync(id);
                if (product == null || product.EnterpriseId != enterpriseId || product.IsActive != true)
                    return NoContent();

                return Ok(await ToProductDtoAsync(product));
            }
            catch (Exception)
            {
                throw;
            }
        }

        /// <summary>
        /// Add a new product
        /// </summary>
        /// <param name="product">Product to be added</param>
        /// <returns>The added product</returns>
        [Authorize(Policy = "rbac:" + RbacPermissions.Products.Create)]
        [HttpPost]
        [RequestSizeLimit(MaxImageUploadRequestBodySizeInBytes)]
        [ProducesResponseType(typeof(ProductDTO), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> AddProduct([FromBody] CreateProductRequestDTO request)
        {
            try
            {
                if (!ModelState.IsValid) return BadRequest(ControllerResponseSanitizer.InvalidRequestPayloadMessage);
                if (request == null) return BadRequest();
                var normalizedRequest = NormalizeCreateProductRequest(request);
                var enterpriseId = await ResolveCallerEnterpriseIdAsync();
                if (!enterpriseId.HasValue)
                    return Unauthorized();
                var product = new ProductPicture
                {
                    Product = new Product
                    {
                        Name = normalizedRequest.Name.Trim(),
                        Description = normalizedRequest.Description?.Trim() ?? string.Empty,
                        DefaultValue = normalizedRequest.DefaultValue.GetValueOrDefault(),
                        // Not GetValueOrDefault: an omitted cost stays unknown. Defaulting it to
                        // zero would make every margin report claim a 100% margin on this product.
                        CostPrice = await CanReadCostAsync() ? normalizedRequest.CostPrice : null,
                        // Zero here on purpose: the opening count is posted below as a movement, so
                        // the ledger explains the whole balance. Assigning it here left every new
                        // product with stock the ledger could not account for, which is why real
                        // drift was indistinguishable from a normal opening balance.
                        StorageQuantity = 0,
                        UnitOfMeasure = normalizedRequest.UnitOfMeasure?.Trim().ToUpperInvariant() ?? string.Empty,
                        IsExternal = normalizedRequest.IsExternal.GetValueOrDefault(),
                        IsService = normalizedRequest.IsService.GetValueOrDefault(),
                        PictureAdress = string.Empty,
                        EnterpriseId = enterpriseId.Value,
                        IsActive = true
                    },
                    File = normalizedRequest.File ?? string.Empty
                };

                ApplyFiscalFields(normalizedRequest, product.Product);

                Product createdProduct = await _productService.CreateAsync(product);
                if (createdProduct == null)
                    return NoContent();

                var openingQuantity = normalizedRequest.StorageQuantity.GetValueOrDefault();
                if (openingQuantity > 0)
                {
                    var opening = await _productService.ApplyStockMovementAsync(
                        createdProduct.Id,
                        enterpriseId.Value,
                        openingQuantity,
                        EnumStockMovementType.Adjustment,
                        referenceId: null);

                    createdProduct.StorageQuantity = opening.BalanceAfter;
                }

                return Ok(await ToProductDtoAsync(createdProduct));
            }
            catch (Exception)
            {
                throw;
            }
        }

        /// <summary>
        /// Update an product
        /// </summary>
        /// <param name="product">Product to be updated</param>
        /// <returns>The updated product</returns>
        [Authorize(Policy = "rbac:" + RbacPermissions.Products.Update)]
        [HttpPut]
        [RequestSizeLimit(MaxImageUploadRequestBodySizeInBytes)]
        [ProducesResponseType(typeof(ProductDTO), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> UpdateProduct([FromBody] UpdateProductRequestDTO request)
        {
            try
            {
                if (!ModelState.IsValid) return BadRequest(ControllerResponseSanitizer.InvalidRequestPayloadMessage);
                if (request == null) return BadRequest();
                if (!TryGetEnterpriseId(out var enterpriseId))
                    return Unauthorized();
                var existentProduct = await _repository.GetByIdAsync(request.Id);
                if (existentProduct == null || existentProduct.EnterpriseId != enterpriseId || existentProduct.IsActive != true)
                    return NoContent();

                existentProduct.Name = request.Name.Trim();
                existentProduct.Description = request.Description?.Trim() ?? string.Empty;
                existentProduct.DefaultValue = request.DefaultValue;
                // Only a caller entitled to see the cost may change it, and an omitted value keeps
                // the stored one: everyone else receives a null cost in the DTO, so echoing the
                // form back would otherwise erase it on every edit they save.
                if (request.CostPrice.HasValue && await CanReadCostAsync())
                    existentProduct.CostPrice = request.CostPrice;
                // Stock moves through the ledger, never by assignment. ApplyStockMovementAsync is
                // the only place StorageQuantity is allowed to change, so a human correcting a
                // count leaves an Adjustment row behind exactly like a sale or a receipt does —
                // otherwise the scalar and the ledger drift apart and BalanceAfter stops being
                // reconstructible. The delta is applied by a compare-and-swap against the stored
                // value rather than by writing the number the form sent, so the correction is
                // relative to what is actually on hand.
                //
                // allowNegative is left false: only the PDV offline sync may drive stock below
                // zero, and the validator already caps the target at zero or greater, so the
                // guard can only ever fire on a concurrent sale — which is exactly when it should.
                var stockDelta = request.StorageQuantity - existentProduct.StorageQuantity;
                if (stockDelta != 0)
                {
                    var adjustment = await _productService.ApplyStockMovementAsync(
                        existentProduct.Id,
                        enterpriseId,
                        stockDelta,
                        EnumStockMovementType.Adjustment,
                        referenceId: null);

                    existentProduct.StorageQuantity = adjustment.BalanceAfter;
                }

                existentProduct.UnitOfMeasure = request.UnitOfMeasure?.Trim().ToUpperInvariant() ?? string.Empty;
                existentProduct.IsExternal = request.IsExternal;
                existentProduct.IsService = request.IsService;
                if (!string.IsNullOrWhiteSpace(request.PictureAdress))
                    return BadRequest("PictureAdress cannot be updated here. Use UploadPicture.");
                existentProduct.IsActive = request.IsActive ?? existentProduct.IsActive;
                ApplyFiscalFields(request, existentProduct);
                existentProduct.UpdatedAt = DateTime.UtcNow;

                Product updatedProduct = await _productService.UpdateAsync(existentProduct);

                if (updatedProduct == null)
                    return NoContent();

                return Ok(await ToProductDtoAsync(updatedProduct));
            }
            catch (Exception)
            {
                throw;
            }
        }

        /// <summary>
        /// Delete an product (soft delete)
        /// </summary>
        /// <param name="id">Product's Id</param>
        /// <returns>The deleted product</returns>
        [Authorize(Policy = "rbac:" + RbacPermissions.Products.Delete)]
        [HttpDelete]
        [ProducesResponseType(typeof(ProductDTO), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> DeleteProduct(Guid id)
        {
            try
            {
                if (!ModelState.IsValid) return BadRequest(ControllerResponseSanitizer.InvalidRequestPayloadMessage);
                if (!TryGetEnterpriseId(out var enterpriseId))
                    return Unauthorized();
                var existentProduct = await _repository.GetByIdAsync(id);
                if (existentProduct == null || existentProduct.EnterpriseId != enterpriseId || existentProduct.IsActive != true)
                    return NoContent();

                Product product = _productService.Delete(id);
                if (product == null)
                    return NoContent();

                return Ok(await ToProductDtoAsync(product));
            }
            catch (Exception)
            {
                throw;
            }
        }

        /// <summary>
        /// Upload an Product's picture
        /// </summary>
        /// <param name="product">Product to be updated</param>
        /// <returns>The updated product</returns>
        [Authorize(Policy = "rbac:" + RbacPermissions.Products.UploadPicture)]
        [HttpPatch]
        [RequestSizeLimit(MaxImageUploadRequestBodySizeInBytes)]
        [ProducesResponseType(typeof(ProductDTO), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> UploadPicture([FromBody] UploadProductPictureRequestDTO request)
        {
            try
            {
                if (!ModelState.IsValid) return BadRequest(ControllerResponseSanitizer.InvalidRequestPayloadMessage);
                if (request == null || request.ProductId == Guid.Empty)
                    return BadRequest("Product id is required.");
                if (!TryGetEnterpriseId(out var enterpriseId))
                    return Unauthorized();
                var existentProduct = await _repository.GetByIdAsync(request.ProductId);
                if (existentProduct == null || existentProduct.EnterpriseId != enterpriseId || existentProduct.IsActive != true)
                    return NoContent();

                existentProduct.UpdatedAt = DateTime.UtcNow;

                var normalizedPayload = new ProductPicture
                {
                    Product = existentProduct,
                    File = request.File
                };

                Product updatedProduct = await _productService.UpdatePictureAsync(normalizedPayload);

                if (updatedProduct == null)
                    return NoContent();

                return Ok(await ToProductDtoAsync(updatedProduct));
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

        private Guid? TryGetUserId()
        {
            var claimValue = User.FindFirst(ClaimTypes.Sid)?.Value
                ?? User.FindFirst("uid")?.Value
                ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            if (Guid.TryParse(claimValue, out var userId) && userId != Guid.Empty)
                return userId;

            return null;
        }

        private async Task<Guid?> ResolveCallerEnterpriseIdAsync()
        {
            var userId = TryGetUserId();
            if (userId.HasValue)
            {
                try
                {
                    var user = await _userRepository.GetByIdAsync(userId.Value);
                    if (user != null && user.IsActive == true && user.EnterpriseId.HasValue && user.EnterpriseId.Value != Guid.Empty)
                        return user.EnterpriseId.Value;
                }
                catch (NotFoundDatabaseException)
                {
                }
            }

            return TryGetEnterpriseId(out var claimEnterpriseId) ? claimEnterpriseId : null;
        }

        private static CreateProductRequestDTO NormalizeCreateProductRequest(CreateProductRequestDTO request)
        {
            if (request.Product == null)
                return request;

            return new CreateProductRequestDTO
            {
                Name = ResolveLegacyOrRootValue(request.Name, request.Product.Name),
                Description = ResolveLegacyOrRootValue(request.Description, request.Product.Description),
                DefaultValue = ResolveLegacyOrRootNumericValue(request.DefaultValue, request.Product.DefaultValue),
                StorageQuantity = ResolveLegacyOrRootNumericValue(request.StorageQuantity, request.Product.StorageQuantity),
                UnitOfMeasure = ResolveLegacyOrRootValue(request.UnitOfMeasure, request.Product.UnitOfMeasure),
                IsExternal = ResolveLegacyOrRootBoolValue(request.IsExternal, request.Product.IsExternal),
                IsService = ResolveLegacyOrRootBoolValue(request.IsService, request.Product.IsService),
                PictureAdress = ResolveLegacyOrRootValue(request.PictureAdress, request.Product.PictureAdress),
                File = request.File,

                // Same reasoning as the fiscal block: the legacy nested shape has no cost field.
                CostPrice = request.CostPrice,

                // Carried straight across: the legacy nested shape predates fiscal data and has
                // no counterpart to fall back to, so rebuilding the request must not drop them.
                Ncm = request.Ncm,
                Cest = request.Cest,
                Cean = request.Cean,
                CfopIntra = request.CfopIntra,
                CfopInter = request.CfopInter,
                Origem = request.Origem,
                UnidadeTributavel = request.UnidadeTributavel,
                Cst = request.Cst,
                Csosn = request.Csosn,
                AliquotaIcms = request.AliquotaIcms,
                ReducaoBaseIcms = request.ReducaoBaseIcms,
                CstPis = request.CstPis,
                AliquotaPis = request.AliquotaPis,
                CstCofins = request.CstCofins,
                AliquotaCofins = request.AliquotaCofins,
                CodigoBeneficioFiscal = request.CodigoBeneficioFiscal
            };
        }

        /// <summary>
        /// Copies the per-product fiscal overrides from a request body onto the entity.
        /// </summary>
        /// <remarks>
        /// Whitespace becomes null rather than an empty string: null is what
        /// <c>NFeItemFiscalResolver</c> reads as "use the enterprise default", and "" would be a
        /// per-product override to nothing, which SEFAZ rejects.
        /// </remarks>
        private static void ApplyFiscalFields(IProductFiscalFields request, Product product)
        {
            product.Ncm = Trimmed(request.Ncm);
            product.Cest = Trimmed(request.Cest);
            product.Cean = Trimmed(request.Cean);
            product.CfopIntra = Trimmed(request.CfopIntra);
            product.CfopInter = Trimmed(request.CfopInter);
            product.Origem = request.Origem;
            product.UnidadeTributavel = Trimmed(request.UnidadeTributavel)?.ToUpperInvariant();
            product.Cst = Trimmed(request.Cst);
            product.Csosn = Trimmed(request.Csosn);
            product.AliquotaIcms = request.AliquotaIcms;
            product.ReducaoBaseIcms = request.ReducaoBaseIcms;
            product.CstPis = Trimmed(request.CstPis);
            product.AliquotaPis = request.AliquotaPis;
            product.CstCofins = Trimmed(request.CstCofins);
            product.AliquotaCofins = request.AliquotaCofins;
            product.CodigoBeneficioFiscal = Trimmed(request.CodigoBeneficioFiscal);
        }

        private static string? Trimmed(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        private static string ResolveLegacyOrRootValue(string? rootValue, string? legacyValue)
        {
            if (!string.IsNullOrWhiteSpace(rootValue))
                return rootValue;

            return legacyValue ?? string.Empty;
        }

        private static decimal ResolveLegacyOrRootNumericValue(decimal? rootValue, decimal legacyValue)
        {
            return rootValue ?? legacyValue;
        }

        private static bool ResolveLegacyOrRootBoolValue(bool? rootValue, bool legacyValue)
        {
            return rootValue ?? legacyValue;
        }




        private async Task<IEnumerable<ProductDTO>> ToProductDtosAsync(IEnumerable<Product> products, bool includePictures = true)
        {
            var productList = products.ToList();
            if (productList.Count == 0)
                return Enumerable.Empty<ProductDTO>();

            // Primed before the parallel loop so the policy is evaluated once for the whole page.
            await CanReadCostAsync();

            var productDtos = new ProductDTO[productList.Count];
            var parallelOptions = new ParallelOptions
            {
                MaxDegreeOfParallelism = 8
            };

            await Parallel.ForEachAsync(Enumerable.Range(0, productList.Count), parallelOptions, async (index, _) =>
            {
                productDtos[index] = await ToProductDtoAsync(productList[index], includePictures);
            });

            return productDtos;
        }

        /// <summary>
        /// Whether this caller may see what the goods cost. Manager tier and above — see
        /// RbacRolePermissionMap. Cheap: the handler reads the caller's user row, which every
        /// authorized action on this controller has already read.
        /// </summary>
        private async Task<bool> CanReadCostAsync()
        {
            // A benign race under Parallel.ForEachAsync: two evaluations of the same policy for the
            // same caller answer the same thing, and the loser only overwrites an identical value.
            _canReadCost ??= (await _authorizationService.AuthorizeAsync(User, RbacPolicies.Products.ReadCost)).Succeeded;
            return _canReadCost.Value;
        }

        private async Task<ProductDTO> ToProductDtoAsync(Product product, bool includePicture = true)
        {
            ProductDTO dto = _mapper.Map<ProductDTO>(product);

            // Withheld rather than never mapped, the same shape EmployeeDTO uses for salary: one
            // funnel, so no read surface on this controller can leak the cost by forgetting.
            if (!await CanReadCostAsync())
            {
                dto.CostPrice = null;
                dto.MarginPercent = null;
            }

            if (!includePicture || string.IsNullOrWhiteSpace(product.PictureAdress))
            {
                dto.PictureAdress = string.Empty;
                return dto;
            }

            dto.PictureAdress = await _productService.GetPictureBase64Async(product.PictureAdress);
            return dto;
        }
    }
}
