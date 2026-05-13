# syntax=docker/dockerfile:1.7

FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0-noble AS build
ARG TARGETARCH
WORKDIR /src

COPY global.json ./
COPY shared.proj ./
COPY src/openmedstack.preator/openmedstack.preator.csproj src/openmedstack.preator/
COPY src/openmedstack.biosharp.annotationdb/openmedstack.biosharp.annotationdb.csproj src/openmedstack.biosharp.annotationdb/
COPY src/openmedstack.biosharp.io/openmedstack.biosharp.io.csproj src/openmedstack.biosharp.io/
COPY src/openmedstack.biosharp.model/openmedstack.biosharp.model.csproj src/openmedstack.biosharp.model/
COPY src/openmedstack.biosharp.calculations/openmedstack.biosharp.calculations.csproj src/openmedstack.biosharp.calculations/

RUN --mount=type=cache,target=/root/.nuget/packages \
    case "$TARGETARCH" in \
      amd64) rid=linux-x64 ;; \
      arm64) rid=linux-arm64 ;; \
      *) echo "Unsupported TARGETARCH: $TARGETARCH" >&2; exit 1 ;; \
    esac && \
    dotnet restore src/openmedstack.preator/openmedstack.preator.csproj -r "$rid"

COPY src/ src/

RUN --mount=type=cache,target=/root/.nuget/packages \
    case "$TARGETARCH" in \
      amd64) rid=linux-x64 ;; \
      arm64) rid=linux-arm64 ;; \
      *) echo "Unsupported TARGETARCH: $TARGETARCH" >&2; exit 1 ;; \
    esac && \
    dotnet publish src/openmedstack.preator/openmedstack.preator.csproj \
      -c Release \
      -r "$rid" \
      --self-contained true \
      --no-restore \
      -p:PublishSingleFile=true \
      -p:EnableCompressionInSingleFile=true \
      -p:PublishTrimmed=true \
      -p:SuppressTrimAnalysisWarnings=true \
      -p:TrimmerRemoveSymbols=true \
      -p:StripSymbols=true \
      -p:DebugType=None \
      -p:DebugSymbols=false \
      -p:InvariantGlobalization=true \
      -o /app/publish

FROM mcr.microsoft.com/dotnet/runtime-deps:10.0-noble-chiseled AS final
WORKDIR /app

ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1

COPY --from=build --chown=$APP_UID:$APP_UID /app/publish/ ./

USER $APP_UID

ENTRYPOINT ["/app/preator"]
