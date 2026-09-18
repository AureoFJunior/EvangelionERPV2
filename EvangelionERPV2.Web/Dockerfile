# syntax=docker/dockerfile:1

# This stage is used when running from VS in fast mode (Default for Debug configuration)
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
ARG APP_UID=0
# libfontconfig1: BoletoNetCore renders the bill barcode through SkiaSharp, whose native
# libSkiaSharp.so links against libfontconfig — without it bill generation dies with
# DllNotFoundException.
# The rest are the shared libraries headless Chromium needs to start; PuppeteerSharp downloads the
# browser itself but cannot launch it on a bare image, failing bill PDFs with ProcessException.
RUN apt-get update \
    && apt-get install -y --no-install-recommends \
        libfontconfig1 \
        libnss3 \
        libatk1.0-0t64 \
        libatk-bridge2.0-0t64 \
        libcups2t64 \
        libdrm2 \
        libxkbcommon0 \
        libxcomposite1 \
        libxdamage1 \
        libxfixes3 \
        libxrandr2 \
        libgbm1 \
        libpango-1.0-0 \
        libcairo2 \
        libasound2t64 \
    && rm -rf /var/lib/apt/lists/*
USER $APP_UID
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
EXPOSE 8081

# Build and publish the web project. The Docker build context must be the repository root
# so that all referenced projects are available during restore and build.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

# Copy solution and project files first to maximize restore layer caching
COPY global.json ./
COPY Directory.Packages.props ./
COPY EvangelionERPV2.sln ./
COPY EvangelionERPV2.Web/EvangelionERPV2.Web.csproj EvangelionERPV2.Web/
COPY EvangelionERPV2.Audit.Application/*.csproj EvangelionERPV2.Audit.Application/
COPY EvangelionERPV2.Audit.Domain/*.csproj EvangelionERPV2.Audit.Domain/
COPY EvangelionERPV2.Audit.Infra/*.csproj EvangelionERPV2.Audit.Infra/
COPY EvangelionERPV2.Bills.Application/*.csproj EvangelionERPV2.Bills.Application/
COPY EvangelionERPV2.Bills.Domain/*.csproj EvangelionERPV2.Bills.Domain/
COPY EvangelionERPV2.Bills.Infra/*.csproj EvangelionERPV2.Bills.Infra/
COPY EvangelionERPV2.Customer.Application/*.csproj EvangelionERPV2.Customer.Application/
COPY EvangelionERPV2.Customer.Domain/*.csproj EvangelionERPV2.Customer.Domain/
COPY EvangelionERPV2.Customer.Infra/*.csproj EvangelionERPV2.Customer.Infra/
COPY EvangelionERPV2.Email.Application/*.csproj EvangelionERPV2.Email.Application/
COPY EvangelionERPV2.Email.Domain/*.csproj EvangelionERPV2.Email.Domain/
COPY EvangelionERPV2.Email.Infra/*.csproj EvangelionERPV2.Email.Infra/
COPY EvangelionERPV2.Enterprise.Application/*.csproj EvangelionERPV2.Enterprise.Application/
COPY EvangelionERPV2.Enterprise.Domain/*.csproj EvangelionERPV2.Enterprise.Domain/
COPY EvangelionERPV2.Enterprise.Infra/*.csproj EvangelionERPV2.Enterprise.Infra/
COPY EvangelionERPV2.NFe.Application/*.csproj EvangelionERPV2.NFe.Application/
COPY EvangelionERPV2.NFe.Domain/*.csproj EvangelionERPV2.NFe.Domain/
COPY EvangelionERPV2.NFe.Infra/*.csproj EvangelionERPV2.NFe.Infra/
COPY EvangelionERPV2.Order.Application/*.csproj EvangelionERPV2.Order.Application/
COPY EvangelionERPV2.Order.Domain/*.csproj EvangelionERPV2.Order.Domain/
COPY EvangelionERPV2.Order.Infra/*.csproj EvangelionERPV2.Order.Infra/
COPY EvangelionERPV2.Product.Application/*.csproj EvangelionERPV2.Product.Application/
COPY EvangelionERPV2.Product.Domain/*.csproj EvangelionERPV2.Product.Domain/
COPY EvangelionERPV2.Product.Infra/*.csproj EvangelionERPV2.Product.Infra/
COPY EvangelionERPV2.Shared/*.csproj EvangelionERPV2.Shared/
COPY EvangelionERPV2.User.Application/*.csproj EvangelionERPV2.User.Application/
COPY EvangelionERPV2.User.Domain/*.csproj EvangelionERPV2.User.Domain/
COPY EvangelionERPV2.User.Infra/*.csproj EvangelionERPV2.User.Infra/

RUN dotnet restore "EvangelionERPV2.Web/EvangelionERPV2.Web.csproj"

# Copy the remaining source now that restore is cached
COPY . .

WORKDIR "/src/EvangelionERPV2.Web"
RUN dotnet build "./EvangelionERPV2.Web.csproj" -c $BUILD_CONFIGURATION -o /app/build

# Publish stage
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./EvangelionERPV2.Web.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

# Final runtime image
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "EvangelionERPV2.Web.dll"]
