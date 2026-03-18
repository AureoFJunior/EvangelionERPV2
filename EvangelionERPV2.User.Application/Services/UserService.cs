using Amazon;
using Amazon.S3;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Enums;
using EvangelionERPV2.Shared.Exceptions;
using EvangelionERPV2.Shared.Utils;
using EvangelionERPV2.UserModule.Application.Interface;
using Google.Apis.Auth;
using Microsoft.Extensions.Configuration;
using Serilog;
using System.Net;

namespace EvangelionERPV2.UserModule.Application.Services
{
    public class UserService : IUserService<User>
    {
        private readonly EvangelionERPV2.Shared.Repositories.IRepository<User> _userRepository;
        private readonly IConfiguration _configuration;
        private readonly AWSKMSKeyProvider _kmsProvider;
        private IAmazonS3? _s3Client;

        public UserService(EvangelionERPV2.Shared.Repositories.IRepository<User> userRepository,
            IConfiguration configuration,
            AWSKMSKeyProvider kmsProvider)
        {
            _userRepository = userRepository;
            _configuration = configuration;
            _kmsProvider = kmsProvider;
        }

        public async Task<User> CreateAsync(User user)
        {
            try
            {
                if (user == null)
                    throw new InsertDatabaseException($"{nameof(User)} is null");

                user.Id = Guid.NewGuid();
                User includedUser = new User();

                user.Password = SharedFunctions.IsPasswordHashFormat(user.Password)
                    ? user.Password
                    : SharedFunctions.HashPassword(user.Password);
                user.ProfilePicture = SharedFunctions.EnsureEncryptedAddress(user.ProfilePicture);
                includedUser = await _userRepository.CreateAsync(user);
                await _userRepository.CommitAsync();
                return includedUser;
            }
            catch (Exception ex)
            {
                throw new InsertDatabaseException(ex.Message, ex);
            }
        }

        public User Update(User user)
        {
            User existentUser = _userRepository.GetById(user.Id);
            User updatedUser = new User();

            if (existentUser == null)
                throw new NotFoundDatabaseException($"{nameof(User)} was not found in database.");

            if (!string.IsNullOrWhiteSpace(user.Password) && !SharedFunctions.IsPasswordHashFormat(user.Password))
                user.Password = SharedFunctions.HashPassword(user.Password);

            user.ProfilePicture = SharedFunctions.EnsureEncryptedAddress(user.ProfilePicture);
            updatedUser = _userRepository.Update(user);
            _userRepository.Commit();
            return updatedUser;
        }

        public User Delete(Guid id)
        {

            User user = _userRepository.GetById(id);
            User deletedUser = new User();

            if (user == null)
                throw new NotFoundDatabaseException($"{nameof(User)} was not found in database.");

            deletedUser = _userRepository.Delete(user);
            _userRepository.Commit();
            return deletedUser;
        }

        public async Task<User> LoginToSSOAsync(string idToken)
        {
            if (string.IsNullOrWhiteSpace(idToken))
                throw new ArgumentException("idToken is required", nameof(idToken));

            var clientId = _kmsProvider.GetKMSKey(_configuration.GetSection("GoogleSettings")["ClientId"] ?? string.Empty);
            if (string.IsNullOrWhiteSpace(clientId))
            {
                Log.Logger.Error("Google client id is not configured.");
                throw new InvalidOperationException("Google client id is not configured.");
            }

            GoogleJsonWebSignature.Payload payload;
            try
            {
                payload = await GoogleJsonWebSignature.ValidateAsync(idToken,
                    new GoogleJsonWebSignature.ValidationSettings { Audience = new[] { clientId } });
            }
            catch (Exception exValidate)
            {
                Log.Logger.Error(exValidate, "Invalid Google IdToken");
                throw new UnauthorizedAccessException("Invalid Google IdToken", exValidate);
            }

            if (payload == null || string.IsNullOrWhiteSpace(payload.Email) ||
                payload.EmailVerified != true)
            {
                Log.Logger.Warning("Google token did not contain a verified email.");
                throw new UnauthorizedAccessException("Email not verified by Google.");
            }

            // Use payload to find/create the user
            var user = _userRepository.GetByCondition(u => u != null && u.Email == payload.Email).FirstOrDefault();

            if (user != null)
            {
                user.IsLogged = 1;
                _userRepository.Update(user);
                _userRepository.Commit();
                return user;
            }

            throw new NotFoundDatabaseException("User not found. Please register before logging in with SSO.");
        }

        public async Task<User> UpdateProfilePictureAsync(User user, string? profilePicturePayload)
        {
            if (user == null)
                throw new ArgumentNullException(nameof(user));

            var existentUser = _userRepository.GetById(user.Id);
            if (existentUser == null)
                throw new NotFoundDatabaseException($"{nameof(User)} was not found in database.");

            var bucketName = SharedFunctions.GetUserBucketName(_configuration);
            if (string.IsNullOrWhiteSpace(bucketName))
                throw new InvalidOperationException("AWS bucket name is not configured.");

            var s3Client = await GetS3ClientAsync();
            await s3Client.DeleteItemIfExistsAsync(bucketName, existentUser.ProfilePicture);

            if (string.IsNullOrWhiteSpace(profilePicturePayload))
            {
                user.ProfilePicture = string.Empty;
                user.UpdatedAt = DateTime.UtcNow;
                return Update(user);
            }

            var keyName = $"users/{user.UserName.ClearString().ToLowerInvariant()}-{DateTime.UtcNow:yyyyMMddHHmmssfff}";
            using var content = SharedFunctions.GetMemoryStreamFromBase64Payload(profilePicturePayload);
            await s3Client.CreateItemAsync(bucketName, keyName, content);

            user.ProfilePicture = SharedFunctions.EnsureEncryptedAddress(keyName);
            user.UpdatedAt = DateTime.UtcNow;
            return Update(user);
        }

        public async Task<string> GetProfilePictureBase64Async(string? profilePictureAddress)
        {
            try
            {
                string bucketName = SharedFunctions.GetUserBucketName(_configuration);
                if (string.IsNullOrWhiteSpace(bucketName))
                    throw new InvalidOperationException("AWS bucket name is not configured.");

                var s3Client = await GetS3ClientAsync();
                return await s3Client.GetItemBase64Async(bucketName, profilePictureAddress);
            }
            catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound || ex.StatusCode == HttpStatusCode.BadRequest)
            {
                Log.Logger.Warning("User profile image not found in S3 for key {KeyName}", profilePictureAddress);
                return string.Empty;
            }
            catch (Exception ex)
            {
                Log.Logger.Warning(ex, "Unable to load user profile image from S3 for key {KeyName}", profilePictureAddress);
                return string.Empty;
            }
        }

        private async Task<IAmazonS3> GetS3ClientAsync()
        {
            if (_s3Client != null)
                return _s3Client;

            var awsCredentials = await _kmsProvider.GetAWSCredentialsAsync();
            _s3Client = new AmazonS3Client(awsCredentials, RegionEndpoint.USEast1);
            return _s3Client;
        }
    }
}
