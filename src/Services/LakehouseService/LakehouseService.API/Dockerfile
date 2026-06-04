FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["src/Services/LakehouseService/LakehouseService.API/LakehouseService.API.csproj", "Services/LakehouseService/LakehouseService.API/"]
COPY ["src/Services/LakehouseService/LakehouseService.Application/LakehouseService.Application.csproj", "Services/LakehouseService/LakehouseService.Application/"]
COPY ["src/Services/LakehouseService/LakehouseService.Domain/LakehouseService.Domain.csproj", "Services/LakehouseService/LakehouseService.Domain/"]
COPY ["src/Services/LakehouseService/LakehouseService.Infrastructure/LakehouseService.Infrastructure.csproj", "Services/LakehouseService/LakehouseService.Infrastructure/"]
COPY ["src/BuildingBlocks/Common/Common.csproj", "BuildingBlocks/Common/"]
COPY ["src/BuildingBlocks/Contracts/Contracts.csproj", "BuildingBlocks/Contracts/"]
COPY ["src/BuildingBlocks/SharedKernel/SharedKernel.csproj", "BuildingBlocks/SharedKernel/"]
RUN dotnet restore "Services/LakehouseService/LakehouseService.API/LakehouseService.API.csproj"

COPY src/ .
WORKDIR /src/Services/LakehouseService/LakehouseService.API
RUN dotnet publish "LakehouseService.API.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Hdos.LakehouseService.API.dll"]
