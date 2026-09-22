# syntax=docker/dockerfile:1
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Directory.Build.props .editorconfig ./
COPY src/BuildingBlocks/OrderFlow.Contracts/OrderFlow.Contracts.csproj               src/BuildingBlocks/OrderFlow.Contracts/
COPY src/BuildingBlocks/OrderFlow.Messaging/OrderFlow.Messaging.csproj               src/BuildingBlocks/OrderFlow.Messaging/
COPY src/BuildingBlocks/OrderFlow.Grpc.Contracts/OrderFlow.Grpc.Contracts.csproj     src/BuildingBlocks/OrderFlow.Grpc.Contracts/
COPY src/BuildingBlocks/OrderFlow.ServiceDefaults/OrderFlow.ServiceDefaults.csproj   src/BuildingBlocks/OrderFlow.ServiceDefaults/
COPY src/Services/Inventory/OrderFlow.Inventory.Api/OrderFlow.Inventory.Api.csproj   src/Services/Inventory/OrderFlow.Inventory.Api/
RUN dotnet restore src/Services/Inventory/OrderFlow.Inventory.Api/OrderFlow.Inventory.Api.csproj

COPY src/BuildingBlocks/ src/BuildingBlocks/
COPY src/Services/Inventory/ src/Services/Inventory/
RUN dotnet publish src/Services/Inventory/OrderFlow.Inventory.Api/OrderFlow.Inventory.Api.csproj \
    -c Release --no-restore -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled AS final
WORKDIR /app
COPY --from=build /app/publish .
USER $APP_UID
EXPOSE 8080 8081
ENTRYPOINT ["dotnet", "OrderFlow.Inventory.Api.dll"]
