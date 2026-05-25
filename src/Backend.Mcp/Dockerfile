FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY Template.sln ./
COPY Directory.Build.props ./
COPY .editorconfig ./
COPY global.json ./
COPY src/Backend.Domain/Backend.Domain.csproj src/Backend.Domain/
COPY src/Backend.Application/Backend.Application.csproj src/Backend.Application/
COPY src/Backend.Infrastructure/Backend.Infrastructure.csproj src/Backend.Infrastructure/
COPY src/Backend.Mcp/Backend.Mcp.csproj src/Backend.Mcp/

RUN dotnet restore src/Backend.Mcp/Backend.Mcp.csproj

COPY src/ src/
RUN dotnet build src/Backend.Mcp/Backend.Mcp.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
EXPOSE 8080

ENV ASPNETCORE_URLS=http://+:8080

COPY --from=build /app/publish ./

USER app
ENTRYPOINT ["dotnet", "Backend.Mcp.dll"]
