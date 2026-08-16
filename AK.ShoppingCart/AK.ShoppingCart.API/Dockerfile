FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY ["AK.ShoppingCart/AK.ShoppingCart.API/AK.ShoppingCart.API.csproj", "AK.ShoppingCart/AK.ShoppingCart.API/"]
COPY ["AK.ShoppingCart/AK.ShoppingCart.Application/AK.ShoppingCart.Application.csproj", "AK.ShoppingCart/AK.ShoppingCart.Application/"]
COPY ["AK.ShoppingCart/AK.ShoppingCart.Domain/AK.ShoppingCart.Domain.csproj", "AK.ShoppingCart/AK.ShoppingCart.Domain/"]
COPY ["AK.ShoppingCart/AK.ShoppingCart.Infrastructure/AK.ShoppingCart.Infrastructure.csproj", "AK.ShoppingCart/AK.ShoppingCart.Infrastructure/"]
COPY ["AK.BuildingBlocks/AK.BuildingBlocks/AK.BuildingBlocks.csproj", "AK.BuildingBlocks/AK.BuildingBlocks/"]
COPY ["nuget.config", "."]
RUN dotnet restore "AK.ShoppingCart/AK.ShoppingCart.API/AK.ShoppingCart.API.csproj" --configfile nuget.config
COPY . .
WORKDIR "/src/AK.ShoppingCart/AK.ShoppingCart.API"
RUN dotnet build "AK.ShoppingCart.API.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "AK.ShoppingCart.API.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
USER $APP_UID
ENTRYPOINT ["dotnet", "AK.ShoppingCart.API.dll"]
