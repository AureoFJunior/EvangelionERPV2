# This stage is used when running from VS in fast mode (Default for Debug configuration)
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8080
EXPOSE 8081


# This stage is used to build the service project
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["EvangelionERPV2.Web/EvangelionERPV2.Web.csproj", "EvangelionERPV2.Web/"]
COPY ["EvangelionERPV2.Customer.Application/EvangelionERPV2.CustomerModule.Application.csproj", "EvangelionERPV2.Customer.Application/"]
COPY ["EvangelionERPV2.Customer.Domain/EvangelionERPV2.CustomerModule.Domain.csproj", "EvangelionERPV2.Customer.Domain/"]
COPY ["EvangelionERPV2.Customer.Infra/EvangelionERPV2.CustomerModule.Infra.csproj", "EvangelionERPV2.Customer.Infra/"]
COPY ["EvangelionERPV2.Shared/EvangelionERPV2.Shared.csproj", "EvangelionERPV2.Shared/"]
COPY ["EvangelionERPV2.Enterprise.Domain/EvangelionERPV2.EnterpriseModule.Domain.csproj", "EvangelionERPV2.Enterprise.Domain/"]
COPY ["EvangelionERPV2.Enterprise.Infra/EvangelionERPV2.EnterpriseModule.Infra.csproj", "EvangelionERPV2.Enterprise.Infra/"]
COPY ["EvangelionERPV2.Email.Application/EvangelionERPV2.EmailModule.Application.csproj", "EvangelionERPV2.Email.Application/"]
COPY ["EvangelionERPV2.Email.Infra/EvangelionERPV2.EmailModule.Infra.csproj", "EvangelionERPV2.Email.Infra/"]
COPY ["EvangelionERPV2.Order.Application/EvangelionERPV2.OrderModule.Application.csproj", "EvangelionERPV2.Order.Application/"]
COPY ["EvangelionERPV2.Order.Domain/EvangelionERPV2.OrderModule.Domain.csproj", "EvangelionERPV2.Order.Domain/"]
COPY ["EvangelionERPV2.Order.Infra/EvangelionERPV2.OrderModule.Infra.csproj", "EvangelionERPV2.Order.Infra/"]
COPY ["EvangelionERPV2.Product.Application/EvangelionERPV2.ProductModule.Application.csproj", "EvangelionERPV2.Product.Application/"]
COPY ["EvangelionERPV2.Product.Domain/EvangelionERPV2.ProductModule.Domain.csproj", "EvangelionERPV2.Product.Domain/"]
COPY ["EvangelionERPV2.Product.Infra/EvangelionERPV2.ProductModule.Infra.csproj", "EvangelionERPV2.Product.Infra/"]
COPY ["EvangelionERPV2.Email.Domain/EvangelionERPV2.EmailModule.Domain.csproj", "EvangelionERPV2.Email.Domain/"]
COPY ["EvangelionERPV2.Enterprise.Application/EvangelionERPV2.EnterpriseModule.Application.csproj", "EvangelionERPV2.Enterprise.Application/"]
COPY ["EvangelionERPV2.User.Application/EvangelionERPV2.UserModule.Application.csproj", "EvangelionERPV2.User.Application/"]
COPY ["EvangelionERPV2.User.Domain/EvangelionERPV2.UserModule.Domain.csproj", "EvangelionERPV2.User.Domain/"]
COPY ["EvangelionERPV2.User.Infra/EvangelionERPV2.UserModule.Infra.csproj", "EvangelionERPV2.User.Infra/"]
COPY ["EvangelionERPV2.Worker.Order/EvangelionERPV2.Worker.Order.csproj", "EvangelionERPV2.Worker.Order/"]
COPY ["EvangelionERPV2.Worker.Email/EvangelionERPV2.Worker.Email.csproj", "EvangelionERPV2.Worker.Email/"]
RUN dotnet restore "./EvangelionERPV2.Web/EvangelionERPV2.Web.csproj"
COPY . .
WORKDIR "/src/EvangelionERPV2.Web"
RUN dotnet build "./EvangelionERPV2.Web.csproj" -c $BUILD_CONFIGURATION -o /app/build

# This stage is used to publish the service project to be copied to the final stage
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./EvangelionERPV2.Web.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

# This stage is used in production or when running from VS in regular mode (Default when not using the Debug configuration)
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "EvangelionERPV2.Web.dll"]
