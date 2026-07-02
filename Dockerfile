# Multi-stage build for production-ready ASP.NET Core 10 application
# Stage 1: Build
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy only project file first for layer caching efficiency
COPY AeroponicIOT.csproj ./
RUN dotnet restore "AeroponicIOT.csproj"

# Copy application source and publish in Release mode
COPY . ./
RUN dotnet publish "AeroponicIOT.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Stage 2: Runtime image
FROM mcr.microsoft.com/dotnet/aspnet:10.0

# Set working directory and environment
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:80 \
    DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false \
    DOTNET_RUNNING_IN_CONTAINER=true

# Expose HTTP port
EXPOSE 80

# Create non-root user for security
RUN groupadd -g 1001 dotnet && useradd -u 1001 -g dotnet dotnet

# Copy published application from build stage
COPY --from=build --chown=dotnet:dotnet /app/publish ./

# Set user context
USER dotnet

# Health check configuration for orchestration
HEALTHCHECK --interval=30s --timeout=3s --start-period=10s --retries=3 \
    CMD curl -f http://localhost/health/live || exit 1

# Run application
ENTRYPOINT ["dotnet", "AeroponicIOT.dll"]
