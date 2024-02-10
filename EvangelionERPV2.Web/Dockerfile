# Build stage
FROM mcr.microsoft.com/dotnet/sdk:7.0 AS build
WORKDIR /src

COPY ["EvangelionERPV2.Web.csproj", "EvangelionERPV2.Web/"]
COPY ["/EvangelionERPV2.Application/EvangelionERPV2.Application.csproj", "EvangelionERPV2.Application/"]
COPY ["EvangelionERPV2.Domain/EvangelionERPV2.Domain.csproj", "EvangelionERPV2.Domain/"]
COPY ["EvangelionERPV2.Infra/EvangelionERPV2.Infra.csproj", "EvangelionERPV2.Infra/"]
RUN dotnet restore "EvangelionERPV2.Web/EvangelionERPV2.Web.csproj"
COPY . .
WORKDIR "./EvangelionERPV2.Web"
RUN dotnet build "EvangelionERPV2.Web.csproj" -c Release -o /app/build

# Publish stage
FROM build AS publish
RUN dotnet publish "EvangelionERPV2.Web.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Final stage
FROM mcr.microsoft.com/dotnet/aspnet:7.0 AS final
WORKDIR /app

# Copy published files from the 'publish' stage
COPY --from=publish /app/publish .

EXPOSE 80

CMD ["dotnet", "EvangelionERPV2.Web.dll"]