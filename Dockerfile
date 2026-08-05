# Multi-stage build for DikuWeb ASP.NET Core application
# Stage 1: Build
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

WORKDIR /src

# Copy project files
COPY ["src/DikuWeb.Server/DikuWeb.Server.csproj", "src/DikuWeb.Server/"]
COPY ["src/DikuWeb.Engine/DikuWeb.Engine.csproj", "src/DikuWeb.Engine/"]
COPY ["src/DikuWeb.Persistence/DikuWeb.Persistence.csproj", "src/DikuWeb.Persistence/"]
COPY ["src/DikuWeb.Domain/DikuWeb.Domain.csproj", "src/DikuWeb.Domain/"]

# Restore dependencies
RUN dotnet restore "src/DikuWeb.Server/DikuWeb.Server.csproj"

# Copy source code
COPY . .

# Build in Release mode
RUN dotnet build "src/DikuWeb.Server/DikuWeb.Server.csproj" -c Release -o /app/build

# Stage 2: Publish
FROM build AS publish

RUN dotnet publish "src/DikuWeb.Server/DikuWeb.Server.csproj" \
    -c Release \
    -o /app/publish \
    --no-build \
    --self-contained=false

# Stage 3: Runtime
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime

WORKDIR /app

# Install dumb-init for proper signal handling
RUN apt-get update && apt-get install -y --no-install-recommends dumb-init curl && rm -rf /var/lib/apt/lists/*

# Copy published app from publish stage
COPY --from=publish /app/publish .

# Non-root user for security
RUN groupadd -g 1000 dotnet && \
    useradd -u 1000 -g dotnet -s /sbin/nologin dotnet && \
    chown -R dotnet:dotnet /app

USER dotnet

# Expose port
EXPOSE 5000

# Health check endpoint
HEALTHCHECK --interval=30s --timeout=10s --retries=3 --start-period=20s \
    CMD curl -f http://localhost:5000/health || exit 1

# Use dumb-init to handle signals properly
ENTRYPOINT ["/sbin/dumb-init", "--"]
CMD ["dotnet", "DikuWeb.Server.dll"]
