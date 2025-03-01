using Microsoft.AspNetCore.Mvc.Versioning;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using System.Text;
using System.Text.Json.Serialization;
using FluentValidation.AspNetCore;
using EvangelionERPV2.Web.FluentValidator;
using EvangelionERPV2.Web.Logging;
using Serilog;
using FluentValidation;
using EvangelionERPV2.Shared.Utils;
using EvangelionERPV2.Shared.Hubs;
using EvangelionERPV2.EnterpriseModule.Application.DI;
using EvangelionERPV2.OrderModule.Application.DI;
using EvangelionERPV2.CustomerModule.Application.DI;
using EvangelionERPV2.UserModule.Application.DI;
using EvangelionERPV2.ProductModule.Application.DI;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.EmailModule.Application.DI;
using Amazon.SecretsManager;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

#region Services

try
{
    #region IoC
    ConfigureIoC(builder);
    #endregion

    LogConfig.Configure();

    Log.Logger.Information("Starting Controllers");
    SetupControllers(builder);

    #region Validator
    BuildValidators(builder);
    #endregion

    Log.Logger.Information("Starting CORS");
    builder.Services.AddCors();

    Log.Logger.Information("Starting API Versioning");
    SetupAPIVersioning(builder);

    Log.Logger.Information("Starting Swagger");
    AddSwaggerGen(builder);

    Log.Logger.Information("Starting JWT");
    SetupJWT(builder);

    Log.Logger.Information("Starting Health Check");
    SetupHealthCheck(builder);

   
    #endregion

    #region App
    var app = builder.Build();

    Log.Logger.Information("Starting App Builder");

    SharedFunctions.Initialize(app.Services);

    Log.Logger.Information("Swagger Config");

    app.UseMiddleware<ExceptionHandlingMiddleware>();

    // Configure the HTTP request pipeline.

    SetupSwagger(app);

    app.UseHttpsRedirection();

    app.UseRouting();

    app.UseCors(x => x.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());

    app.UseAuthentication();
    app.UseAuthorization();

    app.UseEndpoints(endpoints =>
    {
        endpoints.MapHub<OrderHub>("/orderHub");
    });

    app.MapControllers();
    app.UseHealthChecks("/health");

    Log.Logger.Information("Starting App Run");
    app.Run();
}
catch (Exception ex)
{
    Log.Logger.Error(ex.Message + ex.StackTrace);
}

static void BuildValidators(WebApplicationBuilder builder)
{
    builder.Services.AddTransient<IValidator<User>, UserValidator>();
    builder.Services.AddTransient<IValidator<Enterprise>, EnterpriseValidator>();
    builder.Services.AddTransient<IValidator<Order>, OrderValidator>();
    builder.Services.AddTransient<IValidator<OrderedProduct>, OrderedProductValidator>();
    builder.Services.AddTransient<IValidator<Customer>, CustomerValidator>();
    builder.Services.AddTransient<IValidator<Product>, ProductValidator>();
}

static void RegisterValidations(FluentValidationMvcConfiguration options)
{
    options.RegisterValidatorsFromAssemblyContaining<UserValidator>();
    options.RegisterValidatorsFromAssemblyContaining<EnterpriseValidator>();
    options.RegisterValidatorsFromAssemblyContaining<OrderValidator>();
    options.RegisterValidatorsFromAssemblyContaining<OrderedProductValidator>();
    options.RegisterValidatorsFromAssemblyContaining<CustomerValidator>();
    options.RegisterValidatorsFromAssemblyContaining<ProductValidator>();
}

static void SetupSwagger(WebApplication app)
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "EvangelionERPV2");
        c.RoutePrefix = "swagger";
    });
}

static void SetupJWT(WebApplicationBuilder builder)
{
    var key = Encoding.ASCII.GetBytes("f0f228f0-4f22-45bc-bed8-bea3c97d463d");
    builder.Services.AddAuthentication(x =>
    {
        x.DefaultAuthenticateScheme = Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerDefaults.AuthenticationScheme;
        x.DefaultChallengeScheme = Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerDefaults.AuthenticationScheme;
    })
      .AddJwtBearer(x =>
      {
          x.RequireHttpsMetadata = false;
          x.SaveToken = true;
          x.TokenValidationParameters = new Microsoft.IdentityModel.Tokens.TokenValidationParameters
          {
              ValidateIssuerSigningKey = true,
              IssuerSigningKey = new SymmetricSecurityKey(key),
              ValidateIssuer = false,
              ValidateAudience = false
          };
      });
}

static void AddSwaggerGen(WebApplicationBuilder builder)
{
    builder.Services.AddSwaggerGen(c =>
    {
        c.SwaggerDoc("v1", new OpenApiInfo { Title = "EvangelionERP-V2", Version = "v1" });

        c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme()
        {
            Name = "Authorization",
            Type = SecuritySchemeType.ApiKey,
            Scheme = "Bearer",
            BearerFormat = "JWT",
            In = ParameterLocation.Header,
            Description = "JWT Authorization header using the Bearer scheme" +
            "Use the pattern 'Bearer TOKEN'",
        });
        c.AddSecurityRequirement(new OpenApiSecurityRequirement
                    {
                    {
                          new OpenApiSecurityScheme
                          {
                              Reference = new OpenApiReference
                              {
                                  Type = ReferenceType.SecurityScheme,
                                  Id = "Bearer"
                              }
                          },
                         new string[] {}
                    }
                    });
    });
}

static void SetupAPIVersioning(WebApplicationBuilder builder)
{
    builder.Services.AddApiVersioning(opt =>
    {
        opt.DefaultApiVersion = new Microsoft.AspNetCore.Mvc.ApiVersion(1, 0);
        opt.AssumeDefaultVersionWhenUnspecified = true;
        opt.ReportApiVersions = true;
        opt.ApiVersionReader = ApiVersionReader.Combine(new UrlSegmentApiVersionReader(),
                                                        new HeaderApiVersionReader("x-api-version"),
                                                        new MediaTypeApiVersionReader("x-api-version"));
    });
}

static void SetupControllers(WebApplicationBuilder builder)
{
    builder.Services.AddControllers()
        .AddFluentValidation((Action<FluentValidationMvcConfiguration>)(options =>
        {
            // Automatic registration of validators in assembly
            RegisterValidations(options);
        }))
       .AddJsonOptions(options =>
       {
           options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
       });
}

static void SetupHealthCheck(WebApplicationBuilder builder)
{
    builder.Services.AddHealthChecks();
}

static void ConfigureIoC(WebApplicationBuilder builder)
{
    UserIoC.Configure(builder.Services, builder.Configuration);
    ProductIoC.Configure(builder.Services, builder.Configuration);
    OrderIoC.Configure(builder.Services, builder.Configuration);
    CustomerIoC.Configure(builder.Services, builder.Configuration);
    EnterpriseIoC.Configure(builder.Services, builder.Configuration);
    EmailIoC.Configure(builder.Services, builder.Configuration);
    SharedIoC.Configure(builder.Services, builder.Configuration);
}

static void SetupAppSettings(WebApplicationBuilder builder)
{
    var env = builder.Environment;
    builder.Configuration
          .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
          .AddJsonFile($"appsettings.{env.EnvironmentName}.json", optional: true, reloadOnChange: true);
}
#endregion
