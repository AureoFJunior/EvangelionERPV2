using Microsoft.EntityFrameworkCore;

namespace EvangelionERPV2.Infra.Context
{
    public class AppDbContextIndexes
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
