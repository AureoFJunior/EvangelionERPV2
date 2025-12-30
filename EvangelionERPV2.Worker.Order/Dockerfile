# syntax=docker/dockerfile:1

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS base
ARG APP_UID=0
USER $APP_UID
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

COPY EvangelionERPV2.sln ./
COPY EvangelionERPV2.Worker.Order/EvangelionERPV2.Worker.Order.csproj EvangelionERPV2.Worker.Order/
COPY EvangelionERPV2.Order.Application/*.csproj EvangelionERPV2.Order.Application/
COPY EvangelionERPV2.Order.Domain/*.csproj EvangelionERPV2.Order.Domain/
COPY EvangelionERPV2.Order.Infra/*.csproj EvangelionERPV2.Order.Infra/
COPY EvangelionERPV2.Product.Application/*.csproj EvangelionERPV2.Product.Application/
COPY EvangelionERPV2.Product.Domain/*.csproj EvangelionERPV2.Product.Domain/
COPY EvangelionERPV2.Product.Infra/*.csproj EvangelionERPV2.Product.Infra/
COPY EvangelionERPV2.Shared/*.csproj EvangelionERPV2.Shared/

RUN --mount=type=cache,target=/root/.nuget/packages \
    dotnet restore "EvangelionERPV2.Worker.Order/EvangelionERPV2.Worker.Order.csproj"

COPY . .

WORKDIR /src/EvangelionERPV2.Worker.Order
RUN dotnet publish "./EvangelionERPV2.Worker.Order.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "EvangelionERPV2.Worker.Order.dll"]
