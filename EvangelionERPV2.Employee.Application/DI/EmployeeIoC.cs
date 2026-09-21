using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
using Serilog;
using EvangelionERPV2.Shared.Configs;
using EvangelionERPV2.EmployeeModule.Domain.Interface;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.EmployeeModule.Domain.Repositories;
using EvangelionERPV2.EmployeeModule.Application.Configs;
using EvangelionERPV2.EmployeeModule.Application.Interface;
using EvangelionERPV2.EmployeeModule.Application.Services;
using EvangelionERPV2.Shared.Utils;
using Amazon.SecretsManager;
using EvangelionERPV2.Shared.Context;

namespace EvangelionERPV2.EmployeeModule.Application.DI
{
    public class EmployeeIoC
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
                #endregion

                services.AddLogging();

                #region DataBase
                services.AddDbContextPool<AppDbContext>((serviceProvider, options) =>
                {
                    var kmsProvider = serviceProvider.GetRequiredService<AWSKMSKeyProvider>();
                    options.UseSqlServer(kmsProvider.GetKMSKey(configuration.GetConnectionString("DefaultConnection") ?? string.Empty));
                });

                #endregion

                #region Mapper
                var mapper = MapperConfig.RegisterMaps().CreateMapper();
                services.AddSingleton(mapper);
                services.AddAutoMapper(config => { }, AppDomain.CurrentDomain.GetAssemblies());

                #endregion

                #region Repositorys
                services.AddTransient(typeof(EvangelionERPV2.Shared.Repositories.IRepository<>), typeof(EvangelionERPV2.Shared.Repositories.Repository<>));
                services.AddTransient(typeof(EvangelionERPV2.Shared.Repositories.IRepository<Enterprise>), typeof(EnterpriseModule.Domain.Repositories.EnterpriseRepository));
                services.AddTransient(typeof(EvangelionERPV2.Shared.Repositories.IRepository<Employee>), typeof(EmployeeRepository));
                services.AddTransient(typeof(IEmployeeRepository<Employee>), typeof(EmployeeRepository));

                #endregion

                #region Services
                services.AddTransient(typeof(IEmployeeService<Employee>), typeof(EmployeeService));
                services.AddTransient(typeof(IEmployeeAbsenceService<EmployeeAbsence>), typeof(EmployeeAbsenceService));
                services.AddTransient(typeof(IEmployeeVacationService<EmployeeVacationAccrual>), typeof(EmployeeVacationService));

                #region Vacation lapse sweep
                services.Configure<EmployeeVacationSettings>(configuration.GetSection("EmployeeVacationSettings"));

                services.AddScoped<EmployeeVacationLapseService>();
                services.AddHostedService<EmployeeVacationLapseWorker>();
                #endregion

                #endregion

                Log.Logger.Information("Employee vacation accrual services and lapse sweeper registered.");

                services.AddScoped(typeof(EvangelionERPV2.Shared.Repositories.IUnitOfWork<AppDbContext>), typeof(EvangelionERPV2.Shared.Repositories.UnitOfWork<AppDbContext>));
            }
            catch (Exception ex)
            {
                Log.Logger.Error("Error at DI IoC Employee. ErrorType={ErrorType}", ex.GetType().Name);
            }
        }
    }
}
