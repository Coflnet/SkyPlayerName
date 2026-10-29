FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /build
COPY HypixelSkyblock.commit HypixelSkyblock.commit
RUN git init dev && git -C dev remote add origin https://github.com/Coflnet/HypixelSkyblock.git \
    && git -C dev fetch --depth=1 origin "$(cat HypixelSkyblock.commit)" \
    && git -C dev checkout --detach FETCH_HEAD
WORKDIR /build/sky
COPY SkyPlayerName.csproj SkyPlayerName.csproj
RUN dotnet restore
COPY . .
RUN dotnet test Tests/SkyPlayerName.Tests.csproj --filter "TestCategory!=Integration"
RUN dotnet publish -c release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine
WORKDIR /app

COPY --from=build /app .

ENV ASPNETCORE_URLS=http://+:8000

USER app

ENTRYPOINT ["dotnet", "SkyPlayerName.dll", "--hostBuilder:reloadConfigOnChange=false"]
