FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["src/Services/M01Service/M01Service.API/M01Service.API.csproj", "Services/M01Service/M01Service.API/"]
COPY ["src/Services/M01Service/M01Service.Application/M01Service.Application.csproj", "Services/M01Service/M01Service.Application/"]
COPY ["src/Services/M01Service/M01Service.Domain/M01Service.Domain.csproj", "Services/M01Service/M01Service.Domain/"]
COPY ["src/Services/M01Service/M01Service.Infrastructure/M01Service.Infrastructure.csproj", "Services/M01Service/M01Service.Infrastructure/"]
COPY ["src/BuildingBlocks/Common/Common.csproj", "BuildingBlocks/Common/"]
COPY ["src/BuildingBlocks/Contracts/Contracts.csproj", "BuildingBlocks/Contracts/"]
COPY ["src/BuildingBlocks/SharedKernel/SharedKernel.csproj", "BuildingBlocks/SharedKernel/"]
RUN dotnet restore "Services/M01Service/M01Service.API/M01Service.API.csproj"

COPY src/ .
WORKDIR /src/Services/M01Service/M01Service.API
RUN dotnet publish "M01Service.API.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Hdos.M01Service.API.dll"]
