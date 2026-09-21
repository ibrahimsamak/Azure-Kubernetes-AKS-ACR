# syntax=docker/dockerfile:1

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Directory.Build.props .editorconfig ./
COPY src/BuildingBlocks/OrderFlow.Contracts/OrderFlow.Contracts.csproj               src/BuildingBlocks/OrderFlow.Contracts/
COPY src/BuildingBlocks/OrderFlow.Messaging/OrderFlow.Messaging.csproj               src/BuildingBlocks/OrderFlow.Messaging/
COPY src/BuildingBlocks/OrderFlow.Grpc.Contracts/OrderFlow.Grpc.Contracts.csproj     src/BuildingBlocks/OrderFlow.Grpc.Contracts/
COPY src/BuildingBlocks/OrderFlow.ServiceDefaults/OrderFlow.ServiceDefaults.csproj   src/BuildingBlocks/OrderFlow.ServiceDefaults/
COPY src/Services/Order/OrderFlow.Order.Domain/OrderFlow.Order.Domain.csproj                 src/Services/Order/OrderFlow.Order.Domain/
COPY src/Services/Order/OrderFlow.Order.Application/OrderFlow.Order.Application.csproj       src/Services/Order/OrderFlow.Order.Application/
COPY src/Services/Order/OrderFlow.Order.Infrastructure/OrderFlow.Order.Infrastructure.csproj src/Services/Order/OrderFlow.Order.Infrastructure/
COPY src/Services/Order/OrderFlow.Order.Api/OrderFlow.Order.Api.csproj                       src/Services/Order/OrderFlow.Order.Api/
RUN dotnet restore src/Services/Order/OrderFlow.Order.Api/OrderFlow.Order.Api.csproj

COPY src/BuildingBlocks/ src/BuildingBlocks/
COPY src/Services/Order/ src/Services/Order/
RUN dotnet publish src/Services/Order/OrderFlow.Order.Api/OrderFlow.Order.Api.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled-extra AS final
WORKDIR /app
COPY --from=build /app/publish .

USER $APP_UID
EXPOSE 8080

ENTRYPOINT ["dotnet", "OrderFlow.Order.Api.dll"]
