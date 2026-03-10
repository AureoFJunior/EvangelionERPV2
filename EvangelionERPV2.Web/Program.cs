using System.Text;
using System.Text.Json.Serialization;
using AspNetCoreRateLimit;
using EvangelionERPV2.BillsModule.Application.DI;
using EvangelionERPV2.CustomerModule.Application.DI;
using EvangelionERPV2.EmailModule.Application.DI;
using EvangelionERPV2.EnterpriseModule.Application.DI;
using EvangelionERPV2.NFeModule.Application.DI;
using EvangelionERPV2.OrderModule.Application.DI;
using EvangelionERPV2.ProductModule.Application.DI;
using EvangelionERPV2.Shared.Entities;
using Prometheus;
using EvangelionERPV2.Shared.Hubs;
using EvangelionERPV2.Shared.Utils;
using EvangelionERPV2.UserModule.Application.DI;
using EvangelionERPV2.Web.FluentValidator;
using EvangelionERPV2.Web.Logging;
using FluentValidation;
using FluentValidation.AspNetCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc.Versioning;
using Microsoft.IdentityModel.Tokens;
using Serilog;
using Microsoft.OpenApi;
using Microsoft.AspNetCore.ResponseCompression;

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

    builder.Services.AddFluentValidationAutoValidation();
    builder.Services.AddFluentValidationClientsideAdapters();

    Log.Logger.Information("Starting JWT"); 
    SetupJWT(builder);

    Log.Logger.Information("Starting Health Check");
    SetupHealthCheck(builder);

    Log.Logger.Information("Starting Response Compression");
    builder.Services.AddResponseCompression(options =>
    {
        options.EnableForHttps = true;
    });

    AddRequestRateLimit(builder);
    builder.Services.AddSignalR();
    #endregion

    #region App
    var app = builder.Build();

    Log.Logger.Information("Starting App Builder");

    Log.Logger.Information("Swagger Config");

    app.UseMiddleware<RequestLoggingMiddleware>();
    app.UseMiddleware<ExceptionHandlingMiddleware>();

    // Configure the HTTP request pipeline.

    SetupSwagger(app, app.Environment);

    app.UseHttpMetrics();

    app.UseHttpsRedirection();

    app.UseResponseCompression();

    app.UseRouting();

    app.UseIpRateLimiting();

    app.UseCors(x => x.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());

    app.UseAuthentication();
    app.UseAuthorization();

    app.MapMetrics("/metrics");
    app.MapHub<OrderHub>("/orderHub");

    app.MapControllers();
    app.MapHealthChecks("/health");

    SharedFunctions.Initialize(app.Services);

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
    builder.Services.AddTransient<IValidator<ProductPicture>, ProductPictureValidator>();
}

static void SetupSwagger(WebApplication app, IWebHostEnvironment env)
{
    if (!env.IsDevelopment())
        return;

    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "EvangelionERPV2");
        c.RoutePrefix = "swagger";
    });
}

static void SetupJWT(WebApplicationBuilder builder)
{
    var issuer = builder.Configuration.GetSection("JwtSettings")["Issuer"] ?? string.Empty;
    var audience = builder.Configuration.GetSection("JwtSettings")["Audience"] ?? string.Empty;
    var isDevelopment = builder.Environment.IsDevelopment();
    builder.Services.AddAuthentication(x =>
    {
        x.DefaultAuthenticateScheme = Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerDefaults.AuthenticationScheme;
        x.DefaultChallengeScheme = Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerDefaults.AuthenticationScheme;
    })
      .AddJwtBearer();

    builder.Services.AddOptions<JwtBearerOptions>(Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerDefaults.AuthenticationScheme)
        .Configure<IConfiguration, AWSKMSKeyProvider>((options, configuration, kmsProvider) =>
        {
            var tokenKey = kmsProvider.GetKMSKey(configuration.GetSection("Encryption")["TokenKey"] ?? string.Empty);
            if (string.IsNullOrWhiteSpace(tokenKey))
                throw new InvalidOperationException("JWT TokenKey is not configured.");

            options.RequireHttpsMetadata = !isDevelopment;
            options.SaveToken = true;
            options.TokenValidationParameters = new Microsoft.IdentityModel.Tokens.TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.ASCII.GetBytes(tokenKey)),
                ValidateIssuer = true,
                ValidIssuer = issuer,
                ValidateAudience = true,
                ValidAudience = audience,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromMinutes(1)
            };
            options.Events = new JwtBearerEvents
            {
                OnMessageReceived = context =>
                {
                    var accessToken = context.Request.Query["access_token"].FirstOrDefault();
                    var path = context.HttpContext.Request.Path;
                    if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/orderHub"))
                    {
                        context.Token = accessToken;
                    }
                    return Task.CompletedTask;
                }
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
            Description = "JWT Authorization header using the Bearer scheme. " +
            "Use the pattern 'Bearer TOKEN'",
        });
        c.AddSecurityRequirement(document => new OpenApiSecurityRequirement
        {
            [new OpenApiSecuritySchemeReference("Bearer", document)] = []
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
       .AddJsonOptions(options =>
       {
           options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
           options.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
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
    BillsIoC.Configure(builder.Services, builder.Configuration);
    NFeIoC.Configure(builder.Services, builder.Configuration);
    CustomerIoC.Configure(builder.Services, builder.Configuration);
    EnterpriseIoC.Configure(builder.Services, builder.Configuration);
    EmailIoC.Configure(builder.Services, builder.Configuration);
    SharedIoC.Configure(builder.Services, builder.Configuration);
}

static void AddRequestRateLimit(WebApplicationBuilder builder)
{
    builder.Services.AddMemoryCache();
    builder.Services.AddHttpContextAccessor();
    builder.Services.Configure<IpRateLimitOptions>(builder.Configuration.GetSection("IpRateLimiting"));
    builder.Services.Configure<IpRateLimitPolicies>(builder.Configuration.GetSection("IpRateLimitPolicies"));
    builder.Services.AddInMemoryRateLimiting();
    builder.Services.AddSingleton<IRateLimitConfiguration, RateLimitConfiguration>();
}
#endregion

