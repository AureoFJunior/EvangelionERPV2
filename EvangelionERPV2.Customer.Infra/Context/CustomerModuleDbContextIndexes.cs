using Microsoft.EntityFrameworkCore;

namespace EvangelionERPV2.CustomerModule.Infra.Context
{
    public class CustomerModuleDbContextIndexes
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
