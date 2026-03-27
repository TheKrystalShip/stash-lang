# syntax=docker/dockerfile:1

# Runtime base image — label as 'base' so users can swap it easily
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app

# Use the built-in non-root 'app' user (UID 1654) that ships with the aspnet image
USER root
RUN mkdir -p /app/data && chown app:app /app/data
USER app

EXPOSE 8080
VOLUME /app/data

# ── Build stage ──────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy project files first for layer-cached NuGet restore
COPY Stash.Core/Stash.Core.csproj         Stash.Core/
COPY Stash.Registry/Stash.Registry.csproj Stash.Registry/
RUN dotnet restore Stash.Registry/Stash.Registry.csproj

# Copy the rest of the source and build
COPY Stash.Core/     Stash.Core/
COPY Stash.Registry/ Stash.Registry/

# ── Publish stage ─────────────────────────────────────────────────────────────
FROM build AS publish
RUN dotnet publish Stash.Registry/Stash.Registry.csproj \
        -c Release \
        --no-restore \
        -o /app/publish

# ── Final runtime image ───────────────────────────────────────────────────────
FROM base AS final

LABEL org.opencontainers.image.title="Stash Registry" \
      org.opencontainers.image.description="Package registry server for the Stash language" \
      org.opencontainers.image.source="https://github.com/TheKrystalShip/stash-lang"

ENV ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_RUNNING_IN_CONTAINER=true

WORKDIR /app
COPY --from=publish --chown=app:app /app/publish .

ENTRYPOINT ["dotnet", "StashRegistry.dll"]
