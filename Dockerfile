FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /build
RUN git clone --depth=1 https://github.com/Coflnet/HypixelSkyblock.git dev && echo "cachebreak"
WORKDIR /build/sky
COPY SkyPlayerName.csproj SkyPlayerName.csproj
RUN dotnet restore
COPY . .
RUN dotnet publish -c release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine
WORKDIR /app

COPY --from=build /app .

ENV ASPNETCORE_URLS=http://+:8000

USER app

ENTRYPOINT ["dotnet", "SkyPlayerName.dll", "--hostBuilder:reloadConfigOnChange=false"]
