# syntax=docker/dockerfile:1

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
ARG APP_UID=0
USER $APP_UID
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

COPY global.json ./
COPY EvangelionERPV2.sln ./
COPY EvangelionERPV2.Worker.Notification/EvangelionERPV2.Worker.Notification.csproj EvangelionERPV2.Worker.Notification/
COPY EvangelionERPV2.Shared/*.csproj EvangelionERPV2.Shared/

RUN dotnet restore "EvangelionERPV2.Worker.Notification/EvangelionERPV2.Worker.Notification.csproj"

COPY . .

WORKDIR /src/EvangelionERPV2.Worker.Notification
RUN dotnet publish "./EvangelionERPV2.Worker.Notification.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "EvangelionERPV2.Worker.Notification.dll"]
