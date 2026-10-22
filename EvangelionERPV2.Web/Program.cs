using System.Text;
using System.Text.Json.Serialization;
using AspNetCoreRateLimit;
using EvangelionERPV2.AiModule.Application.DI;
using EvangelionERPV2.AiModule.Application.Interface;
using EvangelionERPV2.AuditModule.Application.DI;
using EvangelionERPV2.BillsModule.Application.DI;
using EvangelionERPV2.NotificationModule.Application.DI;
using EvangelionERPV2.CustomerModule.Application.DI;
using EvangelionERPV2.EmployeeModule.Application.DI;
using EvangelionERPV2.EmailModule.Application.DI;
using EvangelionERPV2.EnterpriseModule.Application.DI;
using EvangelionERPV2.NFeModule.Application.DI;
using EvangelionERPV2.OpportunityRadarModule.Application.DI;
using EvangelionERPV2.OrderModule.Application.DI;
using EvangelionERPV2.PdvModule.Application.DI;
using EvangelionERPV2.StorefrontModule.Application.DI;
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
using Microsoft.Extensions.Diagnostics.HealthChecks;
using EvangelionERPV2.Web.Security;
using EvangelionERPV2.Web.Services;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Connections;
using Asp.Versioning;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Http.Timeouts;
using System.Threading.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using Serilog;
using Microsoft.OpenApi;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.Net.Http.Headers;

var builder = WebApplication.CreateBuilder(args);
const string DefaultCorsPolicyName = "DefaultCorsPolicy";

Metrics.ConfigureMeterAdapter(options =>
    options.InstrumentFilterPredicate = instrument =>
        !instrument.Meter.Name.StartsWith("Microsoft.AspNetCore")
        && !instrument.Meter.Name.StartsWith("Private.InternalDiagnostics"));

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
    AddOverloadProtection(builder);
    builder.Services.AddSignalR();
    #endregion

    #region App
    var app = builder.Build();

    Log.Logger.Information("Starting App Builder");

    Log.Logger.Information("Swagger Config");

    app.UseMiddleware<ExceptionHandlingMiddleware>();

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

    app.UseRequestTimeouts();
    app.UseRateLimiter();

    app.UseCors(DefaultCorsPolicyName);

    app.UseAuthentication();
    app.UseMiddleware<SelfApiEndpointEnforcementMiddleware>();
    app.UseMiddleware<RequestLoggingMiddleware>();
    app.UseMiddleware<ActiveTenantEnforcementMiddleware>();
    app.UseAuthorization();

    app.MapMetrics("/metrics").RequireAuthorization(RbacPolicies.Metrics.Read);

    new KestrelMetricServer(port: 9101).Start();
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
    app.MapGet("/health", async (HealthResultCache cache, HttpContext context) =>
    {
        var report = await cache.GetAsync(context.RequestAborted);

        context.Response.StatusCode = report.Status == HealthStatus.Unhealthy
            ? StatusCodes.Status503ServiceUnavailable
            : StatusCodes.Status200OK;
        context.Response.ContentType = "text/plain";

        await context.Response.WriteAsync(report.Status.ToString(), context.RequestAborted);
    }).AllowAnonymous();

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
    if (string.IsNullOrWhiteSpace(issuer))
        throw new InvalidOperationException("JwtSettings:Issuer is not configured.");
    if (string.IsNullOrWhiteSpace(audience))
        throw new InvalidOperationException("JwtSettings:Audience is not configured.");
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

    builder.Services.AddScoped<IPermissionEvaluator, EvangelionERPV2.Web.Security.Iam.PermissionEvaluator>();
    builder.Services.AddScoped<EvangelionERPV2.Web.Security.Iam.IamService>();
    builder.Services.AddScoped<EvangelionERPV2.UserModule.Application.Interface.IMfaDisableGuard>(sp =>
        sp.GetRequiredService<EvangelionERPV2.Web.Security.Iam.IamService>());
    builder.Services.AddSingleton<Microsoft.AspNetCore.Authorization.IAuthorizationMiddlewareResultHandler, EvangelionERPV2.Web.Security.Iam.StepUpAuthorizationResultHandler>();
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
        opt.DefaultApiVersion = new ApiVersion(1, 0);
        opt.AssumeDefaultVersionWhenUnspecified = true;
        opt.ReportApiVersions = true;
        opt.ApiVersionReader = ApiVersionReader.Combine(new UrlSegmentApiVersionReader(),
                                                        new HeaderApiVersionReader("x-api-version"),
                                                        new MediaTypeApiVersionReader("x-api-version"));
    }).AddMvc();
}

static void SetupControllers(WebApplicationBuilder builder)
{
    builder.Services.AddControllers(options =>
       {
           options.Filters.Add<FluentValidationActionFilter>(FluentValidationActionFilter.FilterOrder);
       })
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
    const int attempts = 3;
    var retryDelay = TimeSpan.FromSeconds(3);

    for (var attempt = 1; attempt <= attempts; attempt++)
    {
        try
        {
            using var scope = app.Services.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var applied = (await context.Database.GetAppliedMigrationsAsync()).ToList();
            var pending = (await context.Database.GetPendingMigrationsAsync()).ToList();

            if (pending.Count == 0)
            {
                Log.Logger.Information("Database schema is up to date, no pending migrations");
                return;
            }

            if (applied.Count == 0 && attempt < attempts)
            {
                Log.Logger.Information(
                    "No applied migrations visible yet, the database may still be starting. Retrying ({Attempt}/{Attempts})",
                    attempt,
                    attempts);

                await Task.Delay(retryDelay);
                continue;
            }

            Log.Logger.Fatal(
                "{Count} migration(s) have NOT been applied to this database: {PendingMigrations}. " +
                "Requests touching the new tables will fail with 'Invalid column name' until they are",
                pending.Count,
                string.Join(", ", pending));

            return;
        }
        catch (Exception exception)
        {
            if (attempt == attempts)
            {
                Log.Logger.Error("Could not read pending migrations at startup. ErrorType={ErrorType}", exception.GetType().Name);
                return;
            }

            await Task.Delay(retryDelay);
        }
    }
}

static void SetupHealthCheck(WebApplicationBuilder builder)
{
    builder.Services.AddHealthChecks()
        .AddCheck<DatabaseHealthCheck>("sqlserver")
        .AddCheck<RedisHealthCheck>("redis")
        .AddCheck<RabbitMqHealthCheck>("rabbitmq");

    builder.Services.AddSingleton<HealthResultCache>();
    builder.Services.AddSingleton<IHealthCheckPublisher, PrometheusHealthPublisher>();
    builder.Services.Configure<HealthCheckPublisherOptions>(options =>
    {
        options.Period = TimeSpan.FromSeconds(30);
        options.Delay = TimeSpan.FromSeconds(10);
    });
}

static void ConfigureIoC(WebApplicationBuilder builder)
{
    AuditTrailIoC.Configure(builder.Services, builder.Configuration);
    UserIoC.Configure(builder.Services, builder.Configuration);
    ProductIoC.Configure(builder.Services, builder.Configuration);
    OrderIoC.Configure(builder.Services, builder.Configuration);
    BillsIoC.Configure(builder.Services, builder.Configuration);
    NotificationIoC.Configure(builder.Services, builder.Configuration);
    NFeIoC.Configure(
        builder.Services,
        builder.Configuration,
        builder.Environment.IsEnvironment("E2E") &&
        builder.Configuration.GetValue<bool>("NFeSettings:UseStubProvider"));
    CustomerIoC.Configure(builder.Services, builder.Configuration);
    EmployeeIoC.Configure(builder.Services, builder.Configuration);
    SupplierIoC.Configure(builder.Services, builder.Configuration);
    EnterpriseIoC.Configure(builder.Services, builder.Configuration);
    EmailIoC.Configure(builder.Services, builder.Configuration);
    OpportunityRadarIoC.Configure(builder.Services, builder.Configuration);
    ReportsIoC.Configure(builder.Services, builder.Configuration);
    PdvIoC.Configure(builder.Services, builder.Configuration);
    StorefrontIoC.Configure(builder.Services, builder.Configuration);
    AiIoC.Configure(builder.Services, builder.Configuration);
    builder.Services.AddScoped<IAiProgressNotifier, AiProgressNotifier>();
    SharedIoC.Configure(builder.Services, builder.Configuration);

    builder.Services.AddHttpClient<IRecaptchaVerifier, RecaptchaVerifier>(client =>
    {
        client.Timeout = TimeSpan.FromSeconds(5);
    });

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

static void AddRequestRateLimit(WebApplicationBuilder builder) =>
    StorefrontRateLimit.AddIpRateLimiting(builder.Services, builder.Configuration);

/// <summary>
/// Bounds how much work the process will accept at once, so overload sheds instead of queueing.
/// <para>
/// Measured 2026-09-15 under a heavy read mix: with nothing bounding intake the API accepted
/// everything offered, latency went from ~200ms straight past 32 seconds (the top histogram bucket),
/// the working set grew from 1.7 GiB to 13 GiB, and the load generator sat at 4.7% CPU because it
/// was simply waiting. That is not running out of capacity, it is queueing without limit - a client
/// that has already given up is still holding a request the server is still working on.
/// </para>
/// <para>
/// A rejected request is a better answer than one served after half a minute: the caller learns
/// immediately, retries are cheap, and the queue never becomes the thing that kills the process.
/// Both numbers are configuration because the right value depends on the hardware, and the defaults
/// here are deliberately generous - this is a backstop against collapse, not a throughput knob.
/// </para>
/// </summary>
static void AddOverloadProtection(WebApplicationBuilder builder)
{
    var section = builder.Configuration.GetSection("OverloadProtection");

    var permits = section.GetValue<int?>("MaxConcurrentRequests") ?? 400;
    var queue = section.GetValue<int?>("QueueLimit") ?? 200;
    var timeoutSeconds = section.GetValue<int?>("RequestTimeoutSeconds") ?? 30;

    builder.Services.AddRequestTimeouts(options =>
    {
        options.DefaultPolicy = new RequestTimeoutPolicy
        {
            Timeout = TimeSpan.FromSeconds(timeoutSeconds),
            TimeoutStatusCode = StatusCodes.Status503ServiceUnavailable
        };
    });

    builder.Services.AddRateLimiter(options =>
    {
        options.RejectionStatusCode = StatusCodes.Status503ServiceUnavailable;

        options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        {
            var path = context.Request.Path;
            if (path.StartsWithSegments("/health") || path.StartsWithSegments("/metrics"))
                return RateLimitPartition.GetNoLimiter("exempt");

            return RateLimitPartition.GetConcurrencyLimiter("global", _ => new ConcurrencyLimiterOptions
            {
                PermitLimit = permits,
                QueueLimit = queue,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst
            });
        });

        options.OnRejected = (context, _) =>
        {
            context.HttpContext.Response.Headers.RetryAfter = "1";
            Log.Logger.Warning(
                "Shedding request under load. Path={Path} Permits={Permits} Queue={Queue}",
                context.HttpContext.Request.Path.Value,
                permits,
                queue);
            return ValueTask.CompletedTask;
        };
    });
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
