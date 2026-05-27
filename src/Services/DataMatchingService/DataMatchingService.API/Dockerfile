FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["src/Services/DataMatchingService/DataMatchingService.API/DataMatchingService.API.csproj", "Services/DataMatchingService/DataMatchingService.API/"]
COPY ["src/Services/DataMatchingService/DataMatchingService.Application/DataMatchingService.Application.csproj", "Services/DataMatchingService/DataMatchingService.Application/"]
COPY ["src/Services/DataMatchingService/DataMatchingService.Domain/DataMatchingService.Domain.csproj", "Services/DataMatchingService/DataMatchingService.Domain/"]
COPY ["src/Services/DataMatchingService/DataMatchingService.Infrastructure/DataMatchingService.Infrastructure.csproj", "Services/DataMatchingService/DataMatchingService.Infrastructure/"]
COPY ["src/BuildingBlocks/Common/Common.csproj", "BuildingBlocks/Common/"]
COPY ["src/BuildingBlocks/Contracts/Contracts.csproj", "BuildingBlocks/Contracts/"]
COPY ["src/BuildingBlocks/SharedKernel/SharedKernel.csproj", "BuildingBlocks/SharedKernel/"]
RUN dotnet restore "Services/DataMatchingService/DataMatchingService.API/DataMatchingService.API.csproj"

COPY src/ .
WORKDIR /src/Services/DataMatchingService/DataMatchingService.API
RUN dotnet publish "DataMatchingService.API.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Hdos.DataMatchingService.API.dll"]
