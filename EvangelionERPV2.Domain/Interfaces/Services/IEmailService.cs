using EvangelionERPV2.Domain.Models;
using MimeKit;

namespace EvangelionERPV2.Domain.Interfaces.Services
{
    public interface IEmailService<TEntity> where TEntity : class
    {
        #region Sync
        #endregion

        #region Async
        #endregion
        Task<MimeMessage> CreateEmail(Email email);
        Task SendEmail(MimeMessage message);
        Task SendManualEmail(Email email, Enterprise enterprise);
        Task SendMonthEmail(Guid? enterpriseId = null);
    }
}