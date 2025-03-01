using Microsoft.EntityFrameworkCore;

namespace EvangelionERPV2.OrderModule.Infra.Context
{
    public class OrderModuleDbContextIndexes
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
