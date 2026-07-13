FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY ["src/ArcadeLink.Catalog.Api/ArcadeLink.Catalog.Api.csproj", "src/ArcadeLink.Catalog.Api/"]
RUN dotnet restore "src/ArcadeLink.Catalog.Api/ArcadeLink.Catalog.Api.csproj"

COPY . .
RUN dotnet publish "src/ArcadeLink.Catalog.Api/ArcadeLink.Catalog.Api.csproj" -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "ArcadeLink.Catalog.Api.dll"]
