using Azure.Core;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Exceptions;
using EvangelionERPV2.Shared.Utils;
using EvangelionERPV2.UserModule.Application.Interface;
using EvangelionERPV2.UserModule.Domain.Interface;
using Google.Apis.Auth;
using Microsoft.Extensions.Configuration;
using Serilog;

namespace EvangelionERPV2.UserModule.Application.Services
{
    public class UserService : IUserService<User>
    {
        private readonly IRepository<User> _userRepository;
        private readonly IConfiguration _configuration;
        private readonly AWSKMSKeyProvider _kmsProvider;

        public UserService(IRepository<User> userRepository,
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
                var existentUser = _userRepository.GetById(user.Id);
                User includedUser = new User();

                if (existentUser != null)
                    throw new InsertDatabaseException($"{nameof(User)} already has an register in database");

                user.Password = SharedFunctions.Encrypt(user.Password);
                includedUser = await _userRepository.CreateAsync(user);
                await _userRepository.CommitAsync();
                return includedUser;
            }
            catch (Exception ex)
            {
                throw new InsertDatabaseException(ex.Message, ex.InnerException);
            }
        }

        public User Update(User user)
        {
            User existentUser = _userRepository.GetById(user.Id);
            User updatedUser = new User();

            if (existentUser == null)
                throw new NotFoundDatabaseException($"{nameof(User)} was not found in database.");

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
                (payload.EmailVerified != null && !payload.EmailVerified))
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

            var newUser = new User
            {
                Id = Guid.NewGuid(),
                UserName = payload.Email,
                Email = payload.Email,
                FirstName = payload.GivenName ?? payload.Name ?? string.Empty,
                LastName = payload.FamilyName ?? string.Empty,
                Password = SharedFunctions.Encrypt(Guid.NewGuid().ToString()), // random password for SSO users
                BirthDate = DateTime.UtcNow,
                IsLogged = 1
            };

            var created = await _userRepository.CreateAsync(newUser);
            await _userRepository.CommitAsync();
            return created;
        }
    }
}