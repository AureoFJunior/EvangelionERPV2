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
using FluentValidation.AspNetCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Versioning;
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

// prometheus-net connects its Meter adapter to every System.Diagnostics.Metrics instrument in the
// process, and two of those meters are pure cost here.
//
// Microsoft.AspNetCore.* publishes http_server_request_duration labelled with http_route - a second
// request histogram measuring exactly what UseHttpMetrics already measures, at ~19 series per route.
// Across this API's 184 controller actions that is thousands of duplicate series, and it grows with
// the codebase. Private.InternalDiagnostics.* is msquic internals nobody here reads.
//
// Measured on 8.2.1 with three routes: 262 series down to 166, the per-route duplication gone, and
// the metrics the dashboards actually query all intact. It takes ~45s AFTER the first request for
// these to finish appearing, so a scrape taken seconds after boot understates them - which is why
// the count check in the observability docs says to use a process that has been up a minute.
//
// The Kestrel and hosting *EventCounters* (current_connections, request_queue_length and friends)
// come from a different adapter, are fixed-cardinality, and are deliberately kept.
Metrics.ConfigureMeterAdapter(options =>
    options.InstrumentFilterPredicate = instrument =>
        !instrument.Meter.Name.StartsWith("Microsoft.AspNetCore")
        && !instrument.Meter.Name.StartsWith("Private.InternalDiagnostics"));

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
    AddOverloadProtection(builder);
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

    // Order matters: the IP limiter above answers "is this caller asking too often", this answers
    // "is the server already doing all it can". A caller inside its quota still gets shed when the
    // process is saturated, which is the point.
    app.UseRequestTimeouts();
    app.UseRateLimiter();

    app.UseCors(DefaultCorsPolicyName);

    app.UseAuthentication();
    app.UseMiddleware<RequestLoggingMiddleware>();
    app.UseMiddleware<ActiveTenantEnforcementMiddleware>();
    app.UseAuthorization();

    app.MapMetrics("/metrics").RequireAuthorization(RbacPolicies.Metrics.Read);

    // Same registry, second door, no auth on it. Prometheus scrapes every 15 seconds for ever and
    // the tokens this API issues expire, so the alternative was a never-expiring Supervisor token
    // sitting in a scraper's config file - a credential that can read every business endpoint,
    // handed out so that something can read a counter.
    //
    // 9101 is never published: no host port in docker-compose.yml, no security group rule on ECS
    // (the agent sidecar shares the task's network namespace). The readers are containers on the
    // same network, which is the boundary that already holds the connection string. It is a real
    // narrowing and not a free lunch - some series carry an enterprise_id label - so if that
    // network ever stops being a boundary, this has to be revisited.
    // KestrelMetricServer, NOT app.UseMetricServer(port: 9101). The latter looks like it opens a
    // second listener and does not: it registers the metrics middleware filtered to that port, so
    // the port still has to be bound by something else. Shipped that way it left 9101 refusing
    // connections while the app looked perfectly healthy - verified against 8.2.1 in a container,
    // where only :::8080 was ever listening. This binds *:9101 for real, and is what the three
    // workers already do.
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
    // SQL Server accepts connections before it has finished attaching its databases, and EF reports
    // a history table it cannot read as "every migration is pending" rather than throwing — so the
    // catch below never fires and a merely slow database gets announced as an empty one. That is
    // not a hypothetical: on 2026-09-11 this logged all 64 as missing against a database that had
    // all 64 applied. Retrying while nothing at all looks applied is what tells the two apart.
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

            // Nothing applied at all, with migrations waiting, is a database that is not ready far
            // more often than one that is genuinely blank. A real blank database still reports this
            // on the last attempt, which is the honest answer for a first deploy.
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
                // A database that cannot be reached at boot is DatabaseHealthCheck's job to report,
                // not a reason to take the process down here.
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

    // Publishes the same results as series, so a dependency going down can raise an alert instead
    // of waiting for somebody to think of opening /health. The host drives the timer itself.
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
        // Nothing should take half a minute. Without this a request has no upper bound at all and
        // can hold a thread and a connection long after the client has gone.
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
            // Health and metrics must answer while the API is shedding: an orchestrator that cannot
            // reach /health during overload will kill a container that is defending itself, and a
            // scrape that fails takes away the graph showing why.
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
            // Tell the caller it is worth trying again; a 503 with no hint reads as a fault.
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
