using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using AutoMapper;
using Serilog;
using EvangelionERPV2.EnterpriseModule.Domain.Interface;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.EnterpriseModule.Application.Interface;
using EvangelionERPV2.Shared.DTOs;
using EvangelionERPV2.Shared.Exceptions;
using EvangelionERPV2.Shared.Utils;

namespace EvangelionERPV2.Web.Controllers
{
    [Authorize]
    [Route("api/v{version:apiVersion}/[controller]/[action]")]
    [ApiVersion("1.0")]
    public class EnterpriseController : Controller
    {
        private const int DefaultPageNumber = 1;
        private const int DefaultPageSize = 50;
        private const int MaxPageSize = 200;
        private const int MaxEnterpriseWriteRequestBodySizeInBytes = 64 * 1024;

        private readonly IEnterpriseRepository<Enterprise> _enterpriseRepository;
        private readonly IEnterpriseService<Enterprise> _enterpriseService;
        private readonly EvangelionERPV2.Shared.Repositories.IRepository<Enterprise> _repository;
        private readonly IMapper _mapper;

        public EnterpriseController(IEnterpriseService<Enterprise> enterpriseService,
            EvangelionERPV2.Shared.Repositories.IRepository<Enterprise> repository,
            IEnterpriseRepository<Enterprise> enterpriseRepository,
            IMapper mapper)
        {
            _enterpriseService = enterpriseService;
            _repository = repository;
            _enterpriseRepository = enterpriseRepository;
            _mapper = mapper;
        }

        /// <summary>
        /// Return all the enterprises (also works with pagination).
        /// </summary>
        /// <param name="pageNumber">Number of the current page</param>
        /// <param name="pageSize">Size of the desired page</param>
        /// <returns></returns>
        [HttpGet("{pageNumber?}/{pageSize?}")]
        [ProducesResponseType(typeof(IEnumerable<EnterpriseDTO>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetEnterprises(int? pageNumber = null, int? pageSize = null)
        {
            try
            {
                if (!ModelState.IsValid) return BadRequest(ModelState);
                var (normalizedPageNumber, normalizedPageSize) = PaginationExtensions.NormalizePagination(pageNumber, pageSize, MaxPageSize);

                IEnumerable<Enterprise> enterprises = await _repository.GetAllAsync(normalizedPageNumber, normalizedPageSize);
                if (enterprises == null)
                    return NoContent();

                IEnumerable<EnterpriseDTO> enterpriseDTO = _mapper.Map<IEnumerable<EnterpriseDTO>>(enterprises);
                return Ok(enterpriseDTO);
            }
            catch (NotFoundDatabaseException exnf)
            {
                Log.Logger.Error("Enterprises not found. ErrorType={ErrorType}", exnf.GetType().Name);
                return NoContent();
            }
            catch (Exception)
            {
                throw;
            }
        }

        /// <summary>
        /// Return all the enterprises that matches the filter (also works with pagination).
        /// </summary>
        /// <param name="descending">Order by type.</param>
        /// <param name="pageNumber">Number of the current page</param>
        /// <param name="pageSize">Size of the desired page</param>
        /// <param name="enterprise">Object used to filter data.</param>
        /// <returns></returns>
        [HttpPost("{descending}/{pageNumber?}/{pageSize?}")]
        [RequestSizeLimit(MaxEnterpriseWriteRequestBodySizeInBytes)]
        [ProducesResponseType(typeof(IEnumerable<EnterpriseDTO>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetEnterprisesByFilter([FromBody] Enterprise enterprise, bool descending, int? pageNumber = null, int? pageSize = null)
        {
            try
            {
                var (normalizedPageNumber, normalizedPageSize) = PaginationExtensions.NormalizePagination(pageNumber, pageSize, MaxPageSize);
                IEnumerable<Enterprise> enterprises = await _enterpriseRepository.GetAllAsyncFiltering(
                    descending,
                    normalizedPageNumber,
                    normalizedPageSize,
                    enterprise);
                if (enterprises == null)
                    return NoContent();

                IEnumerable<EnterpriseDTO> enterpriseDTO = _mapper.Map<IEnumerable<EnterpriseDTO>>(enterprises);
                return Ok(enterpriseDTO);
            }
            catch (NotFoundDatabaseException exnf)
            {
                Log.Logger.Error("Enterprises not found. ErrorType={ErrorType}", exnf.GetType().Name);
                return NoContent();
            }
            catch (Exception)
            {
                throw;
            }
        }

        /// <summary>
        /// Get a enterprise.
        /// </summary>
        /// <param name="id">Id of the enterprise</param>
        /// <returns>The enterprise that match with the id parameter.</returns>
        [HttpGet("{id}")]
        [ProducesResponseType(typeof(EnterpriseDTO), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetEnterprise(Guid id)
        {
            try
            {
                Enterprise enterprise = await _repository.GetByIdAsync(id);
                if (enterprise == null)
                    return NoContent();

                EnterpriseDTO enterpriseDTO = _mapper.Map<EnterpriseDTO>(enterprise);
                return Ok(enterpriseDTO);
            }
            catch (Exception)
            {
                throw;
            }
        }

        /// <summary>
        /// Add a new enterprise
        /// </summary>
        /// <param name="enterprise">Enterprise to be added</param>
        /// <returns>The added enterprise</returns>
        [HttpPost]
        [RequestSizeLimit(MaxEnterpriseWriteRequestBodySizeInBytes)]
        [ProducesResponseType(typeof(EnterpriseDTO), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> AddEnterprise([FromBody] Enterprise enterprise)
        {
            try
            {
                if (!ModelState.IsValid) return BadRequest(ModelState);

                Enterprise createdEnterprise = await _enterpriseService.CreateAsync(enterprise);
                return Ok(createdEnterprise);
            }
            catch (Exception)
            {
                throw;
            }
        }

        /// <summary>
        /// Update an enterprise
        /// </summary>
        /// <param name="enterprise">Enterprise to be updated</param>
        /// <returns>The updated enterprise</returns>
        [HttpPut]
        [RequestSizeLimit(MaxEnterpriseWriteRequestBodySizeInBytes)]
        [ProducesResponseType(typeof(EnterpriseDTO), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> UpdateEnterprise([FromBody] Enterprise enterprise)
        {
            try
            {
                if (!ModelState.IsValid) return BadRequest(ModelState);

                Enterprise updatedEnterprise = _enterpriseService.Update(enterprise);

                if (updatedEnterprise == null)
                    return NoContent();

                return Ok(enterprise);
            }
            catch (Exception)
            {
                throw;
            }
        }

        /// <summary>
        /// Delete an enterprise (soft delete)
        /// </summary>
        /// <param name="id">Enterprise's Id</param>
        /// <returns>The deleted enterprise</returns>
        [HttpDelete]
        [ProducesResponseType(typeof(EnterpriseDTO), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> DeleteEnterprise(Guid id)
        {
            try
            {
                if (!ModelState.IsValid) return BadRequest(ModelState);

                Enterprise enterprise = _enterpriseService.Delete(id);
                if (enterprise == null)
                    return NoContent();

                return Ok(enterprise);
            }
            catch (Exception)
            {
                throw;
            }
        }
    }
}
