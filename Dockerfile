# Multi-stage Dockerfile for BookVerse API (.NET 10)
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8080
ENV ASPNETCORE_HTTP_PORTS=8080

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

# Copy project files for caching restore layer
COPY ["src/BookVerse.Domain/BookVerse.Domain.csproj", "src/BookVerse.Domain/"]
COPY ["src/BookVerse.Application/BookVerse.Application.csproj", "src/BookVerse.Application/"]
COPY ["src/BookVerse.Infrastructure/BookVerse.Infrastructure.csproj", "src/BookVerse.Infrastructure/"]
COPY ["src/BookVerse.Api/BookVerse.Api.csproj", "src/BookVerse.Api/"]

RUN dotnet restore "src/BookVerse.Api/BookVerse.Api.csproj"

# Copy full source code
COPY ["src/", "src/"]

# Build application
WORKDIR "/src/src/BookVerse.Api"
RUN dotnet build "BookVerse.Api.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "BookVerse.Api.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "BookVerse.Api.dll"]
