# The build is the same for every platform (the app is portable .NET), so it runs natively and only the runtime image differs.
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props Romdex.sln ./
COPY src/Romdex.Contracts/Romdex.Contracts.csproj src/Romdex.Contracts/
COPY src/Romdex.Client/Romdex.Client.csproj src/Romdex.Client/
COPY src/Romdex.Web/Romdex.Web.csproj src/Romdex.Web/
COPY src/Romdex.Plugins.DatFolder/Romdex.Plugins.DatFolder.csproj src/Romdex.Plugins.DatFolder/
COPY src/Romdex.Core/Romdex.Core.csproj src/Romdex.Core/
RUN dotnet restore src/Romdex.Core/Romdex.Core.csproj
COPY src/ src/
# The web app carries the user guide and changelog, for Settings → Help.
COPY docs/guide/ docs/guide/
COPY CHANGELOG.md ./
# Releases build with the tag's version; anything else keeps the one in Directory.Build.props.
# The commit, which the About page links to, is passed in too: .git isn't part of the build context.
ARG VERSION=""
ARG COMMIT=""
RUN dotnet publish src/Romdex.Core/Romdex.Core.csproj -c Release -o /app --no-restore \
    ${VERSION:+-p:Version=$VERSION} ${COMMIT:+-p:SourceRevisionId=$COMMIT}

FROM mcr.microsoft.com/dotnet/aspnet:10.0
LABEL org.opencontainers.image.title="Romdex" \
      org.opencontainers.image.description="Know what's in your game library, what's missing, and keep it tidy." \
      org.opencontainers.image.source="https://github.com/dextrd/Romdex" \
      org.opencontainers.image.licenses="MIT"
WORKDIR /app
# Staging is shared with the igir worker and the intake plugins, which run as other users. Whichever image first
# mounts a new volume sets its owner, so every image makes it writable by all, sticky so nobody removes another's jobs.
RUN mkdir -p /config /roms /dats /staging /plugins \
    && chown -R $APP_UID /config /plugins \
    && chmod 1777 /staging
COPY --from=build /app .
ENV ASPNETCORE_HTTP_PORTS=8080 \
    Romdex__DataPath=/config \
    Romdex__LibraryPath=/roms \
    Romdex__DatPath=/dats \
    Romdex__StagingPath=/staging \
    Romdex__PluginsPath=/plugins
EXPOSE 8080
VOLUME ["/config", "/staging"]
USER $APP_UID
ENTRYPOINT ["dotnet", "Romdex.Core.dll"]
