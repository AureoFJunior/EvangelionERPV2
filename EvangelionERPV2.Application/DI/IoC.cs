using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;

namespace EvangelionERPV2.Application.DI
{
    public class IoC
    {
        public static void Configure(IServiceCollection services, string conection, IConfiguration configuration)
        {
            #region DataBase
            //services.AddDbContext<AppDbContext>(options => options.UseMySQL(conection));

            #endregion

            #region Mapper
            //var mapper = MapperConfig.RegisterMaps().CreateMapper();
            //services.AddSingleton(mapper);
            //services.AddAutoMapper(AppDomain.CurrentDomain.GetAssemblies());

            #endregion

            #region Repositorys
            //services.AddScoped(typeof(IRepository<Employer>), typeof(EmployerRepository));
           

            #endregion

            #region Services
            //services.AddScoped(typeof(IEmployerService<Employer>), typeof(EmployerService));
       

            #endregion

            //services.AddScoped(typeof(IUnitOfWork<AppDbContext>), typeof(UnitOfWork<AppDbContext>));
            //FluentValidator here
            services.AddLogging();

            #region Redis
            //services.AddStackExchangeRedisCache(o =>
            //{
            //    o.InstanceName = "evaRedis";
            //    //o.Configuration = configuration.Get;
            //});

            #endregion
        }
    }
}