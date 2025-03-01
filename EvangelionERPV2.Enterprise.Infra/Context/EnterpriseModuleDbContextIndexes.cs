using Microsoft.EntityFrameworkCore;

namespace EvangelionERPV2.EnterpriseModule.Infra.Context
{
    public class EnterpriseModuleDbContextIndexes
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
