FROM mcr.microsoft.com/dotnet/sdk:6.0 as build
WORKDIR /build
RUN git clone --depth=1 -b net6 https://github.com/Coflnet/HypixelSkyblock.git dev
WORKDIR /build/sky
COPY SkyPlayerName.csproj SkyPlayerName.csproj
RUN dotnet restore
COPY . .
RUN dotnet publish -c release

FROM mcr.microsoft.com/dotnet/aspnet:6.0
WORKDIR /app

COPY --from=build /build/sky/bin/release/net6.0/publish/ .

ENV ASPNETCORE_URLS=http://+:8000

RUN useradd --uid $(shuf -i 2000-65000 -n 1) app
USER app

ENTRYPOINT ["dotnet", "SkyPlayerName.dll", "--hostBuilder:reloadConfigOnChange=false"]
