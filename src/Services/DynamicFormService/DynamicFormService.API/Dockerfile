FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["src/Services/DynamicFormService/DynamicFormService.API/DynamicFormService.API.csproj", "Services/DynamicFormService/DynamicFormService.API/"]
COPY ["src/Services/DynamicFormService/DynamicFormService.Application/DynamicFormService.Application.csproj", "Services/DynamicFormService/DynamicFormService.Application/"]
COPY ["src/Services/DynamicFormService/DynamicFormService.Domain/DynamicFormService.Domain.csproj", "Services/DynamicFormService/DynamicFormService.Domain/"]
COPY ["src/Services/DynamicFormService/DynamicFormService.Infrastructure/DynamicFormService.Infrastructure.csproj", "Services/DynamicFormService/DynamicFormService.Infrastructure/"]
COPY ["src/BuildingBlocks/Common/Common.csproj", "BuildingBlocks/Common/"]
COPY ["src/BuildingBlocks/Contracts/Contracts.csproj", "BuildingBlocks/Contracts/"]
COPY ["src/BuildingBlocks/SharedKernel/SharedKernel.csproj", "BuildingBlocks/SharedKernel/"]
RUN dotnet restore "Services/DynamicFormService/DynamicFormService.API/DynamicFormService.API.csproj"

COPY src/ .
WORKDIR /src/Services/DynamicFormService/DynamicFormService.API
RUN dotnet publish "DynamicFormService.API.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Hdos.DynamicFormService.API.dll"]
