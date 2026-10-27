# PS2 Medius server (SOCOM II) — MUIS / MAS / MLS / DME / NAT / DNS / patch HTTP + DnaSDK DNAS in one container.
# Build context: the REPO ROOT (the server references ../DnaSDK); the root .dockerignore keeps the context small.
#   docker build -f CodeDesigner.Ps2.Multiplayer/Dockerfile -t socom2-server .
#   az acr build -r <registry> -t socom2-server:<tag> -f CodeDesigner.Ps2.Multiplayer/Dockerfile .

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY CodeDesigner.Ps2.Multiplayer/CodeDesigner.Ps2.Multiplayer.csproj CodeDesigner.Ps2.Multiplayer/
COPY DnaSDK/DnaSDK.csproj DnaSDK/
RUN dotnet restore CodeDesigner.Ps2.Multiplayer/CodeDesigner.Ps2.Multiplayer.csproj
COPY . .
# SkipInstallDeploy: don't run the desktop "copy to %LOCALAPPDATA%" step inside the image build.
RUN dotnet publish CodeDesigner.Ps2.Multiplayer/CodeDesigner.Ps2.Multiplayer.csproj \
    -c Release -o /app --no-restore -p:SkipInstallDeploy=true -p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/runtime:8.0
WORKDIR /app
COPY --from=build /app .

# Container defaults — override any setting with Online__<Name> environment variables.
#   Online__ServerIp=auto + Online__PublicHostname=<fqdn>  → announce the IP the FQDN resolves to
#   Online__DnsForwardUnknown=false                        → never act as an open DNS resolver
#   Online__ExternalDnasServer=<ip|host>                   → use that DNAS server; the built-in one stays off
#   MULTIPLAYER_LOG_FILE=none                              → log to stdout only
#   Online__DataDirectory=/data                            → accounts, stats, clans, DNAS tickets + captures,
#                                                            patch/APACHE00.ZDB (mount a volume)
ENV Online__BindAddress=0.0.0.0 \
    Online__DataDirectory=/data \
    Online__ServerIp=auto \
    Online__DnsForwardUnknown=false \
    MULTIPLAYER_LOG_FILE=none \
    Logging__Console__FormatterName=simple \
    Logging__Console__FormatterOptions__SingleLine=true \
    Logging__Console__FormatterOptions__TimestampFormat="HH:mm:ss.fff "

VOLUME /data

# DNS (PS2 primary DNS), patch HTTP, DNAS HTTPS, NAT, DME, MUIS, MLS, MAS.
EXPOSE 53/udp 80/tcp 443/tcp 10069/udp 10070/tcp 10071/tcp 10075/tcp 10077/tcp

# Runs as root: the PS2 needs the fixed privileged ports 53 (DNS), 80 (patch) and 443 (DNAS).
ENTRYPOINT ["dotnet", "CodeDesigner.Ps2.Multiplayer.dll"]
