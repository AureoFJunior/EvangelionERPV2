using EvangelionERPV2.ProductModule.Domain.Interface;
using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using EvangelionERPV2.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using AutoMapper;
using Serilog;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.ProductModule.Application.Interface;
using EvangelionERPV2.ProductModule.Application.Services;
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
        private const int MaxPageSize = 200;
        private const int MaxPageSizeWithPictures = 50;

        private readonly IProductService<Product> _productService;
        private readonly EvangelionERPV2.Shared.Repositories.IRepository<Product> _repository;
        private readonly IRepository<User> _userRepository;
        private readonly IMapper _mapper;
        private readonly IAuthorizationService _authorizationService;

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
                var groupIds = filter.VariantGroupIds is { Count: > 0 } ids ? ids.Distinct().ToList() : null;
                var categoryId = filter.StorefrontCategoryId;
                var uncategorized = filter.Uncategorized;
                var publishedOnly = filter.PublishedOnly;
                var (normalizedPageNumber, normalizedPageSize) = PaginationExtensions.NormalizePagination(pageNumber, pageSize, MaxPageSize);
                if (includePictures && normalizedPageSize > MaxPageSizeWithPictures)
                    normalizedPageSize = MaxPageSizeWithPictures;

                var escapedNameFilter = SharedFunctions.EscapeLikePattern(nameFilter ?? string.Empty);

                var products = await _repository.GetAllAsync(
                    normalizedPageNumber,
                    normalizedPageSize,
                    x =>
                        x.EnterpriseId == enterpriseId &&
                        (!isActiveFilter.HasValue || x.IsActive == isActiveFilter.Value) &&
                        (groupIds == null || (x.VariantGroupId.HasValue && groupIds.Contains(x.VariantGroupId.Value))) &&
                        (categoryId == null || x.StorefrontCategoryId == categoryId) &&
                        (!uncategorized || x.StorefrontCategoryId == null) &&
                        (!publishedOnly || x.IsPublishedOnStorefront) &&
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
        /// A product's photo as an image, so lists can load it lazily instead of carrying base64.
        /// <c>thumb</c> is the long-edge-200 JPEG kept in S3 beside the original. The ETag is derived
        /// from the stored key: a new upload gets a new key, so it also gets a new ETag.
        /// </summary>
        [Authorize(Policy = "rbac:" + RbacPermissions.Products.Read)]
        [HttpGet("{id:guid}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status304NotModified)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> Picture(Guid id, [FromQuery] string size = "thumb")
        {
            if (!TryGetEnterpriseId(out var enterpriseId))
                return Unauthorized();

            if (size is not ("thumb" or "full"))
                return BadRequest(ControllerResponseSanitizer.InvalidRequestPayloadMessage);

            var product = await _repository.GetByIdAsync(id);
            if (product == null || product.EnterpriseId != enterpriseId || product.IsActive != true)
                return NotFound();

            return await ServePictureAsync(product.PictureAdress, size);
        }

        /// <summary>A variant family's own photo (the one showing the whole family), same caching as <see cref="Picture"/>.</summary>
        [Authorize(Policy = "rbac:" + RbacPermissions.Products.Read)]
        [HttpGet("{variantGroupId:guid}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status304NotModified)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> FamilyPicture(Guid variantGroupId, [FromQuery] string size = "thumb")
        {
            if (!TryGetEnterpriseId(out var enterpriseId))
                return Unauthorized();

            if (size is not ("thumb" or "full"))
                return BadRequest(ControllerResponseSanitizer.InvalidRequestPayloadMessage);

            return await ServePictureAsync(await _productService.GetFamilyPictureAddressAsync(enterpriseId, variantGroupId), size);
        }

        /// <summary>Sets the photo of a whole variant family; each variant keeps its own.</summary>
        [Authorize(Policy = "rbac:" + RbacPermissions.Products.Update)]
        [HttpPatch]
        [RequestSizeLimit(MaxImageUploadRequestBodySizeInBytes)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> UploadFamilyPicture([FromBody] UploadFamilyPictureRequestDTO request)
        {
            if (!TryGetEnterpriseId(out var enterpriseId))
                return Unauthorized();

            return await _productService.SetFamilyPictureAsync(enterpriseId, request.VariantGroupId, request.File)
                ? Ok(new { variantGroupId = request.VariantGroupId, hasPicture = true })
                : NotFound();
        }

        private async Task<IActionResult> ServePictureAsync(string? pictureAddress, string size)
        {
            if (string.IsNullOrWhiteSpace(pictureAddress))
                return NotFound();

            var etag = $"\"{Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(pictureAddress + "|" + size)))[..32]}\"";
            Response.Headers.CacheControl = "private, max-age=300";
            Response.Headers.ETag = etag;
            if (Request.Headers.IfNoneMatch.Contains(etag))
                return StatusCode(StatusCodes.Status304NotModified);

            var base64 = size == "thumb"
                ? (await _productService.GetThumbnailBase64Async(pictureAddress)).Base64
                : await _productService.GetPictureBase64Async(pictureAddress);
            if (string.IsNullOrWhiteSpace(base64))
                return NotFound();

            var bytes = Convert.FromBase64String(base64);
            return File(bytes, ImageContentType(bytes));
        }

        private static string ImageContentType(byte[] bytes) => bytes switch
        {
            [0x89, 0x50, 0x4E, 0x47, ..] => "image/png",
            [(byte)'R', (byte)'I', (byte)'F', (byte)'F', _, _, _, _, (byte)'W', (byte)'E', (byte)'B', (byte)'P', ..] => "image/webp",
            [(byte)'G', (byte)'I', (byte)'F', ..] => "image/gif",
            _ => "image/jpeg"
        };

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
                        CostPrice = await CanReadCostAsync() ? normalizedRequest.CostPrice : null,
                        StorageQuantity = normalizedRequest.StorageQuantity.GetValueOrDefault(),
                        UnitOfMeasure = normalizedRequest.UnitOfMeasure?.Trim().ToUpperInvariant() ?? string.Empty,
                        IsExternal = normalizedRequest.IsExternal.GetValueOrDefault(),
                        IsService = normalizedRequest.IsService.GetValueOrDefault(),
                        PictureAdress = string.Empty,
                        EnterpriseId = enterpriseId.Value,
                        VariantGroupId = normalizedRequest.VariantGroupId,
                        VariantLabel = Trimmed(normalizedRequest.VariantLabel),
                        StorefrontCategoryId = normalizedRequest.StorefrontCategoryId,
                        IsActive = true
                    },
                    File = normalizedRequest.File ?? string.Empty
                };

                ApplyFiscalFields(normalizedRequest, product.Product);

                Product createdProduct = await _productService.CreateAsync(product);
                if (createdProduct == null)
                    return NoContent();

                var created = await ToProductDtoAsync(createdProduct);
                created.PictureError = product.PictureError;
                return Ok(created);
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
                var existentProduct = await LoadForWriteAsync(request.Id);
                if (existentProduct == null || existentProduct.EnterpriseId != enterpriseId || existentProduct.IsActive != true)
                    return NoContent();
                if (!string.IsNullOrWhiteSpace(request.PictureAdress))
                    return BadRequest("PictureAdress cannot be updated here. Use UploadPicture.");

                existentProduct.Name = request.Name.Trim();
                existentProduct.Description = request.Description?.Trim() ?? string.Empty;
                existentProduct.DefaultValue = request.DefaultValue;
                if (request.CostPrice.HasValue && await CanReadCostAsync())
                    existentProduct.CostPrice = request.CostPrice;
                existentProduct.StorageQuantity = request.StorageQuantity;

                existentProduct.UnitOfMeasure = request.UnitOfMeasure?.Trim().ToUpperInvariant() ?? string.Empty;
                existentProduct.IsExternal = request.IsExternal;
                existentProduct.IsService = request.IsService;
                if (request.VariantGroupingWasProvided)
                {
                    existentProduct.VariantGroupId = request.VariantGroupId;
                    existentProduct.VariantLabel = Trimmed(request.VariantLabel);
                }
                existentProduct.StorefrontCategoryId = request.StorefrontCategoryId ?? existentProduct.StorefrontCategoryId;
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
                var existentProduct = await LoadForWriteAsync(id);
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
                var existentProduct = await LoadForWriteAsync(request.ProductId);
                if (existentProduct == null || existentProduct.EnterpriseId != enterpriseId || existentProduct.IsActive != true)
                    return NoContent();

                existentProduct.UpdatedAt = DateTime.UtcNow;

                var normalizedPayload = new ProductPicture
                {
                    Product = existentProduct,
                    File = request.File
                };

                Product updatedProduct;
                try
                {
                    updatedProduct = await _productService.UpdatePictureAsync(normalizedPayload);
                }
                catch (ArgumentException ex)
                {
                    return BadRequest(new { message = ex.Message });
                }

                if (updatedProduct == null)
                    return NoContent();

                return Ok(await ToProductDtoAsync(updatedProduct));
            }
            catch (Exception)
            {
                throw;
            }
        }

        [Authorize(Policy = "rbac:" + RbacPermissions.Products.Read)]
        [HttpGet("{productId:guid}")]
        [ProducesResponseType(typeof(IEnumerable<ProductImageDTO>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        public async Task<IActionResult> GetProductImages(Guid productId)
        {
            if (!TryGetEnterpriseId(out var enterpriseId))
                return Unauthorized();

            var product = await LoadForWriteAsync(productId);
            if (product == null || product.EnterpriseId != enterpriseId || product.IsActive != true)
                return NoContent();

            var images = await _productService.GetImagesAsync(enterpriseId, productId);
            return images.Count == 0
                ? NoContent()
                : Ok(images.Select(ToProductImageDto));
        }

        /// <summary>Returns one tenant-owned gallery image so ERP clients can preview secondary photos.</summary>
        [Authorize(Policy = "rbac:" + RbacPermissions.Products.Read)]
        [HttpGet("image/{imageId:guid}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetProductImage(Guid imageId)
        {
            if (!TryGetEnterpriseId(out var enterpriseId))
                return Unauthorized();

            var image = await _productService.GetImageAsync(enterpriseId, imageId);
            if (image == null)
                return NotFound();

            var product = await LoadForWriteAsync(image.ProductId);
            if (product == null || product.EnterpriseId != enterpriseId || product.IsActive != true)
                return NotFound();

            var base64 = await _productService.GetPictureBase64Async(image.Adress);
            if (string.IsNullOrWhiteSpace(base64))
                return NotFound();

            byte[] bytes;
            try
            {
                bytes = Convert.FromBase64String(base64);
            }
            catch (FormatException)
            {
                return NotFound();
            }

            var contentType = bytes.AsSpan().StartsWith(new byte[] { 0x89, 0x50, 0x4E, 0x47 })
                ? "image/png"
                : bytes.AsSpan().StartsWith(new byte[] { 0xFF, 0xD8, 0xFF }) ? "image/jpeg" : "application/octet-stream";
            return File(bytes, contentType);
        }

        [Authorize(Policy = "rbac:" + RbacPermissions.Products.UploadPicture)]
        [HttpPost]
        [RequestSizeLimit(MaxImageUploadRequestBodySizeInBytes)]
        [ProducesResponseType(typeof(ProductImageDTO), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        public async Task<IActionResult> AddProductImage([FromBody] UploadProductPictureRequestDTO request)
        {
            if (!ModelState.IsValid || request == null || request.ProductId == Guid.Empty)
                return BadRequest(ControllerResponseSanitizer.InvalidRequestPayloadMessage);
            if (!TryGetEnterpriseId(out var enterpriseId))
                return Unauthorized();

            var product = await LoadForWriteAsync(request.ProductId);
            if (product == null || product.EnterpriseId != enterpriseId || product.IsActive != true)
                return NoContent();

            try
            {
                var image = await _productService.AddImageAsync(product, request.File);
                return Ok(ToProductImageDto(image));
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (ProductGalleryFullException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [Authorize(Policy = "rbac:" + RbacPermissions.Products.UploadPicture)]
        [HttpPatch("{imageId:guid}")]
        [ProducesResponseType(typeof(IEnumerable<ProductImageDTO>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        public async Task<IActionResult> MoveProductImage(Guid imageId, [FromBody] MoveProductImageRequestDTO request)
        {
            if (!ModelState.IsValid || request == null)
                return BadRequest(ControllerResponseSanitizer.InvalidRequestPayloadMessage);
            if (!TryGetEnterpriseId(out var enterpriseId))
                return Unauthorized();

            var images = await _productService.MoveImageAsync(enterpriseId, imageId, request.Position);
            return images == null
                ? NoContent()
                : Ok(images.Select(ToProductImageDto));
        }

        [Authorize(Policy = "rbac:" + RbacPermissions.Products.DeletePicture)]
        [HttpDelete("{imageId:guid}")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        public async Task<IActionResult> RemoveProductImage(Guid imageId)
        {
            if (!TryGetEnterpriseId(out var enterpriseId))
                return Unauthorized();

            await _productService.RemoveImageAsync(enterpriseId, imageId);
            return NoContent();
        }

        private static ProductImageDTO ToProductImageDto(ProductImage image) =>
            new(image.Id, image.Position, image.Width, image.Height);

        private Task<Product> LoadForWriteAsync(Guid id) =>
            _repository is IProductRepository<Product> products ? products.GetFreshByIdAsync(id) : _repository.GetByIdAsync(id);

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

                CostPrice = request.CostPrice,

                VariantGroupId = request.VariantGroupId,
                VariantLabel = request.VariantLabel,
                StorefrontCategoryId = request.StorefrontCategoryId,

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

            await CanReadCostAsync();

            var productDtos = new ProductDTO[productList.Count];
            var parallelOptions = new ParallelOptions
            {
                MaxDegreeOfParallelism = 8
            };

            await Parallel.ForEachAsync(Enumerable.Range(0, productList.Count), parallelOptions, async (index, _) =>
            {
                productDtos[index] = await ToProductDtoAsync(productList[index], includePictures, familyHasPicture: false);
            });

            var groupIds = productList.Where(p => p.VariantGroupId != null).Select(p => p.VariantGroupId!.Value).Distinct().ToList();
            if (groupIds.Count > 0 && TryGetEnterpriseId(out var enterpriseId))
            {
                var withPicture = await _productService.FamiliesWithPictureAsync(enterpriseId, groupIds);
                for (var index = 0; index < productList.Count; index++)
                    productDtos[index].FamilyHasPicture = productList[index].VariantGroupId is { } group && withPicture.Contains(group);
            }

            if (productList.Any(p => p.StorefrontCategoryId != null))
            {
                var paths = await _productService.GetStorefrontCategoryPathsAsync();
                for (var index = 0; index < productList.Count; index++)
                    productDtos[index].StorefrontCategoryPath = productList[index].StorefrontCategoryId is { } category ? paths.GetValueOrDefault(category) : null;
            }

            return productDtos;
        }

        /// <summary>
        /// Whether this caller may see what the goods cost. Manager tier and above — see
        /// RbacRolePermissionMap. Cheap: the handler reads the caller's user row, which every
        /// authorized action on this controller has already read.
        /// </summary>
        private async Task<bool> CanReadCostAsync()
        {
            _canReadCost ??= (await _authorizationService.AuthorizeAsync(User, RbacPolicies.Products.ReadCost)).Succeeded;
            return _canReadCost.Value;
        }

        /// <param name="familyHasPicture">Known by a list caller, which looks every family up in one query; null asks for this product's.</param>
        private async Task<ProductDTO> ToProductDtoAsync(Product product, bool includePicture = true, bool? familyHasPicture = null)
        {
            ProductDTO dto = _mapper.Map<ProductDTO>(product);
            dto.HasPicture = !string.IsNullOrWhiteSpace(product.PictureAdress);
            dto.FamilyHasPicture = familyHasPicture
                ?? (product.VariantGroupId is { } group && product.EnterpriseId is { } enterprise &&
                    (await _productService.FamiliesWithPictureAsync(enterprise, [group])).Contains(group));
            if (familyHasPicture == null && product.StorefrontCategoryId is { } category)
                dto.StorefrontCategoryPath = (await _productService.GetStorefrontCategoryPathsAsync()).GetValueOrDefault(category);

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
