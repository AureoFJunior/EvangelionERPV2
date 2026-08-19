using System.Security.Claims;
using AutoMapper;
using EvangelionERPV2.EmployeeModule.Application.Interface;
using EvangelionERPV2.EmployeeModule.Domain.Interface;
using EvangelionERPV2.Shared.DTOs;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Exceptions;
using EvangelionERPV2.Shared.Repositories;
using EvangelionERPV2.Shared.Utils;
using EvangelionERPV2.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Serilog;
using System.Linq;

namespace EvangelionERPV2.Web.Controllers
{
    [Authorize]
    [Route("api/v{version:apiVersion}/[controller]/[action]")]
    [ApiVersion("1.0")]
    public class EmployeeController : Controller
    {
        private const int MaxPageSize = 200;
        private const int MaxPageSizeWithPictures = 50;
        private const int MaxEmployeeWriteRequestBodySizeInBytes = 64 * 1024;
        private const int MaxImageUploadRequestBodySizeInBytes = 8 * 1024 * 1024;

        private readonly IEmployeeService<Employee> _employeeService;
        private readonly EvangelionERPV2.Shared.Repositories.IRepository<Employee> _repository;
        private readonly IEmployeeRepository<Employee> _employeeRepository;
        private readonly IRepository<User> _userRepository;
        private readonly IMapper _mapper;

        public EmployeeController(IEmployeeService<Employee> employeeService,
            EvangelionERPV2.Shared.Repositories.IRepository<Employee> repository,
            IEmployeeRepository<Employee> employeeRepository,
            IRepository<User> userRepository,
            IMapper mapper)
        {
            _employeeService = employeeService;
            _repository = repository;
            _employeeRepository = employeeRepository;
            _userRepository = userRepository;
            _mapper = mapper;
        }

        /// <summary>
        /// Return all the employees (also works with pagination).
        /// </summary>
        /// <param name="pageNumber">Number of the current page</param>
        /// <param name="pageSize">Size of the desired page</param>
        /// <returns></returns>
        [Authorize(Policy = "rbac:" + RbacPermissions.Employees.Read)]
        [HttpGet("{pageNumber?}/{pageSize?}")]
        [ProducesResponseType(typeof(IEnumerable<EmployeeDTO>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetEmployees(int? pageNumber = null, int? pageSize = null, [FromQuery] bool includePictures = false)
        {
            try
            {
                if (!ModelState.IsValid) return BadRequest(ControllerResponseSanitizer.InvalidRequestPayloadMessage);

                if (!TryGetEnterpriseId(out var enterpriseId))
                    return Unauthorized();
                var (normalizedPageNumber, normalizedPageSize) = PaginationExtensions.NormalizePagination(pageNumber, pageSize, MaxPageSize);
                if (includePictures && normalizedPageSize > MaxPageSizeWithPictures)
                    normalizedPageSize = MaxPageSizeWithPictures;
                IEnumerable<Employee> employees = await _repository.GetAllAsync(
                    normalizedPageNumber,
                    normalizedPageSize,
                    x => x.IsActive == true && x.EnterpriseId != null && (x.EnterpriseId != default(Guid) && x.EnterpriseId == enterpriseId));
                if (employees == null)
                    return NoContent();

                return Ok(await ToEmployeeDtosAsync(employees, includePictures));
            }
            catch (NotFoundDatabaseException exnf)
            {
                Log.Logger.Error("Employees not found. ErrorType={ErrorType}", exnf.GetType().Name);
                return NoContent();
            }
            catch (Exception)
            {
                throw;
            }
        }

        /// <summary>
        /// Return all the employees that matches the filter (also works with pagination).
        /// </summary>
        /// <param name="descending">Order by type.</param>
        /// <param name="pageNumber">Number of the current page</param>
        /// <param name="pageSize">Size of the desired page</param>
        /// <param name="filter">Object used to filter data.</param>
        /// <returns></returns>
        [Authorize(Policy = "rbac:" + RbacPermissions.Employees.Read)]
        [HttpPost("{descending}/{pageNumber?}/{pageSize?}")]
        [RequestSizeLimit(MaxEmployeeWriteRequestBodySizeInBytes)]
        [ProducesResponseType(typeof(IEnumerable<EmployeeDTO>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetEmployeesByFilter([FromBody] EmployeeFilterRequestDTO? filter, bool descending, int? pageNumber = null, int? pageSize = null, [FromQuery] bool includePictures = false)
        {
            try
            {
                if (!ModelState.IsValid)
                    return BadRequest(ControllerResponseSanitizer.InvalidRequestPayloadMessage);

                if (!TryGetEnterpriseId(out var enterpriseId))
                    return Unauthorized();
                filter ??= new EmployeeFilterRequestDTO();
                var nameFilter = filter.Name?.Trim();
                var emailFilter = filter.Email?.Trim();
                var positionFilter = filter.Position?.Trim();
                var phoneFilter = filter.PhoneNumber?.Trim();
                var isActiveFilter = filter.IsActive;
                var (normalizedPageNumber, normalizedPageSize) = PaginationExtensions.NormalizePagination(pageNumber, pageSize, MaxPageSize);
                if (includePictures && normalizedPageSize > MaxPageSizeWithPictures)
                    normalizedPageSize = MaxPageSizeWithPictures;

                // Filtering lives in the repository: it builds a translatable EF.Functions.Like
                // predicate. String.Contains with a StringComparison cannot be translated to SQL.
                var employees = await _employeeRepository.GetAllAsyncFiltering(
                    descending,
                    normalizedPageNumber,
                    normalizedPageSize,
                    new Employee
                    {
                        Name = nameFilter ?? string.Empty,
                        Email = emailFilter ?? string.Empty,
                        Position = positionFilter ?? string.Empty,
                        PhoneNumber = phoneFilter ?? string.Empty,
                        EnterpriseId = enterpriseId
                    },
                    isActiveFilter);

                if (employees == null || !employees.Any())
                    return NoContent();

                return Ok(await ToEmployeeDtosAsync(employees, includePictures));
            }
            catch (NotFoundDatabaseException exnf)
            {
                Log.Logger.Error("Employees not found. ErrorType={ErrorType}", exnf.GetType().Name);
                return NoContent();
            }
            catch (Exception)
            {
                throw;
            }
        }

        /// <summary>
        /// Get an employee.
        /// </summary>
        /// <param name="id">Id of the employee</param>
        /// <returns>The employee that match with the id parameter.</returns>
        [Authorize(Policy = "rbac:" + RbacPermissions.Employees.Read)]
        [HttpGet("{id}")]
        [ProducesResponseType(typeof(EmployeeDTO), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetEmployee(Guid id)
        {
            try
            {
                if (!TryGetEnterpriseId(out var enterpriseId))
                    return Unauthorized();
                Employee employee = await _repository.GetByIdAsync(id);
                if (employee == null || employee.EnterpriseId != enterpriseId || employee.IsActive != true)
                    return NoContent();

                return Ok(await ToEmployeeDtoAsync(employee));
            }
            catch (Exception)
            {
                throw;
            }
        }

        /// <summary>
        /// Add a new employee
        /// </summary>
        /// <param name="request">Employee to be added</param>
        /// <returns>The added employee</returns>
        [Authorize(Policy = "rbac:" + RbacPermissions.Employees.Create)]
        [HttpPost]
        [RequestSizeLimit(MaxImageUploadRequestBodySizeInBytes)]
        [ProducesResponseType(typeof(EmployeeDTO), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> AddEmployee([FromBody] CreateEmployeeRequestDTO request)
        {
            try
            {
                if (!ModelState.IsValid) return BadRequest(ControllerResponseSanitizer.InvalidRequestPayloadMessage);
                if (request == null) return BadRequest();
                if (!TryGetEnterpriseId(out var enterpriseId))
                    return Unauthorized();
                if (!await HasActiveTenantMembershipAsync(TryGetUserId(), enterpriseId))
                    return Unauthorized();

                var employee = MapCreateEmployeeRequest(request, enterpriseId);
                Employee createdEmployee = await _employeeService.CreateAsync(employee, request.Picture);
                return Ok(await ToEmployeeDtoAsync(createdEmployee));
            }
            catch (Exception)
            {
                throw;
            }
        }

        /// <summary>
        /// Update an employee
        /// </summary>
        /// <param name="request">Employee to be updated</param>
        /// <returns>The updated employee</returns>
        [Authorize(Policy = "rbac:" + RbacPermissions.Employees.Update)]
        [HttpPut]
        [RequestSizeLimit(MaxEmployeeWriteRequestBodySizeInBytes)]
        [ProducesResponseType(typeof(EmployeeDTO), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> UpdateEmployee([FromBody] UpdateEmployeeRequestDTO request)
        {
            try
            {
                if (!ModelState.IsValid) return BadRequest(ControllerResponseSanitizer.InvalidRequestPayloadMessage);
                if (request == null) return BadRequest();
                if (!TryGetEnterpriseId(out var enterpriseId))
                    return Unauthorized();
                var existingEmployee = await _repository.GetByIdAsync(request.Id);
                if (existingEmployee == null || existingEmployee.EnterpriseId != enterpriseId || existingEmployee.IsActive != true)
                    return NoContent();

                existingEmployee.Name = request.Name.Trim();
                existingEmployee.Email = request.Email.Trim();
                existingEmployee.PhoneNumber = request.PhoneNumber.Trim();
                existingEmployee.Position = request.Position.Trim();
                // Sensitive fields (LGPD): only overwrite when the caller sent a value, so a
                // partial update payload does not wipe stored document/salary.
                if (!string.IsNullOrWhiteSpace(request.Document))
                    existingEmployee.Document = request.Document.Trim();
                if (request.Salary.HasValue)
                    existingEmployee.Salary = request.Salary;
                if (request.HireDate.HasValue)
                    existingEmployee.HireDate = request.HireDate.Value;
                // Guarded like the other sensitive fields: an absent termination date means "not
                // provided", never "reinstate". The validator only compares it to the payload's
                // hire date, so it is re-checked here against the stored one.
                if (request.TerminationDate.HasValue)
                {
                    if (request.TerminationDate.Value.Date < existingEmployee.HireDate.Date)
                        return BadRequest("Employee termination date cannot be before the hire date.");

                    existingEmployee.TerminationDate = request.TerminationDate;
                }
                existingEmployee.IsActive = request.IsActive;
                existingEmployee.UpdatedAt = DateTime.UtcNow;

                Employee updatedEmployee = _employeeService.Update(existingEmployee);

                if (updatedEmployee == null)
                    return NoContent();

                return Ok(await ToEmployeeDtoAsync(updatedEmployee));
            }
            catch (Exception)
            {
                throw;
            }
        }

        /// <summary>
        /// Delete an employee (soft delete)
        /// </summary>
        /// <param name="id">Employee's Id</param>
        /// <returns>The deleted employee</returns>
        [Authorize(Policy = "rbac:" + RbacPermissions.Employees.Delete)]
        [HttpDelete("{id}")]
        [ProducesResponseType(typeof(EmployeeDTO), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> DeleteEmployee(Guid id)
        {
            try
            {
                if (!ModelState.IsValid) return BadRequest(ControllerResponseSanitizer.InvalidRequestPayloadMessage);
                if (!TryGetEnterpriseId(out var enterpriseId))
                    return Unauthorized();
                var employee = await _repository.GetByIdAsync(id);
                if (employee == null || employee.EnterpriseId != enterpriseId || employee.IsActive != true)
                    return NoContent();

                Employee deletedEmployee = _employeeService.Delete(id);
                if (deletedEmployee == null)
                    return NoContent();

                return Ok(await ToEmployeeDtoAsync(deletedEmployee, includePicture: false));
            }
            catch (Exception)
            {
                throw;
            }
        }

        /// <summary>
        /// Upload an employee's picture. An empty file clears the stored picture.
        /// </summary>
        /// <param name="request">Employee id and base64 image payload</param>
        /// <returns>The updated employee</returns>
        [Authorize(Policy = "rbac:" + RbacPermissions.Employees.UploadPicture)]
        [HttpPatch]
        [RequestSizeLimit(MaxImageUploadRequestBodySizeInBytes)]
        [ProducesResponseType(typeof(EmployeeDTO), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> UploadPicture([FromBody] UploadEmployeePictureRequestDTO request)
        {
            try
            {
                if (!ModelState.IsValid) return BadRequest(ControllerResponseSanitizer.InvalidRequestPayloadMessage);
                if (request == null || request.EmployeeId == Guid.Empty)
                    return BadRequest("Employee id is required.");
                if (!TryGetEnterpriseId(out var enterpriseId))
                    return Unauthorized();
                var existingEmployee = await _repository.GetByIdAsync(request.EmployeeId);
                if (existingEmployee == null || existingEmployee.EnterpriseId != enterpriseId || existingEmployee.IsActive != true)
                    return NoContent();

                Employee updatedEmployee = await _employeeService.UpdatePictureAsync(existingEmployee, request.File);

                if (updatedEmployee == null)
                    return NoContent();

                return Ok(await ToEmployeeDtoAsync(updatedEmployee));
            }
            catch (Exception)
            {
                throw;
            }
        }

        private async Task<IEnumerable<EmployeeDTO>> ToEmployeeDtosAsync(IEnumerable<Employee> employees, bool includePictures = false)
        {
            // Picture hydration is one S3 round-trip each: sequentially that is seconds of latency
            // for a full page. GetPictureBase64Async swallows its own failures, so a missing
            // object still only blanks that one picture.
            return await Task.WhenAll(employees.Select(employee => ToEmployeeDtoAsync(employee, includePictures)));
        }

        private async Task<EmployeeDTO> ToEmployeeDtoAsync(Employee employee, bool includePicture = true)
        {
            var dto = _mapper.Map<EmployeeDTO>(employee);
            if (!includePicture || string.IsNullOrWhiteSpace(employee.Picture))
            {
                dto.Picture = string.Empty;
                return dto;
            }

            dto.Picture = await _employeeService.GetPictureBase64Async(employee.Picture);
            return dto;
        }

        private static Employee MapCreateEmployeeRequest(CreateEmployeeRequestDTO request, Guid enterpriseId)
        {
            return new Employee
            {
                Name = request.Name.Trim(),
                Email = request.Email.Trim(),
                PhoneNumber = request.PhoneNumber.Trim(),
                Position = request.Position.Trim(),
                Document = request.Document?.Trim(),
                Salary = request.Salary,
                HireDate = request.HireDate ?? DateTime.UtcNow,
                EnterpriseId = enterpriseId,
                IsActive = request.IsActive ?? true
            };
        }

        private Guid? TryGetUserId()
        {
            var claimValue = User.FindFirst(ClaimTypes.Sid)?.Value
                             ?? User.FindFirst("uid")?.Value;

            if (Guid.TryParse(claimValue, out var userId) && userId != Guid.Empty)
                return userId;

            return null;
        }


        private async Task<bool> HasActiveTenantMembershipAsync(Guid? userId, Guid enterpriseId)
        {
            if (!userId.HasValue || enterpriseId == Guid.Empty)
                return false;

            var user = await _userRepository.GetByIdAsync(userId.Value);
            return user != null && user.IsActive == true && user.EnterpriseId == enterpriseId;
        }


        private bool TryGetEnterpriseId(out Guid enterpriseId)
        {
            var claimValue = User.FindFirst(ClaimTypes.GroupSid)?.Value;
            return Guid.TryParse(claimValue, out enterpriseId) && enterpriseId != Guid.Empty;
        }
    }
}
