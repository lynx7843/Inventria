# Build the published app and a self-contained EF Core migration bundle in one
# SDK stage, then ship only the ASP.NET Core runtime - the SDK and dotnet-ef
# never need to exist in the image that actually runs in production.
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY backend/inventria.csproj backend/
RUN dotnet restore backend/inventria.csproj

COPY backend/ backend/
RUN dotnet publish backend/inventria.csproj -c Release -o /app/publish --no-restore

# `ef migrations bundle` produces a standalone native executable that applies
# migrations without needing dotnet-ef or the SDK at container startup - see
# docker-entrypoint.sh. Building it re-executes Program.cs's top-level startup
# code to discover the DbContext, which throws before that if Jwt:Key looks
# unset - true during a build, which has no access to the real one from
# docker-compose's environment. This placeholder only ever exists in this
# build stage; docker-compose sets the real Jwt__Key on the final image's
# container at runtime, overriding it.
ENV Jwt__Key=docker-image-build-time-placeholder-never-used-at-runtime-32b
RUN dotnet tool install --global dotnet-ef --version 9.* \
    && ~/.dotnet/tools/dotnet-ef migrations bundle \
        --project backend/inventria.csproj \
        --configuration Release \
        --self-contained \
        --target-runtime linux-x64 \
        --output /app/publish/efbundle

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
COPY backend/docker-entrypoint.sh .
RUN chmod +x docker-entrypoint.sh efbundle

ENTRYPOINT ["./docker-entrypoint.sh"]
