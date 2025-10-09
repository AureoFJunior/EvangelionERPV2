using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using AutoMapper;
using Serilog;
using EvangelionERPV2.ProductModule.Domain.Interface;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.ProductModule.Application.Interface;
using EvangelionERPV2.Shared.DTOs;
using EvangelionERPV2.Shared.Exceptions;
using EvangelionERPV2.Shared.Utils;
using System.Security.Claims;

namespace EvangelionERPV2.Web.Controllers
{
    [Authorize]
    [Route("api/v{version:apiVersion}/[controller]/[action]")]
    [ApiVersion("1.0")]
    public class ProductController : Controller
    {
        private readonly IProductRepository<Product> _productRepository;
        private readonly IProductService<Product> _productService;
        private readonly IRepository<Product> _repository;
        private readonly IMapper _mapper;

        public ProductController(IProductService<Product> productService,
            IRepository<Product> repository,
            IProductRepository<Product> productRepository,
            IMapper mapper)
        {
            _productService = productService;
            _repository = repository;
            _productRepository = productRepository;
            _mapper = mapper;
        }

        /// <summary>
        /// Return all the products (also works with pagination).
        /// </summary>
        /// <param name="pageNumber">Number of the current page</param>
        /// <param name="pageSize">Size of the desired page</param>
        /// <returns></returns>
        [HttpGet("{pageNumber?}/{pageSize?}")]
        [ProducesResponseType(typeof(IEnumerable<ProductDTO>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetProducts(int? pageNumber = null, int? pageSize = null)
        {

            if (!ModelState.IsValid) return BadRequest(ModelState);

            Guid enterpriseId = Guid.Parse(User.FindFirst(ClaimTypes.GroupSid)?.Value);

            IEnumerable<Product> products = await _repository.GetAllAsync(pageNumber, pageSize, x => x.EnterpriseId != null && (x.EnterpriseId != default(Guid) && x.EnterpriseId == enterpriseId));
            if (products == null)
                return NoContent();

            IEnumerable<ProductDTO> productDTO = _mapper.Map<IEnumerable<ProductDTO>>(products);
            return Ok(productDTO);
        }

        /// <summary>
        /// Return all the products that matches the filter (also works with pagination).
        /// </summary>
        /// <param name="descending">Order by type.</param>
        /// <param name="pageNumber">Number of the current page</param>
        /// <param name="pageSize">Size of the desired page</param>
        /// <param name="product">Object used to filter data.</param>
        /// <returns></returns>
        [HttpPost("{descending}/{pageNumber?}/{pageSize?}")]
        [ProducesResponseType(typeof(IEnumerable<ProductDTO>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetProductsByFilter([FromBody] Product product, bool descending, int? pageNumber = null, int? pageSize = null)
        {
            try
            {
                product.EnterpriseId = Guid.Parse(User.FindFirst(ClaimTypes.GroupSid)?.Value);

                (IEnumerable<Product> products, int totalItems) = await _productRepository.GetAllAsyncFiltering(descending, pageNumber, pageSize, product);
                if (products == null)
                    return NoContent();

                IEnumerable<ProductDTO> productDTO = _mapper.Map<IEnumerable<ProductDTO>>(products);
                return Ok(productDTO.ToPaginatedResult(pageNumber ?? 1, pageSize ?? 1, totalItems));
            }
            catch (NotFoundDatabaseException exnf)
            {
                Log.Logger.Error("Products not found", exnf);
                return NoContent();
            }
            catch (Exception ex)
            {
                Log.Logger.Error("Error when getting Products", ex);
                return Problem(ex.Message);
            }
        }

        /// <summary>
        /// Get a product.
        /// </summary>
        /// <param name="id">Id of the product</param>
        /// <returns>The product that match with the id parameter.</returns>
        [HttpGet("{id}")]
        [ProducesResponseType(typeof(ProductDTO), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetProduct(Guid id)
        {
            try
            {
                Product product = await _repository.GetByIdAsync(id);
                if (product == null)
                    return NoContent();

                ProductDTO productDTO = _mapper.Map<ProductDTO>(product);
                return Ok(productDTO);
            }
            catch (Exception ex)
            {
                Log.Logger.Error($"Error when getting Product ID {id}", ex);
                return Problem(ex.Message);
            }
        }

        /// <summary>
        /// Add a new product
        /// </summary>
        /// <param name="product">Product to be added</param>
        /// <returns>The added product</returns>
        [HttpPost]
        [ProducesResponseType(typeof(ProductDTO), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> AddProduct([FromBody] ProductPicture product)
        {
            try
            {
                if (!ModelState.IsValid) return BadRequest(ModelState);

                Product createdProduct = await _productService.CreateAsync(product);
                return Ok(_mapper.Map<ProductDTO>(createdProduct));
            }
            catch (Exception ex)
            {
                Log.Logger.Error($"Error when creating Product: {product.Product.Name}", ex);
                return Problem(ex.Message);
            }
        }

        /// <summary>
        /// Update an product
        /// </summary>
        /// <param name="product">Product to be updated</param>
        /// <returns>The updated product</returns>
        [HttpPut]
        [ProducesResponseType(typeof(ProductDTO), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> UpdateProduct([FromBody] Product product)
        {
            try
            {
                if (!ModelState.IsValid) return BadRequest(ModelState);

                Product updatedProduct = await _productService.UpdateAsync(product);

                if (updatedProduct == null)
                    return NoContent();

                return Ok(_mapper.Map<ProductDTO>(product));
            }
            catch (Exception ex)
            {
                Log.Logger.Error($"Error when updating Product: {product.Name}", ex);
                return Problem(ex.Message);
            }
        }

        /// <summary>
        /// Delete an product (soft delete)
        /// </summary>
        /// <param name="id">Product's Id</param>
        /// <returns>The deleted product</returns>
        [HttpDelete]
        [ProducesResponseType(typeof(ProductDTO), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> DeleteProduct(Guid id)
        {
            try
            {
                if (!ModelState.IsValid) return BadRequest(ModelState);

                Product product = _productService.Delete(id);
                if (product == null)
                    return NoContent();

                return Ok(product);
            }
            catch (Exception ex)
            {
                Log.Logger.Error($"Error when deleting Product ID {id}", ex);
                return Problem(ex.Message);
            }
        }

        /// <summary>
        /// Upload an Product's picture
        /// </summary>
        /// <param name="product">Product to be updated</param>
        /// <returns>The updated product</returns>
        [HttpPatch]
        [ProducesResponseType(typeof(ProductDTO), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> UploadPicture([FromBody] ProductPicture productPicture)
        {
            try
            {
                if (!ModelState.IsValid) return BadRequest(ModelState);

                Product updatedProduct = await _productService.UpdatePictureAsync(productPicture);

                if (updatedProduct == null)
                    return NoContent();

                return Ok(updatedProduct);
            }
            catch (Exception ex)
            {
                Log.Logger.Error($"Error when updating Product: {productPicture.Product.Name}", ex);
                return Problem(ex.Message);
            }
        }
    }
}
