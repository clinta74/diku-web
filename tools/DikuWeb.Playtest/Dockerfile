# The playtesting apparatus, containerised so a load run can be given a CPU budget of its own.
#
# Separate from the root Dockerfile on purpose. That one builds the server; this one builds the
# thing that measures it, and the entire point of a load run is that the two are not competing for
# the same cores by accident. Two images means docker-compose.load.yml can say, in one place and
# out loud, how much machine each side gets — which is the number PLAN.md §11 leaves unsaid when
# it claims "200 concurrent sessions on one process" without naming the process's budget.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

WORKDIR /src

# Same reasoning as the server image: none of the csproj files carries a TargetFramework or a
# package version, because both live in the root props files. Restoring without them fails as
# NETSDK1013 and reads like a broken project file. .editorconfig is build input too, since
# Directory.Build.props turns on EnforceCodeStyleInBuild.
COPY ["Directory.Build.props", "Directory.Packages.props", ".editorconfig", "./"]

COPY ["tools/DikuWeb.Playtest/DikuWeb.Playtest.csproj", "tools/DikuWeb.Playtest/"]
COPY ["src/DikuWeb.Domain/DikuWeb.Domain.csproj", "src/DikuWeb.Domain/"]

RUN dotnet restore "tools/DikuWeb.Playtest/DikuWeb.Playtest.csproj"

COPY . .

RUN dotnet publish "tools/DikuWeb.Playtest/DikuWeb.Playtest.csproj" \
    -c Release \
    -o /app/publish \
    --no-restore \
    --self-contained=false

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS runtime

WORKDIR /app

COPY --from=build --chown=$APP_UID:$APP_UID /app/publish .

# The plans travel with the image so a run needs nothing mounted to be reproducible, and a run
# directory is written under /runs, which compose binds out to the host — a load report nobody can
# read afterwards is not a measurement.
COPY --from=build --chown=$APP_UID:$APP_UID /src/tools/DikuWeb.Playtest/Plans ./Plans

USER $APP_UID

# Server GC, and this is not a detail. Two hundred sessions each hold an open SSE stream and a
# transcript; under workstation GC the apparatus spends its time collecting and starts reporting
# its own pauses as the server's slowness. The one thing a load generator must not do is become
# the bottleneck it is measuring.
ENV DOTNET_gcServer=1

ENTRYPOINT ["dotnet", "DikuWeb.Playtest.dll"]
