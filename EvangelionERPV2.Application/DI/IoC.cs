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
using EvangelionERPV2.Domain.Models.RabbitMQ;

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
                services.AddScoped(typeof(IRepository<Enterprise>), typeof(EnterpriseRepository));


                #endregion

                #region Services
                services.AddScoped(typeof(TokenService));
                services.AddScoped(typeof(IUserService<User>), typeof(UserService));
                services.AddScoped(typeof(IEnterpriseService<Enterprise>), typeof(EnterpriseService));


                #endregion

                services.AddScoped(typeof(IUnitOfWork<AppDbContext>), typeof(UnitOfWork<AppDbContext>));

                #region RabbitMQ
                services.Configure<RabbitMQSettings>(opt => configuration.GetSection("RabbitMQSettings").Bind(opt));
                services.Configure<OrderChannelSettings>(opt => configuration.GetSection("OrderChannelSettings").Bind(opt));
                services.Configure<EmailChannelSettings>(opt => configuration.GetSection("EmailChannelSettings").Bind(opt));
                services.Configure<BaseChannelSettings>(opt => configuration.GetSection("BaseChannelSettings").Bind(opt));
                services.AddScoped(typeof(IRabbitMQManager), typeof(RabbitMQManager));

                #endregion

                #region Settings
                services.Configure<EmailSettings>(opt => configuration.GetSection("EmailSettings").Bind(opt));
                #endregion

                #region Redis
                // We'll use it later
                //services.AddStackExchangeRedisCache(o =>
                //{
                //    o.InstanceName = "evaRedis";
                //    //o.Configuration = configuration.Get;
                //});
                #endregion
            }
            catch (Exception ex)
            {
                Log.Logger.Error($"Error at DI IoC: {ex.Message}", ex);
            }
            
        }
    }
}