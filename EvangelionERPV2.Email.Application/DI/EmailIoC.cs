using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
using Serilog;
using EvangelionERPV2.Shared.Entities.RabbitMQ;
using EvangelionERPV2.Shared.Configs;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.EnterpriseModule.Domain.Interface;
using EvangelionERPV2.EmailModule.Application.Services;
using EvangelionERPV2.EmailModule.Application.Interface;
using EvangelionERPV2.EnterpriseModule.Domain.Repositories;
using EvangelionERPV2.EnterpriseModule.Infra.Context;
using EvangelionERPV2.OrderModule.Application.Interface;
using EvangelionERPV2.OrderModule.Application.Services;
using EvangelionERPV2.Shared.Utils;
using Amazon.SecretsManager;

namespace EvangelionERPV2.EmailModule.Application.DI
{
    public class EmailIoC
    {
        public static void Configure(IServiceCollection services, IConfiguration configuration)
        {
            try
            {
                #region AWS
                services.AddSingleton<IAmazonSecretsManager, AmazonSecretsManagerClient>(sp =>
                {
                    var region = Amazon.RegionEndpoint.USEast1;
                    return new AmazonSecretsManagerClient(region);
                });
                services.AddSingleton<AWSKMSKeyProvider>();

                var serviceProvider = services.BuildServiceProvider();
                var kmsProvider = serviceProvider.GetRequiredService<AWSKMSKeyProvider>();
                #endregion

                services.AddLogging();

                #region DataBase
                services.AddDbContext<EnterpriseModuleDbContext>(options => options.UseSqlServer(kmsProvider.GetKMSKey(configuration.GetConnectionString("DefaultConnection") ?? string.Empty)));

                #endregion

                #region Mapper
                var mapper = MapperConfig.RegisterMaps().CreateMapper();
                services.AddSingleton(mapper);
                services.AddAutoMapper(AppDomain.CurrentDomain.GetAssemblies());

                #endregion

                #region Repositorys
                services.AddTransient(typeof(IRepository<Enterprise>), typeof(EnterpriseRepository));


                #endregion

                #region Services
                services.AddScoped(typeof(IEmailService<Email>), typeof(EmailService));
                services.AddScoped(typeof(IOrderService<Order>), typeof(OrderService));


                #endregion

                services.AddScoped(typeof(IUnitOfWork<EnterpriseModuleDbContext>), typeof(UnitOfWork<EnterpriseModuleDbContext>));

                #region RabbitMQ
                services.Configure<RabbitMQSettings>(opt => configuration.GetSection("RabbitMQSettings").Bind(opt));
                services.Configure<EmailChannelSettings>(opt => configuration.GetSection("EmailChannelSettings").Bind(opt));
                services.AddSingleton<IEmailRabbitMQManager, EmailRabbitMQManager>();

                #endregion

                #region Settings
                services.Configure<EmailSettings>(opt => configuration.GetSection("EmailSettings").Bind(opt));
                #endregion
            }
            catch (Exception ex)
            {
                Log.Logger.Error($"Error at DI IoC Email: {ex.Message}", ex);
            }

        }
    }
}