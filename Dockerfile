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
#
# TrBlazeUI comes from the PRIVATE GitHub Packages feed, which refuses anonymous reads, so the
# restore needs a token (build with BuildKit):
#   docker build --secret id=nuget_pat,env=TRBLAZEUI_PACKAGES_TOKEN -t tflens .
# The token arrives as a BuildKit secret: it exists only for this RUN, is never written to a layer and
# never shows in `docker history`. It is NOT an ARG or ENV (both land in the image), and nuget.config
# is not copied (it carries no credentials by design). The generated config lives under /tmp and is
# deleted inside the same RUN. The secret id `nuget_pat` is the contract with deploy.yml; the same
# pattern as TechieBlog's Dockerfile. Without a valid token the restore fails on TrBlazeUI, which is
# the right outcome: there is no silent fallback that could ship an image without its UI library.
COPY src/TfLens/TfLens.csproj src/TfLens/
COPY src/TfLens.Core/TfLens.Core.csproj src/TfLens.Core/
RUN --mount=type=secret,id=nuget_pat \
    set -eu; \
    NUGET_PAT="$(cat /run/secrets/nuget_pat 2>/dev/null || true)"; \
    { \
      echo '<?xml version="1.0" encoding="utf-8"?>'; \
      echo '<configuration>'; \
      echo '  <packageSources>'; \
      echo '    <clear />'; \
      echo '    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />'; \
      echo '    <add key="TrBlazeUI" value="https://nuget.pkg.github.com/techierathore/index.json" />'; \
      echo '  </packageSources>'; \
      if [ -n "$NUGET_PAT" ]; then \
        echo '  <packageSourceCredentials>'; \
        echo '    <TrBlazeUI>'; \
        echo '      <add key="Username" value="techierathore" />'; \
        echo "      <add key=\"ClearTextPassword\" value=\"$NUGET_PAT\" />"; \
        echo '    </TrBlazeUI>'; \
        echo '  </packageSourceCredentials>'; \
      fi; \
      echo '</configuration>'; \
    } > /tmp/nuget.docker.config; \
    dotnet restore src/TfLens/TfLens.csproj --configfile /tmp/nuget.docker.config; \
    rm -f /tmp/nuget.docker.config

COPY src/ src/

# THE SECOND RESTORE IS LOAD-BEARING. DO NOT DELETE IT AS REDUNDANT.
# The restore above ran when only the .csproj files existed, which keeps the layer cache. But that
# restore writes an obj/ state without the Blazor FRAMEWORK static web assets, and `publish
# --no-restore` reuses it: the image builds and /healthz answers, yet /app/wwwroot has no _framework,
# blazor.web.js 404s and no page is interactive. Measured 2026-09-15: the image built without this
# restore had 0 files in wwwroot/_framework and no blazor.web.js endpoint, where a local publish has
# both. TechieBlog found and fixed the same defect on 2026-08-11. Every package is already in the
# cache, so this downloads nothing; the secret is mounted again because NuGet still reads the sources.
RUN --mount=type=secret,id=nuget_pat \
    set -eu; \
    NUGET_PAT="$(cat /run/secrets/nuget_pat 2>/dev/null || true)"; \
    { \
      echo '<?xml version="1.0" encoding="utf-8"?>'; \
      echo '<configuration>'; \
      echo '  <packageSources>'; \
      echo '    <clear />'; \
      echo '    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />'; \
      echo '    <add key="TrBlazeUI" value="https://nuget.pkg.github.com/techierathore/index.json" />'; \
      echo '  </packageSources>'; \
      if [ -n "$NUGET_PAT" ]; then \
        echo '  <packageSourceCredentials>'; \
        echo '    <TrBlazeUI>'; \
        echo '      <add key="Username" value="techierathore" />'; \
        echo "      <add key=\"ClearTextPassword\" value=\"$NUGET_PAT\" />"; \
        echo '    </TrBlazeUI>'; \
        echo '  </packageSourceCredentials>'; \
      fi; \
      echo '</configuration>'; \
    } > /tmp/nuget.docker.config; \
    dotnet restore src/TfLens/TfLens.csproj --configfile /tmp/nuget.docker.config; \
    rm -f /tmp/nuget.docker.config

# `--no-restore` stays: the publish can never re-resolve packages without the secret.
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
