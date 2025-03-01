using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Exceptions;
using EvangelionERPV2.Shared.Utils;
using EvangelionERPV2.UserModule.Application.Interface;
using EvangelionERPV2.UserModule.Domain.Interface;

namespace EvangelionERPV2.UserModule.Application.Services
{
    public class UserService : IUserService<User>
    {
        private readonly IRepository<User> _userRepository;

        public UserService(IRepository<User> userRepository)
        {
            _userRepository = userRepository;
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
    }
}