using Amazon.S3;
using EvangelionERPV2.EmployeeModule.Application.Interface;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Exceptions;
using EvangelionERPV2.Shared.Utils;
using Microsoft.Extensions.Configuration;
using Serilog;
using System.Net;

namespace EvangelionERPV2.EmployeeModule.Application.Services
{
    public class EmployeeService : IEmployeeService<Employee>
    {
        private readonly EvangelionERPV2.Shared.Repositories.IRepository<Employee> _employeeRepository;
        private readonly IConfiguration _configuration;
        private readonly AWSKMSKeyProvider _kmsProvider;
        private IAmazonS3? _s3Client;

        public EmployeeService(EvangelionERPV2.Shared.Repositories.IRepository<Employee> employeeRepository,
            IConfiguration configuration,
            AWSKMSKeyProvider kmsProvider)
        {
            _employeeRepository = employeeRepository;
            _configuration = configuration;
            _kmsProvider = kmsProvider;
        }

        public async Task<Employee> CreateAsync(Employee employee, string? picturePayload = null)
        {
            try
            {
                employee.Id = Guid.NewGuid();
                employee.Picture = string.Empty;
                Employee includedEmployee = await _employeeRepository.CreateAsync(employee);
                await _employeeRepository.CommitAsync();

                if (string.IsNullOrWhiteSpace(picturePayload))
                    return includedEmployee;

                try
                {
                    return await UpdatePictureAsync(includedEmployee, picturePayload);
                }
                catch (Exception ex)
                {
                    Log.Logger.Warning(
                        ex,
                        "Employee picture upload failed after employee creation. EmployeeId={EmployeeId} ErrorType={ErrorType}",
                        includedEmployee.Id,
                        ex.GetType().Name);

                    throw new InsertDatabaseException("Employee picture upload failed during creation.", ex);
                }
            }
            catch (InsertDatabaseException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new InsertDatabaseException("Unexpected error while creating employee.", ex);
            }
        }

        public Employee Update(Employee employee)
        {
            // Generic update path: an empty Picture means "not provided", so the stored
            // value is preserved. Picture removal goes through UpdatePictureAsync with an
            // explicit clear signal.
            return Update(employee, clearPicture: false);
        }

        private Employee Update(Employee employee, bool clearPicture)
        {
            try
            {
                Employee existentEmployee = _employeeRepository.GetById(employee.Id);
                Employee updatedEmployee = new Employee();

                if (existentEmployee == null)
                    throw new NotFoundDatabaseException($"{nameof(Employee)} was not found in database.");

                if (clearPicture)
                    employee.Picture = string.Empty;
                else
                    employee.Picture = string.IsNullOrWhiteSpace(employee.Picture)
                        ? existentEmployee.Picture
                        : SharedFunctions.EnsureEncryptedAddress(employee.Picture);

                employee.UpdatedAt = DateTime.UtcNow;
                updatedEmployee = _employeeRepository.Update(employee);
                _employeeRepository.Commit();
                return updatedEmployee;
            }
            catch (NotFoundDatabaseException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new InsertDatabaseException("Unexpected error while updating employee.", ex);
            }
        }

        public Employee Delete(Guid id)
        {
            try
            {
                Employee employee = _employeeRepository.GetById(id);
                Employee deletedEmployee = new Employee();

                if (employee == null)
                    throw new NotFoundDatabaseException($"{nameof(Employee)} was not found in database.");

                employee.IsActive = false;
                employee.UpdatedAt = DateTime.UtcNow;
                deletedEmployee = _employeeRepository.Update(employee);
                _employeeRepository.Commit();
                return deletedEmployee;
            }
            catch (NotFoundDatabaseException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new InsertDatabaseException("Unexpected error while deleting employee.", ex);
            }
        }

        public async Task<Employee> UpdatePictureAsync(Employee employee, string? picturePayload)
        {
            if (employee == null)
                throw new ArgumentNullException(nameof(employee));

            Employee existentEmployee = _employeeRepository.GetById(employee.Id);
            if (existentEmployee == null)
                throw new NotFoundDatabaseException($"{nameof(Employee)} was not found in database.");

            var bucketName = SharedFunctions.GetEmployeeBucketName(_configuration);
            if (string.IsNullOrWhiteSpace(bucketName))
                throw new InvalidOperationException("AWS bucket name is not configured.");

            var s3Client = await GetS3ClientAsync();
            var oldPictureAddress = existentEmployee.Picture;

            if (string.IsNullOrWhiteSpace(picturePayload))
            {
                // Explicit clear signal: without it Update would treat the empty value as
                // "not provided" and keep the old key, leaving the database pointing at the
                // S3 object deleted below.
                employee.UpdatedAt = DateTime.UtcNow;
                var updatedEmployee = Update(employee, clearPicture: true);
                await TryDeleteOldPictureAsync(s3Client, bucketName, oldPictureAddress);
                return updatedEmployee;
            }

            var keyName = SharedFunctions.GenerateStorageObjectKey("employees", employee.Id);
            using var content = SharedFunctions.GetMemoryStreamFromBase64Payload(picturePayload);
            await s3Client.CreateItemAsync(bucketName, keyName, content);
            try
            {
                employee.Picture = SharedFunctions.EnsureEncryptedAddress(keyName);
                employee.UpdatedAt = DateTime.UtcNow;
                var updatedEmployee = Update(employee);
                await TryDeleteOldPictureAsync(s3Client, bucketName, oldPictureAddress);
                return updatedEmployee;
            }
            catch
            {
                await s3Client.DeleteItemIfExistsAsync(bucketName, keyName);
                throw;
            }
        }

        public async Task<string> GetPictureBase64Async(string? pictureAddress)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(pictureAddress))
                    return string.Empty;

                string bucketName = SharedFunctions.GetEmployeeBucketName(_configuration);
                if (string.IsNullOrWhiteSpace(bucketName))
                    throw new InvalidOperationException("AWS bucket name is not configured.");

                var s3Client = await GetS3ClientAsync();
                return await s3Client.GetItemBase64Async(bucketName, pictureAddress);
            }
            catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound || ex.StatusCode == HttpStatusCode.BadRequest)
            {
                Log.Logger.Warning("Employee picture not found in S3.");
                return string.Empty;
            }
            catch (Exception ex)
            {
                Log.Logger.Warning(
                    "Unable to load employee picture from S3. ErrorType={ErrorType}",
                    ex.GetType().Name);
                return string.Empty;
            }
        }

        private static async Task TryDeleteOldPictureAsync(IAmazonS3 s3Client, string bucketName, string? oldPictureAddress)
        {
            try
            {
                await s3Client.DeleteItemIfExistsAsync(bucketName, oldPictureAddress);
            }
            catch (Exception ex)
            {
                Log.Logger.Warning(
                    "Unable to delete old employee image from S3 after successful picture update. ErrorType={ErrorType}",
                    ex.GetType().Name);
            }
        }

        private async Task<IAmazonS3> GetS3ClientAsync()
        {
            if (_s3Client != null)
                return _s3Client;

            var awsCredentials = await _kmsProvider.GetAWSCredentialsAsync();
            var awsRegion = SharedFunctions.GetAWSRegion(_configuration);
            _s3Client = new AmazonS3Client(awsCredentials, awsRegion);
            return _s3Client;
        }
    }
}
