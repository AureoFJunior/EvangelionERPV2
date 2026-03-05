using EvangelionERPV2.Shared.Entities;

namespace EvangelionERPV2.UserModule.Application.Interface
{
    public interface IUserService<TEntity> where TEntity : class
    {
        #region Sync
        public TEntity Delete(Guid id);
        public TEntity Update(User user);
        #endregion

        #region Async
        public Task<TEntity> CreateAsync(User user);
        Task<User> LoginToSSOAsync(string idToken);
        Task<TEntity> UpdateProfilePictureAsync(User user, string? profilePicturePayload);
        #endregion
    }
}
