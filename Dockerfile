# TfLens — one multi-stage image for the single executable head (BRD-77, ADR-005).
#
# The same binary serves the UI and runs the command verbs, so a parity run
# (`docker exec tflens dotnet TfLens.dll export --user 1`) exercises exactly the code the pages use.
# No secret is baked in: everything sensitive arrives as a PascalCase environment variable.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source

# Restore against the two project files the head needs, so the layer caches across source-only
# changes. The test projects are deliberately not copied: the image ships the app, and coupling the
# image build to a test project's existence would break `docker build` every time tests are reshuffled.
COPY src/TfLens/TfLens.csproj src/TfLens/
COPY src/TfLens.Core/TfLens.Core.csproj src/TfLens.Core/
RUN dotnet restore src/TfLens/TfLens.csproj

COPY src/ src/
RUN dotnet publish src/TfLens/TfLens.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# wget backs the compose healthcheck against /healthz.
# libgssapi-krb5-2 is needed by Npgsql: it probes for GSSAPI/Kerberos when opening a connection, and
# without it every startup and every command verb prints
#   "Error: libgssapi_krb5.so.2: cannot open shared object file"
# before carrying on. The connection works either way, but an error line on a healthy boot trains
# people to ignore error lines.
RUN apt-get update \
    && apt-get install -y --no-install-recommends wget libgssapi-krb5-2 \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish ./
COPY database/ ./database/

# Both are volume mount points: the raw archive under data/ is the rebuild source and the only thing
# that must survive the container, and the rolling Serilog file sink writes to logs/ (Coding Standards
# §Logging). Declared as volumes so the image is correct even when run without compose (BRD-77).
RUN mkdir -p /app/data /app/logs && chmod 0775 /app/data /app/logs
VOLUME ["/app/data", "/app/logs"]

ENV ASPNETCORE_URLS=http://+:8080 \
    TfLensDataRoot=/app/data \
    DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false

EXPOSE 8080

# The app refuses to start without its required settings, so a misconfigured container stops here
# rather than at the first user's sign-in (BRD-9).
ENTRYPOINT ["dotnet", "TfLens.dll"]
