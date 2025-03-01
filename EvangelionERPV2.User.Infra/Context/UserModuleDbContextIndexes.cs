using Microsoft.EntityFrameworkCore;

namespace EvangelionERPV2.UserModule.Infra.Context
{
    public class UserModuleDbContextIndexes
    {
        public static void Configure(ModelBuilder builder)
        {
            #region Consult
            //builder.Entity<Consult>()
            //    .HasIndex(x => x.Id);
            #endregion
        }
    }
}
