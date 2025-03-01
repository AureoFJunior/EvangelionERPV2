using Microsoft.EntityFrameworkCore;

namespace EvangelionERPV2.ProductModule.Infra.Context
{
    public class ProductModuleDbContextIndexes
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
