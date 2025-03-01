using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using AutoMapper;
using Serilog;
using EvangelionERPV2.EnterpriseModule.Domain.Interface;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.EnterpriseModule.Application.Interface;
using EvangelionERPV2.Shared.DTOs;
using EvangelionERPV2.Shared.Exceptions;

namespace EvangelionERPV2.Web.Controllers
{
    [Authorize]
    [Route("api/v{version:apiVersion}/[controller]/[action]")]
    [ApiVersion("1.0")]
    public class EnterpriseController : Controller
    {
        private readonly IEnterpriseRepository<Enterprise> _enterpriseRepository;
        private readonly IEnterpriseService<Enterprise> _enterpriseService;
        private readonly IRepository<Enterprise> _repository;
        private readonly IMapper _mapper;

        public EnterpriseController(IEnterpriseService<Enterprise> enterpriseService,
            IRepository<Enterprise> repository,
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

                IEnumerable<Enterprise> enterprises = await _repository.GetAllAsync(pageNumber, pageSize);
                if (enterprises == null)
                    return NoContent();

                IEnumerable<EnterpriseDTO> enterpriseDTO = _mapper.Map<IEnumerable<EnterpriseDTO>>(enterprises);
                return Ok(enterpriseDTO);
            }
            catch (NotFoundDatabaseException exnf)
            {
                Log.Logger.Error("Enterprises not found", exnf);
                return NoContent();
            }
            catch (Exception ex)
            {
                Log.Logger.Error("Error when getting Enterprises", ex);
                return Problem(ex.Message);
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
        [ProducesResponseType(typeof(IEnumerable<EnterpriseDTO>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetEnterprisesByFilter([FromBody] Enterprise enterprise, bool descending, int? pageNumber = null, int? pageSize = null)
        {
            try
            {
                IEnumerable<Enterprise> enterprises = await _enterpriseRepository.GetAllAsyncFiltering(descending, pageNumber, pageSize, enterprise);
                if (enterprises == null)
                    return NoContent();

                IEnumerable<EnterpriseDTO> enterpriseDTO = _mapper.Map<IEnumerable<EnterpriseDTO>>(enterprises);
                return Ok(enterpriseDTO);
            }
            catch (NotFoundDatabaseException exnf)
            {
                Log.Logger.Error("Enterprises not found", exnf);
                return NoContent();
            }
            catch (Exception ex)
            {
                Log.Logger.Error("Error when getting Enterprises", ex);
                return Problem(ex.Message);
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
            catch (Exception ex)
            {
                Log.Logger.Error($"Error when getting Enterprise ID {id}", ex);
                return Problem(ex.Message);
            }
        }

        /// <summary>
        /// Add a new enterprise
        /// </summary>
        /// <param name="enterprise">Enterprise to be added</param>
        /// <returns>The added enterprise</returns>
        [HttpPost]
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
            catch (Exception ex)
            {
                Log.Logger.Error($"Error when creating Enterprise: {enterprise.Name}", ex);
                return Problem(ex.Message);
            }
        }

        /// <summary>
        /// Update an enterprise
        /// </summary>
        /// <param name="enterprise">Enterprise to be updated</param>
        /// <returns>The updated enterprise</returns>
        [HttpPut]
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
            catch (Exception ex)
            {
                Log.Logger.Error($"Error when updating Enterprise: {enterprise.Name}", ex);
                return Problem(ex.Message);
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
            catch (Exception ex)
            {
                Log.Logger.Error($"Error when deleting Enterprise ID {id}", ex);
                return Problem(ex.Message);
            }
        }
    }
}
