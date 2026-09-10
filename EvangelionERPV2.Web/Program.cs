using System.Text;
using System.Text.Json.Serialization;
using AspNetCoreRateLimit;
using EvangelionERPV2.AiModule.Application.DI;
using EvangelionERPV2.AiModule.Application.Interface;
using EvangelionERPV2.AuditModule.Application.DI;
using EvangelionERPV2.BillsModule.Application.DI;
using EvangelionERPV2.CustomerModule.Application.DI;
using EvangelionERPV2.EmployeeModule.Application.DI;
using EvangelionERPV2.EmailModule.Application.DI;
using EvangelionERPV2.EnterpriseModule.Application.DI;
using EvangelionERPV2.NFeModule.Application.DI;
using EvangelionERPV2.OpportunityRadarModule.Application.DI;
using EvangelionERPV2.OrderModule.Application.DI;
using EvangelionERPV2.PdvModule.Application.DI;
using EvangelionERPV2.ProductModule.Application.DI;
using EvangelionERPV2.ReportsModule.Application.DI;
using EvangelionERPV2.SupplierModule.Application.DI;
using EvangelionERPV2.Shared.Context;
using EvangelionERPV2.Shared.DTOs;
using EvangelionERPV2.Shared.Entities;
using Prometheus;
using EvangelionERPV2.Shared.Hubs;
using EvangelionERPV2.Shared.Utils;
using EvangelionERPV2.UserModule.Application.DI;
using EvangelionERPV2.Web.FluentValidator;
using EvangelionERPV2.Web.Logging;
using EvangelionERPV2.Web.HealthChecks;
using EvangelionERPV2.Web.Security;
using EvangelionERPV2.Web.Services;
using FluentValidation;
using FluentValidation.AspNetCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Versioning;
using Microsoft.IdentityModel.Tokens;
using Serilog;
using Microsoft.OpenApi;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.Net.Http.Headers;

var builder = WebApplication.CreateBuilder(args);
const string DefaultCorsPolicyName = "DefaultCorsPolicy";

// Add services to the container.

#region Services

try
{
    #region IoC
    ConfigureIoC(builder);
    #endregion

    SetupRequestLimits(builder);

    LogConfig.Configure();

    Log.Logger.Information("Starting Controllers");
    SetupControllers(builder);

    #region Validator
    BuildValidators(builder);
    #endregion

    Log.Logger.Information("Starting CORS");
    SetupCors(builder);

    Log.Logger.Information("Starting API Versioning");
    SetupAPIVersioning(builder);

    Log.Logger.Information("Starting Swagger");
    AddSwaggerGen(builder);

    builder.Services.AddFluentValidationAutoValidation();
    builder.Services.AddFluentValidationClientsideAdapters();
    builder.Services.Configure<RequestLoggingOptions>(builder.Configuration.GetSection("RequestLogging"));

    Log.Logger.Information("Starting JWT"); 
    SetupJWT(builder);

    Log.Logger.Information("Starting Authorization");
    SetupAuthorization(builder);

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

    app.UseMiddleware<ExceptionHandlingMiddleware>();

    // Configure the HTTP request pipeline.

    SetupSwagger(app, app.Environment);

    app.UseHttpMetrics();

    app.UseHttpsRedirection();
    if (!app.Environment.IsDevelopment())
        app.UseHsts();
    app.Use(async (context, next) =>
    {
        ApplySecurityHeaders(context.Response.Headers);
        await next();
    });

    app.UseResponseCompression();

    app.UseRouting();

    app.UseIpRateLimiting();

    app.UseCors(DefaultCorsPolicyName);

    app.UseAuthentication();
    app.UseMiddleware<RequestLoggingMiddleware>();
    app.UseMiddleware<ActiveTenantEnforcementMiddleware>();
    app.UseAuthorization();

    app.MapMetrics("/metrics").RequireAuthorization(RbacPolicies.Metrics.Read);
    app.MapHub<OrderHub>("/orderHub", options =>
    {
        options.Transports =
            HttpTransportType.WebSockets |
            HttpTransportType.ServerSentEvents |
            HttpTransportType.LongPolling;
    }).RequireAuthorization(RbacPolicies.Orders.Read);

    app.MapHub<AiHub>("/aiHub", options =>
    {
        options.Transports =
            HttpTransportType.WebSockets |
            HttpTransportType.ServerSentEvents |
            HttpTransportType.LongPolling;
    }).RequireAuthorization(RbacPolicies.Ai.Chat);

    app.MapControllers();
    app.MapHealthChecks("/health").AllowAnonymous();

    SharedFunctions.Initialize(app.Services);

    await LogPendingMigrationsAsync(app);

    Log.Logger.Information("Starting App Run");
    app.Run();
}
catch (Exception ex)
{
    Log.Logger.Error("Application startup failed. ErrorType={ErrorType}", ex.GetType().Name);
    throw;
}

static void BuildValidators(WebApplicationBuilder builder)
{
    // Assembly scan rather than a hand-maintained list. A validator that existed but was never
    // added to that list was silently inert - validation that looks present and does nothing -
    // and ValidatorCoverageTests now fails when a bound request type has no validator at all.
    builder.Services.AddValidatorsFromAssemblyContaining<UserValidator>(ServiceLifetime.Transient);
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
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(tokenKey)),
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
                    // Browser WebSocket and EventSource cannot set an Authorization header, so
                    // @microsoft/signalr sends the JWT here instead. Every mapped hub needs to be
                    // in this list or it only authenticates over long polling.
                    var isSignalRHubRequest =
                        path.StartsWithSegments("/orderHub") ||
                        path.StartsWithSegments("/aiHub");

                    if (!string.IsNullOrEmpty(accessToken) && isSignalRHubRequest)
                    {
                        context.Token = accessToken;
                    }

                    return Task.CompletedTask;
                }
            };
        });
}

static void SetupAuthorization(WebApplicationBuilder builder)
{
    builder.Services.AddAuthorization(options =>
    {
        options.FallbackPolicy = new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .Build();

        options.AddRbacPolicies();
    });

    builder.Services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();
    builder.Services.AddScoped<IAuthorizationHandler, SelfOrPermissionAuthorizationHandler>();
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

static void SetupRequestLimits(WebApplicationBuilder builder)
{
    const long defaultMaxRequestBodySizeBytes = 256 * 1024;
    var configuredMaxRequestBodySize = builder.Configuration.GetValue<long?>("RequestLimits:MaxRequestBodySizeBytes");
    var maxRequestBodySizeBytes = configuredMaxRequestBodySize.HasValue && configuredMaxRequestBodySize.Value > 0
        ? configuredMaxRequestBodySize.Value
        : defaultMaxRequestBodySizeBytes;

    builder.WebHost.ConfigureKestrel(options =>
    {
        options.Limits.MaxRequestBodySize = maxRequestBodySizeBytes;
    });

    builder.Services.Configure<IISServerOptions>(options =>
    {
        options.MaxRequestBodySize = maxRequestBodySizeBytes;
    });
}

/// <summary>
/// Nothing in the app calls <c>Database.Migrate()</c>, by decision - so a committed migration has
/// not run anywhere until somebody applies it, and nothing used to say which ones were waiting.
/// Twice the first symptom was every request to the new tables answering 500 with
/// <c>Invalid column name</c>; this turns that into one line at boot.
/// Startup never fails on it: refusing to boot would strand an environment that is behind, which is
/// exactly the environment that needs to serve while somebody applies them.
/// </summary>
static async Task LogPendingMigrationsAsync(WebApplication app)
{
    try
    {
        using var scope = app.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var pending = (await context.Database.GetPendingMigrationsAsync()).ToList();

        if (pending.Count == 0)
            Log.Logger.Information("Database schema is up to date, no pending migrations");
        else
            Log.Logger.Fatal(
                "{Count} migration(s) have NOT been applied to this database: {PendingMigrations}. " +
                "Requests touching the new tables will fail with 'Invalid column name' until they are",
                pending.Count,
                string.Join(", ", pending));
    }
    catch (Exception exception)
    {
        // A database that cannot be reached at boot is DatabaseHealthCheck's job to report, not
        // a reason to take the process down here.
        Log.Logger.Error("Could not read pending migrations at startup. ErrorType={ErrorType}", exception.GetType().Name);
    }
}

static void SetupHealthCheck(WebApplicationBuilder builder)
{
    builder.Services.AddHealthChecks()
        .AddCheck<DatabaseHealthCheck>("sqlserver")
        .AddCheck<RedisHealthCheck>("redis")
        .AddCheck<RabbitMqHealthCheck>("rabbitmq");
}

static void ConfigureIoC(WebApplicationBuilder builder)
{
    AuditTrailIoC.Configure(builder.Services, builder.Configuration);
    UserIoC.Configure(builder.Services, builder.Configuration);
    ProductIoC.Configure(builder.Services, builder.Configuration);
    OrderIoC.Configure(builder.Services, builder.Configuration);
    BillsIoC.Configure(builder.Services, builder.Configuration);
    NFeIoC.Configure(builder.Services, builder.Configuration);
    CustomerIoC.Configure(builder.Services, builder.Configuration);
    EmployeeIoC.Configure(builder.Services, builder.Configuration);
    SupplierIoC.Configure(builder.Services, builder.Configuration);
    EnterpriseIoC.Configure(builder.Services, builder.Configuration);
    EmailIoC.Configure(builder.Services, builder.Configuration);
    OpportunityRadarIoC.Configure(builder.Services, builder.Configuration);
    ReportsIoC.Configure(builder.Services, builder.Configuration);
    PdvIoC.Configure(builder.Services, builder.Configuration);
    AiIoC.Configure(builder.Services, builder.Configuration);
    builder.Services.AddScoped<IAiProgressNotifier, AiProgressNotifier>();
    SharedIoC.Configure(builder.Services, builder.Configuration);

    // reCAPTCHA verification (short timeout, fail-closed for auth endpoints).
    builder.Services.AddHttpClient<IRecaptchaVerifier, RecaptchaVerifier>(client =>
    {
        client.Timeout = TimeSpan.FromSeconds(5);
    });

    // One password check, shared by the web login and the till's. Scoped: it holds a repository.
    builder.Services.AddScoped<PasswordLoginService>();
}

static void SetupCors(WebApplicationBuilder builder)
{
    var configuredOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
        ?.Where(origin => !string.IsNullOrWhiteSpace(origin))
        .Select(origin => origin.Trim())
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray() ?? [];

    if (!builder.Environment.IsDevelopment() && configuredOrigins.Length == 0)
        throw new InvalidOperationException("Cors:AllowedOrigins must be configured outside development environments.");

    builder.Services.AddCors(options =>
    {
        options.AddPolicy(DefaultCorsPolicyName, policy =>
        {
            if (configuredOrigins.Length > 0)
            {
                policy.WithOrigins(configuredOrigins)
                      .AllowAnyHeader()
                      .AllowAnyMethod();
                return;
            }

            policy.WithOrigins(
                    "http://localhost:19006",
                    "http://localhost:8081",
                    "http://localhost:3000",
                    "http://localhost:8082")
                .AllowAnyHeader()
                .AllowAnyMethod();
        });
    });
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

static void ApplySecurityHeaders(IHeaderDictionary headers)
{
    headers[HeaderNames.XContentTypeOptions] = "nosniff";
    headers[HeaderNames.XFrameOptions] = "DENY";
    headers["Referrer-Policy"] = "no-referrer";
    headers["Permissions-Policy"] = "accelerometer=(), autoplay=(), camera=(), geolocation=(), gyroscope=(), magnetometer=(), microphone=(), payment=(), usb=()";
    headers["Content-Security-Policy"] = "default-src 'none'; frame-ancestors 'none'; base-uri 'none'; form-action 'none'";
}
#endregion
