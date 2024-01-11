using EvangelionERPV2.Application.DI;
using Microsoft.AspNetCore.Mvc.Versioning;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using System.Reflection;
using System.Text;
using System.Text.Json.Serialization;
using FluentValidation.AspNetCore;
using EvangelionERPV2.Web.FluentValidator;
using EvangelionERPV2.Web.Logging;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

#region Services

try
{
    IoC.Configure(builder.Services, builder.Configuration.GetConnectionString("DefaultConnection"), builder.Configuration);
    LogConfig.Configure();

    Log.Logger.Information("Starting Controllers");
    builder.Services.AddControllers()
        .AddFluentValidation(options =>
        {
            // Automatic registration of validators in assembly
            options.RegisterValidatorsFromAssemblyContaining<UserValidator>();
        })
        .AddJsonOptions(options =>
            options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

    Log.Logger.Information("Starting CORS");
    builder.Services.AddCors();

    Log.Logger.Information("Starting API Versioning");
    builder.Services.AddApiVersioning(opt =>
    {
        opt.DefaultApiVersion = new Microsoft.AspNetCore.Mvc.ApiVersion(1, 0);
        opt.AssumeDefaultVersionWhenUnspecified = true;
        opt.ReportApiVersions = true;
        opt.ApiVersionReader = ApiVersionReader.Combine(new UrlSegmentApiVersionReader(),
                                                        new HeaderApiVersionReader("x-api-version"),
                                                        new MediaTypeApiVersionReader("x-api-version"));
    });

    Log.Logger.Information("Starting Swagger");
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

    Log.Logger.Information("Starting JWT");
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

    #endregion

    #region App
    var app = builder.Build();

    Log.Logger.Information("Starting App Builder");

    // Configure the HTTP request pipeline.
    if (app.Environment.IsDevelopment())
    {
        Log.Logger.Information("In Dev ENV");
        app.UseSwagger();
        app.UseSwaggerUI(c =>
        {
            c.SwaggerEndpoint("/swagger/v1/swagger.json", "EvangelionERPV2");
            c.RoutePrefix = "swagger";
        });
    }

    app.UseHttpsRedirection();

    app.UseRouting();

    app.UseCors(x => x.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());

    app.UseAuthentication();
    app.UseAuthorization();

    app.MapControllers();

    Log.Logger.Information("Starting App Run");
    app.Run();
}
catch(Exception ex)
{
    Log.Logger.Error(ex.Message + ex.StackTrace);
}
#endregion