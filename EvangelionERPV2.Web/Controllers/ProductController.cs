using Microsoft.AspNetCore.Mvc;
using EvangelionERPV2.Web.Security;
using Microsoft.AspNetCore.Authorization;
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

        public ProductController(IProductService<Product> productService,
            EvangelionERPV2.Shared.Repositories.IRepository<Product> repository,
            IRepository<User> userRepository,
            IMapper mapper)
        {
            _productService = productService;
            _repository = repository;
            _userRepository = userRepository;
            _mapper = mapper;
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

                var products = await _repository.GetAllAsync(
                    normalizedPageNumber,
                    normalizedPageSize,
                    x =>
                        x.EnterpriseId == enterpriseId &&
                        (!isActiveFilter.HasValue || x.IsActive == isActiveFilter.Value) &&
                        (string.IsNullOrWhiteSpace(nameFilter) || x.Name.Contains(nameFilter, StringComparison.OrdinalIgnoreCase)));

                if (products == null || !products.Any())
                    return NoContent();

                IEnumerable<ProductDTO> productDTO = await ToProductDtosAsync(products, includePictures);
                return Ok(productDTO);
            }
            catch (NotFoundDatabaseException exnf)
            {
                Log.Logger.Error("Products not found. ErrorType={ErrorType}", exnf.GetType().Name);
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
                        StorageQuantity = normalizedRequest.StorageQuantity.GetValueOrDefault(),
                        UnitOfMeasure = normalizedRequest.UnitOfMeasure?.Trim().ToUpperInvariant() ?? string.Empty,
                        IsExternal = normalizedRequest.IsExternal.GetValueOrDefault(),
                        IsService = normalizedRequest.IsService.GetValueOrDefault(),
                        PictureAdress = string.Empty,
                        EnterpriseId = enterpriseId.Value,
                        IsActive = true
                    },
                    File = normalizedRequest.File ?? string.Empty
                };

                Product createdProduct = await _productService.CreateAsync(product);
                if (createdProduct == null)
                    return NoContent();

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
                existentProduct.StorageQuantity = request.StorageQuantity;
                existentProduct.UnitOfMeasure = request.UnitOfMeasure?.Trim().ToUpperInvariant() ?? string.Empty;
                existentProduct.IsExternal = request.IsExternal;
                existentProduct.IsService = request.IsService;
                if (!string.IsNullOrWhiteSpace(request.PictureAdress))
                    return BadRequest("PictureAdress cannot be updated here. Use UploadPicture.");
                existentProduct.IsActive = request.IsActive ?? existentProduct.IsActive;
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
                File = request.File
            };
        }

        private static string ResolveLegacyOrRootValue(string? rootValue, string? legacyValue)
        {
            if (!string.IsNullOrWhiteSpace(rootValue))
                return rootValue;

            return legacyValue ?? string.Empty;
        }

        private static double ResolveLegacyOrRootNumericValue(double? rootValue, double legacyValue)
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

        private async Task<ProductDTO> ToProductDtoAsync(Product product, bool includePicture = true)
        {
            ProductDTO dto = _mapper.Map<ProductDTO>(product);
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
