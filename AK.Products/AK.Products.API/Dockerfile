FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY ["AK.Products/AK.Products.API/AK.Products.API.csproj", "AK.Products/AK.Products.API/"]
COPY ["AK.Products/AK.Products.Application/AK.Products.Application.csproj", "AK.Products/AK.Products.Application/"]
COPY ["AK.Products/AK.Products.Domain/AK.Products.Domain.csproj", "AK.Products/AK.Products.Domain/"]
COPY ["AK.Products/AK.Products.Infrastructure/AK.Products.Infrastructure.csproj", "AK.Products/AK.Products.Infrastructure/"]
COPY ["AK.BuildingBlocks/AK.BuildingBlocks/AK.BuildingBlocks.csproj", "AK.BuildingBlocks/AK.BuildingBlocks/"]
COPY ["nuget.config", "."]
RUN dotnet restore "AK.Products/AK.Products.API/AK.Products.API.csproj" --configfile nuget.config
COPY . .
WORKDIR "/src/AK.Products/AK.Products.API"
RUN dotnet build "AK.Products.API.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "AK.Products.API.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
USER $APP_UID
ENTRYPOINT ["dotnet", "AK.Products.API.dll"]
