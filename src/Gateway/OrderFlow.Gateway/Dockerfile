# syntax=docker/dockerfile:1
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Directory.Build.props .editorconfig ./
COPY src/BuildingBlocks/OrderFlow.ServiceDefaults/OrderFlow.ServiceDefaults.csproj src/BuildingBlocks/OrderFlow.ServiceDefaults/
COPY src/Gateway/OrderFlow.Gateway/OrderFlow.Gateway.csproj                       src/Gateway/OrderFlow.Gateway/
RUN dotnet restore src/Gateway/OrderFlow.Gateway/OrderFlow.Gateway.csproj

COPY src/BuildingBlocks/OrderFlow.ServiceDefaults/ src/BuildingBlocks/OrderFlow.ServiceDefaults/
COPY src/Gateway/ src/Gateway/
RUN dotnet publish src/Gateway/OrderFlow.Gateway/OrderFlow.Gateway.csproj \
    -c Release --no-restore -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled AS final
WORKDIR /app
COPY --from=build /app/publish .
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "OrderFlow.Gateway.dll"]
