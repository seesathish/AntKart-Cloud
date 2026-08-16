FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY ["AK.Discount/AK.Discount.Grpc/AK.Discount.Grpc.csproj", "AK.Discount/AK.Discount.Grpc/"]
COPY ["AK.Discount/AK.Discount.Application/AK.Discount.Application.csproj", "AK.Discount/AK.Discount.Application/"]
COPY ["AK.Discount/AK.Discount.Domain/AK.Discount.Domain.csproj", "AK.Discount/AK.Discount.Domain/"]
COPY ["AK.Discount/AK.Discount.Infrastructure/AK.Discount.Infrastructure.csproj", "AK.Discount/AK.Discount.Infrastructure/"]
COPY ["AK.BuildingBlocks/AK.BuildingBlocks/AK.BuildingBlocks.csproj", "AK.BuildingBlocks/AK.BuildingBlocks/"]
COPY ["nuget.config", "."]
RUN dotnet restore "AK.Discount/AK.Discount.Grpc/AK.Discount.Grpc.csproj" --configfile nuget.config
COPY . .
WORKDIR "/src/AK.Discount/AK.Discount.Grpc"
RUN dotnet build "AK.Discount.Grpc.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "AK.Discount.Grpc.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
RUN mkdir -p /app/data && chown -R $APP_UID:$APP_UID /app/data
USER $APP_UID
ENTRYPOINT ["dotnet", "AK.Discount.Grpc.dll"]
