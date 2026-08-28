# Multi-stage build for DikuWeb ASP.NET Core application
# Stage 1: Build
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

WORKDIR /src

# The build configuration comes first, and it is not optional. None of the csproj files below
# declares a TargetFramework, and none pins a package version: those live in Directory.Build.props
# and Directory.Packages.props, which apply to every project by sitting at the root. Restoring
# without them fails as NETSDK1013, "The TargetFramework value '' was not recognized" - which
# reads like a broken project file and is really a missing COPY.
#
# .editorconfig is build input here rather than editor preference, because Directory.Build.props
# turns on EnforceCodeStyleInBuild. Without it the container compiles under a different rule set
# from CI and from a developer's machine, which is the one thing a container build should not do.
COPY ["Directory.Build.props", "Directory.Packages.props", ".editorconfig", "./"]

# Project files next, so that editing source does not invalidate the restore layer.
COPY ["src/DikuWeb.Server/DikuWeb.Server.csproj", "src/DikuWeb.Server/"]
COPY ["src/DikuWeb.Engine/DikuWeb.Engine.csproj", "src/DikuWeb.Engine/"]
COPY ["src/DikuWeb.Persistence/DikuWeb.Persistence.csproj", "src/DikuWeb.Persistence/"]
COPY ["src/DikuWeb.Domain/DikuWeb.Domain.csproj", "src/DikuWeb.Domain/"]

# Restore dependencies
RUN dotnet restore "src/DikuWeb.Server/DikuWeb.Server.csproj"

# Copy source code
COPY . .

# What build this is, baked in at publish time. There is no git repository inside the
# runtime image and there should not be, so the version has to arrive as a build
# argument or not at all. The defaults are deliberately honest: a plain `docker build`
# produces an image that says 0.0.0/unknown rather than claiming to be a release.
#
# The SDK combines Version and SourceRevisionId into AssemblyInformationalVersion as
# `0.0.0+<sha>`, which is what BuildInfo reads back and /api/version reports.
ARG VERSION=0.0.0
ARG REVISION=unknown

# One publish, rather than a build followed by a --no-build publish. `dotnet build -o` moves
# OutputPath, which is not where a later `--no-build` publish looks for the assemblies - so that
# pair would have failed the moment the restore above started working. Publish builds by default,
# and the separate build step bought nothing.
RUN dotnet publish "src/DikuWeb.Server/DikuWeb.Server.csproj" \
    -c Release \
    -o /app/publish \
    --no-restore \
    --self-contained=false \
    -p:Version="$VERSION" \
    -p:SourceRevisionId="$REVISION"

# Stage 2: Runtime
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime

WORKDIR /app

# Install dumb-init for proper signal handling
RUN apt-get update && apt-get install -y --no-install-recommends dumb-init curl && rm -rf /var/lib/apt/lists/*

# Copy published app from build stage, owned by the user that will run it.
COPY --from=build --chown=$APP_UID:$APP_UID /app/publish .

# Non-root user for security. The base image already ships one for exactly this purpose - `app`,
# at $APP_UID (1654) - so there is nothing to create. Creating one by hand at uid/gid 1000 failed
# outright, because 1000 is already the `ubuntu` account in this image.
USER $APP_UID

# 8080, which is what the aspnet base image already listens on. This used to say 5000 in both
# places while the app listened on 8080, so the image's own health check could never pass. The
# compose files used to paper over that by setting ASPNETCORE_URLS to 5000 - which then did not
# match the client image's BACKEND_ORIGIN default of http://web:8080, so every /api call 502'd.
# 8080 is now the single answer in the app image, the client image, and the compose files.
EXPOSE 8080

# Health check endpoint
HEALTHCHECK --interval=30s --timeout=10s --retries=3 --start-period=20s \
    CMD curl -f http://localhost:8080/health || exit 1

# Use dumb-init to handle signals properly. The Debian package installs to /usr/bin, not /sbin -
# the wrong path here is not a startup warning, it is the container failing to create a process
# at all, with an error from runc rather than from anything in this application.
ENTRYPOINT ["/usr/bin/dumb-init", "--"]
CMD ["dotnet", "DikuWeb.Server.dll"]
