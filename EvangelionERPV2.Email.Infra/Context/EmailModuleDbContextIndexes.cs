using Microsoft.EntityFrameworkCore;

namespace EvangelionERPV2.EmailModule.Infra.Context
{
    public class EmailModuleDbContextIndexes
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
