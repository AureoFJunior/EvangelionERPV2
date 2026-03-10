# syntax=docker/dockerfile:1

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS base
ARG APP_UID=0
USER $APP_UID
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

COPY EvangelionERPV2.sln ./
COPY EvangelionERPV2.Worker.Email/EvangelionERPV2.Worker.Email.csproj EvangelionERPV2.Worker.Email/
COPY EvangelionERPV2.Email.Application/*.csproj EvangelionERPV2.Email.Application/
COPY EvangelionERPV2.Email.Domain/*.csproj EvangelionERPV2.Email.Domain/
COPY EvangelionERPV2.Email.Infra/*.csproj EvangelionERPV2.Email.Infra/
COPY EvangelionERPV2.Enterprise.Infra/*.csproj EvangelionERPV2.Enterprise.Infra/
COPY EvangelionERPV2.Enterprise.Domain/*.csproj EvangelionERPV2.Enterprise.Domain/
COPY EvangelionERPV2.Bills.Application/*.csproj EvangelionERPV2.Bills.Application/
COPY EvangelionERPV2.Bills.Domain/*.csproj EvangelionERPV2.Bills.Domain/
COPY EvangelionERPV2.Bills.Infra/*.csproj EvangelionERPV2.Bills.Infra/
COPY EvangelionERPV2.Order.Application/*.csproj EvangelionERPV2.Order.Application/
COPY EvangelionERPV2.Order.Domain/*.csproj EvangelionERPV2.Order.Domain/
COPY EvangelionERPV2.Order.Infra/*.csproj EvangelionERPV2.Order.Infra/
COPY EvangelionERPV2.Product.Application/*.csproj EvangelionERPV2.Product.Application/
COPY EvangelionERPV2.Product.Domain/*.csproj EvangelionERPV2.Product.Domain/
COPY EvangelionERPV2.Product.Infra/*.csproj EvangelionERPV2.Product.Infra/
COPY EvangelionERPV2.Shared/*.csproj EvangelionERPV2.Shared/

RUN dotnet restore "EvangelionERPV2.Worker.Email/EvangelionERPV2.Worker.Email.csproj"

COPY . .

WORKDIR /src/EvangelionERPV2.Worker.Email
RUN dotnet publish "./EvangelionERPV2.Worker.Email.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "EvangelionERPV2.Worker.Email.dll"]
