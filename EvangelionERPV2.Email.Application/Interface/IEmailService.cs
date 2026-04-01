using EvangelionERPV2.Shared.Entities;
using MimeKit;

namespace EvangelionERPV2.EmailModule.Application.Interface
{
    public interface IEmailService<TEntity> where TEntity : class
    {
        #region Sync
        #endregion

        #region Async
        Task<Email> CreateAsync(Email email);
        Task<MimeMessage> CreateEmail(EmailStructure email);
        Task SendEmail(MimeMessage message);
        Task SendManualEmail(EmailStructure email, Enterprise enterprise);
        Task SendMonthEmail(Guid? enterpriseId = null);
        Task SendStockEmail(Guid? enterpriseId = null);
        #endregion
    }
}
