using System.Net;
using System.Reflection;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;
using EvangelionERPV2.EmployeeModule.Application.Services;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Exceptions;
using EvangelionERPV2.Shared.Repositories;
using EvangelionERPV2.Shared.Utils;
using Microsoft.Extensions.Configuration;
using Moq;
using Xunit;

namespace EvangelionERPV2.EmployeeModule.Test
{
    public class EmployeeServiceTests
    {
        private readonly Mock<IRepository<Employee>> _employeeRepository;
        private readonly EmployeeService _service;

        public EmployeeServiceTests()
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["AWSSettings:SecretName"] = "test-secret",
                    ["AWSSettings:BucketEmployeeName"] = "test-employee-bucket"
                })
                .Build();

            var secretsManager = new Mock<IAmazonSecretsManager>();
            secretsManager.Setup(s => s.GetSecretValueAsync(It.IsAny<GetSecretValueRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new GetSecretValueResponse
                {
                    SecretString = "{\"access-key-id\":\"test\",\"secret-access-key\":\"test\"}"
                });

            var kmsProvider = new AWSKMSKeyProvider(secretsManager.Object, configuration);
            _employeeRepository = new Mock<IRepository<Employee>>();
            _service = new EmployeeService(_employeeRepository.Object, configuration, kmsProvider);
        }

        private static Employee BuildEmployee(Guid? id = null)
        {
            return new Employee("Employee", "11999999999", "employee@test.com", "Analyst", 4500.50m, "12345678901")
            {
                Id = id ?? Guid.NewGuid()
            };
        }

        [Fact]
        public async Task CreateAsync_IgnoresIncomingId_GeneratesServerId()
        {
            var incomingId = Guid.NewGuid();
            var employee = BuildEmployee(incomingId);

            _employeeRepository
                .Setup(r => r.CreateAsync(It.IsAny<Employee>()))
                .ReturnsAsync((Employee entity) => entity);
            _employeeRepository
                .Setup(r => r.CommitAsync(It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            var result = await _service.CreateAsync(employee);

            Assert.NotEqual(incomingId, result.Id);
            _employeeRepository.Verify(r => r.CreateAsync(It.Is<Employee>(x => x.Id != incomingId)), Times.Once);
        }

        [Fact]
        public async Task CreateAsync_WhenValid_CreatesEmployee()
        {
            var employee = BuildEmployee();

            _employeeRepository.Setup(r => r.GetById(employee.Id)).Returns((Employee)null!);
            _employeeRepository.Setup(r => r.CreateAsync(It.IsAny<Employee>())).ReturnsAsync(employee);
            _employeeRepository.Setup(r => r.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

            var result = await _service.CreateAsync(employee);

            Assert.Same(employee, result);
            _employeeRepository.Verify(r => r.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task CreateAsync_WhenRepositoryFails_ThrowsInsertDatabaseException()
        {
            var employee = BuildEmployee();

            _employeeRepository
                .Setup(r => r.CreateAsync(It.IsAny<Employee>()))
                .ThrowsAsync(new InvalidOperationException("boom"));

            await Assert.ThrowsAsync<InsertDatabaseException>(() => _service.CreateAsync(employee));
        }

        [Fact]
        public void Update_WhenEmployeeMissing_ThrowsNotFoundDatabaseException()
        {
            var employee = BuildEmployee();

            _employeeRepository.Setup(r => r.GetById(employee.Id)).Returns((Employee)null!);

            Assert.Throws<NotFoundDatabaseException>(() => _service.Update(employee));
        }

        [Fact]
        public void Update_WhenValid_UpdatesEmployeeAndKeepsStoredPicture()
        {
            var employeeId = Guid.NewGuid();
            var stored = BuildEmployee(employeeId);
            stored.Picture = "employees/stored-key";

            // Distinct instance: asserting on the one handed in only proves Commit was called.
            var incoming = BuildEmployee(employeeId);
            incoming.Name = "Renamed";
            incoming.Picture = string.Empty;

            Employee? persisted = null;
            _employeeRepository.Setup(r => r.GetById(employeeId)).Returns(stored);
            _employeeRepository
                .Setup(r => r.Update(It.IsAny<Employee>()))
                .Returns((Employee entity) =>
                {
                    persisted = entity;
                    return entity;
                });

            var result = _service.Update(incoming);

            Assert.NotNull(persisted);
            Assert.Equal("Renamed", persisted!.Name);
            // An empty picture means "not provided": the stored S3 key must survive.
            Assert.Equal("employees/stored-key", persisted.Picture);
            Assert.NotEqual(DateTime.MinValue, result.UpdatedAt ?? DateTime.MinValue);
            _employeeRepository.Verify(r => r.Commit(), Times.Once);
        }

        [Fact]
        public void Delete_WhenEmployeeMissing_ThrowsNotFoundDatabaseException()
        {
            var employeeId = Guid.NewGuid();

            _employeeRepository.Setup(r => r.GetById(employeeId)).Returns((Employee)null!);

            Assert.Throws<NotFoundDatabaseException>(() => _service.Delete(employeeId));
        }

        [Fact]
        public void Delete_WhenValid_SetsInactive()
        {
            var employee = BuildEmployee();
            employee.IsActive = true;

            _employeeRepository.Setup(r => r.GetById(employee.Id)).Returns(employee);
            _employeeRepository.Setup(r => r.Update(It.IsAny<Employee>())).Returns(employee);

            var result = _service.Delete(employee.Id);
            var updatedAt = result.UpdatedAt ?? DateTime.MinValue;

            Assert.Same(employee, result);
            Assert.False(result.IsActive ?? true);
            Assert.NotEqual(DateTime.MinValue, updatedAt);
            _employeeRepository.Verify(r => r.Commit(), Times.Once);
        }

        [Fact]
        public async Task UpdatePictureAsync_WithEmptyPayload_ClearsPictureAndDeletesStoredObject()
        {
            EnsureEncryptionKeyInitialized();
            var s3Client = AttachS3Mock();

            var employeeId = Guid.NewGuid();
            var stored = BuildEmployee(employeeId);
            stored.Picture = "employees/old-key";
            var incoming = BuildEmployee(employeeId);

            _employeeRepository.Setup(r => r.GetById(employeeId)).Returns(stored);
            _employeeRepository.Setup(r => r.Update(It.IsAny<Employee>())).Returns((Employee entity) => entity);
            s3Client
                .Setup(s => s.DeleteObjectAsync(It.IsAny<DeleteObjectRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new DeleteObjectResponse());

            var result = await _service.UpdatePictureAsync(incoming, null);

            Assert.Equal(string.Empty, result.Picture);
            s3Client.Verify(
                s => s.DeleteObjectAsync(
                    It.Is<DeleteObjectRequest>(request => request.Key == "employees/old-key"),
                    It.IsAny<CancellationToken>()),
                Times.Once);
            s3Client.Verify(s => s.PutObjectAsync(It.IsAny<PutObjectRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task UpdatePictureAsync_WithPayload_UploadsAndReplacesTheOldObject()
        {
            EnsureEncryptionKeyInitialized();
            var s3Client = AttachS3Mock();

            var employeeId = Guid.NewGuid();
            var stored = BuildEmployee(employeeId);
            stored.Picture = "employees/old-key";
            var incoming = BuildEmployee(employeeId);

            _employeeRepository.Setup(r => r.GetById(employeeId)).Returns(stored);
            _employeeRepository.Setup(r => r.Update(It.IsAny<Employee>())).Returns((Employee entity) => entity);
            s3Client
                .Setup(s => s.PutObjectAsync(It.IsAny<PutObjectRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new PutObjectResponse());
            s3Client
                .Setup(s => s.DeleteObjectAsync(It.IsAny<DeleteObjectRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new DeleteObjectResponse());

            var payload = Convert.ToBase64String(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D });
            var result = await _service.UpdatePictureAsync(incoming, payload);

            Assert.StartsWith("employees/", SharedFunctions.EnsureDecryptedAddress(result.Picture));
            s3Client.Verify(s => s.PutObjectAsync(It.IsAny<PutObjectRequest>(), It.IsAny<CancellationToken>()), Times.Once);
            s3Client.Verify(
                s => s.DeleteObjectAsync(
                    It.Is<DeleteObjectRequest>(request => request.Key == "employees/old-key"),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Fact]
        public async Task UpdatePictureAsync_WhenEmployeeMissing_ThrowsNotFoundDatabaseException()
        {
            var employee = BuildEmployee();
            _employeeRepository.Setup(r => r.GetById(employee.Id)).Returns((Employee)null!);

            await Assert.ThrowsAsync<NotFoundDatabaseException>(() => _service.UpdatePictureAsync(employee, null));
        }

        [Fact]
        public async Task GetPictureBase64Async_WithoutAddress_ReturnsEmptyWithoutCallingS3()
        {
            var s3Client = AttachS3Mock();

            var result = await _service.GetPictureBase64Async(null);

            Assert.Equal(string.Empty, result);
            s3Client.Verify(s => s.GetObjectAsync(It.IsAny<GetObjectRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task GetPictureBase64Async_ReturnsStoredObjectAsBase64()
        {
            var s3Client = AttachS3Mock();
            var content = new byte[] { 1, 2, 3, 4 };

            s3Client
                .Setup(s => s.GetObjectAsync(It.IsAny<GetObjectRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => new GetObjectResponse { ResponseStream = new MemoryStream(content) });

            var result = await _service.GetPictureBase64Async("employees/some-key");

            Assert.Equal(Convert.ToBase64String(content), result);
        }

        [Fact]
        public async Task GetPictureBase64Async_WhenObjectIsMissing_ReturnsEmpty()
        {
            var s3Client = AttachS3Mock();

            s3Client
                .Setup(s => s.GetObjectAsync(It.IsAny<GetObjectRequest>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new AmazonS3Exception("missing") { StatusCode = HttpStatusCode.NotFound });

            var result = await _service.GetPictureBase64Async("employees/some-key");

            Assert.Equal(string.Empty, result);
        }

        private Mock<IAmazonS3> AttachS3Mock()
        {
            var s3Client = new Mock<IAmazonS3>();

            typeof(EmployeeService)
                .GetField("_s3Client", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(_service, s3Client.Object);

            return s3Client;
        }

        private static void EnsureEncryptionKeyInitialized()
        {
            var encryptionField = typeof(SharedFunctions)
                .GetField("_encryptionKey", BindingFlags.NonPublic | BindingFlags.Static);

            if (encryptionField == null)
                return;

            var currentValue = encryptionField.GetValue(null) as string;
            if (!string.IsNullOrWhiteSpace(currentValue))
                return;

            var keyBytes = Enumerable.Range(1, 32).Select(i => (byte)i).ToArray();
            encryptionField.SetValue(null, Convert.ToBase64String(keyBytes));
        }
    }
}
