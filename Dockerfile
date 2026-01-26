FROM mcr.microsoft.com/dotnet/sdk:3.1-buster AS build

WORKDIR /src
COPY TS3AudioBot.sln ./
COPY TS3AudioBot/TS3AudioBot.csproj TS3AudioBot/
COPY TSLib/TSLib.csproj TSLib/
COPY TS3ABotUnitTests/TS3ABotUnitTests.csproj TS3ABotUnitTests/
RUN dotnet restore TS3AudioBot.sln
COPY . .
RUN dotnet publish TS3AudioBot/TS3AudioBot.csproj -c Release -o /opt/TS3AudioBot

FROM node:22-bookworm-slim AS web-build

WORKDIR /web
COPY WebInterface/package.json WebInterface/package-lock.json ./
RUN npm ci --no-audit --no-fund
COPY WebInterface/ ./
RUN npm run build

FROM mcr.microsoft.com/dotnet/aspnet:3.1-buster-slim
LABEL description="TS3Audiobot Dockerized"
LABEL licenseUrl="https://github.com/Splamy/TS3AudioBot/blob/master/LICENSE"
LABEL url="https://github.com/Splamy/TS3AudioBot"
LABEL supportUrl="https://github.com/Splamy/TS3AudioBot/issues"
LABEL os="Linux"
LABEL arch="x64"

RUN apt-get update \
    && apt-get install -y --no-install-recommends ffmpeg curl openssl libopus0 \
    && rm -rf /tmp/* /var/tmp/* /var/lib/apt/lists/*

RUN curl -L https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp -o /usr/local/bin/youtube-dl \
    && chmod a+rx /usr/local/bin/youtube-dl

RUN useradd -ms /bin/bash -u 9999 ts3audiobot \
    && mkdir -p /data \
    && chown -R ts3audiobot:nogroup /data

COPY --from=build /opt/TS3AudioBot /opt/TS3AudioBot
COPY --from=web-build /web/dist/ /opt/TS3AudioBot/WebInterface/

WORKDIR /data
VOLUME /data
USER ts3audiobot
EXPOSE 58913
ENTRYPOINT ["dotnet", "/opt/TS3AudioBot/TS3AudioBot.dll", "--non-interactive", "--stats-disabled"]
