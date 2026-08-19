using EvangelionERPV2.Shared.Entities;

namespace EvangelionERPV2.EmployeeModule.Application.Interface
{
    public interface IEmployeeService<TEntity> where TEntity : class
    {
        #region Sync
        public TEntity Delete(Guid id);
        public TEntity Update(Employee employee);
        #endregion

        #region Async
        public Task<TEntity> CreateAsync(Employee employee, string? picturePayload = null);
        public Task<TEntity> UpdatePictureAsync(Employee employee, string? picturePayload);
        public Task<string> GetPictureBase64Async(string? pictureAddress);
        #endregion
    }
}
