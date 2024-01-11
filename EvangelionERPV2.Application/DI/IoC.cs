using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
using EvangelionERPV2.Infra.Context;
using EvangelionERPV2.Application.Configs;
using EvangelionERPV2.Domain.Interfaces;
using EvangelionERPV2.Infra.Repositories;
using EvangelionERPV2.Domain.Utils;
using Serilog;
using EvangelionERPV2.Domain.Models;
using EvangelionERPV2.Domain.Interfaces.Services;
using EvangelionERPV2.Domain.Interfaces.Repositories;
using EvangelionERPV2.Domain.Models.Token;

namespace EvangelionERPV2.Application.DI
{
    public class IoC
    {
        public static void Configure(IServiceCollection services, string connection, IConfiguration configuration)
        {
            try
            {
                services.AddLogging();

                #region DataBase
                services.AddDbContext<AppDbContext>(options => options.UseNpgsql(connection));

                #endregion

                #region Mapper
                var mapper = MapperConfig.RegisterMaps().CreateMapper();
                services.AddSingleton(mapper);
                services.AddAutoMapper(AppDomain.CurrentDomain.GetAssemblies());

                #endregion

                #region Repositorys
                services.AddScoped(typeof(IRepository<>), typeof(Repository<>));
                services.AddScoped(typeof(IRepository<User>), typeof(UserRepository));


                #endregion

                #region Services
                services.AddScoped(typeof(TokenService));
                services.AddScoped(typeof(IRabbitMQManager), typeof(RabbitMQManager));
                services.AddScoped(typeof(IUserService<User>), typeof(UserService));


                #endregion

                services.AddScoped(typeof(IUnitOfWork<AppDbContext>), typeof(UnitOfWork<AppDbContext>));

                #region Redis
                // We'll use it later
                //services.AddStackExchangeRedisCache(o =>
                //{
                //    o.InstanceName = "evaRedis";
                //    //o.Configuration = configuration.Get;
                //});
            }
            catch (Exception ex)
            {
                Log.Logger.Error($"Error at DI IoC: {ex.Message}", ex);
            }
            #endregion
        }
    }
}