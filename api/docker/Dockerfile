# Multi-stage build: sdk for build/publish, aspnet for runtime.
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY CleaningSuite.Backend.sln ./
COPY CleaningSuite.Api/CleaningSuite.Api.csproj CleaningSuite.Api/
COPY CleaningSuite.Application/CleaningSuite.Application.csproj CleaningSuite.Application/
COPY CleaningSuite.Domain/CleaningSuite.Domain.csproj CleaningSuite.Domain/
COPY CleaningSuite.Infrastructure/CleaningSuite.Infrastructure.csproj CleaningSuite.Infrastructure/
COPY CleaningSuite.Tests/CleaningSuite.Tests.csproj CleaningSuite.Tests/
RUN dotnet restore CleaningSuite.Api/CleaningSuite.Api.csproj

COPY . .
RUN dotnet publish CleaningSuite.Api/CleaningSuite.Api.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "CleaningSuite.Api.dll"]
