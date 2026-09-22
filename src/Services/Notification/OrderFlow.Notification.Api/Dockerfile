# syntax=docker/dockerfile:1
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Directory.Build.props .editorconfig ./
COPY src/BuildingBlocks/OrderFlow.Contracts/OrderFlow.Contracts.csproj                     src/BuildingBlocks/OrderFlow.Contracts/
COPY src/BuildingBlocks/OrderFlow.Messaging/OrderFlow.Messaging.csproj                     src/BuildingBlocks/OrderFlow.Messaging/
COPY src/BuildingBlocks/OrderFlow.ServiceDefaults/OrderFlow.ServiceDefaults.csproj         src/BuildingBlocks/OrderFlow.ServiceDefaults/
COPY src/Services/Notification/OrderFlow.Notification.Api/OrderFlow.Notification.Api.csproj src/Services/Notification/OrderFlow.Notification.Api/
RUN dotnet restore src/Services/Notification/OrderFlow.Notification.Api/OrderFlow.Notification.Api.csproj

COPY src/BuildingBlocks/ src/BuildingBlocks/
COPY src/Services/Notification/ src/Services/Notification/
RUN dotnet publish src/Services/Notification/OrderFlow.Notification.Api/OrderFlow.Notification.Api.csproj \
    -c Release --no-restore -o /app/publish /p:UseAppHost=false

# -extra adds ICU + tzdata that Microsoft.Data.SqlClient requires (no invariant mode).
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled-extra AS final
WORKDIR /app
COPY --from=build /app/publish .
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "OrderFlow.Notification.Api.dll"]
