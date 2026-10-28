# Two images from one build:
#   docker build --target api .        → ASP.NET API (port 8080)
#   docker build --target collector .  → run-once collector ("collect" / "migrate")
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first, from project files only, so the layer is cached until dependencies change.
COPY Directory.Build.props ./
COPY src/Ashopper.Domain/Ashopper.Domain.csproj src/Ashopper.Domain/
COPY src/Ashopper.Infrastructure/Ashopper.Infrastructure.csproj src/Ashopper.Infrastructure/
COPY src/Ashopper.Api/Ashopper.Api.csproj src/Ashopper.Api/
COPY src/Ashopper.Collector/Ashopper.Collector.csproj src/Ashopper.Collector/
RUN dotnet restore src/Ashopper.Api && dotnet restore src/Ashopper.Collector

COPY src/ src/
RUN dotnet publish src/Ashopper.Api -c Release -o /out/api --no-restore -p:OpenApiGenerateDocuments=false \
 && dotnet publish src/Ashopper.Collector -c Release -o /out/collector --no-restore

# "chiseled-extra": distroless Ubuntu with ICU/tzdata (SqlClient needs globalization), runs as non-root.
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled-extra AS api
WORKDIR /app
COPY --from=build /out/api .
EXPOSE 8080
ENTRYPOINT ["dotnet", "Ashopper.Api.dll"]

FROM mcr.microsoft.com/dotnet/runtime:10.0-noble-chiseled-extra AS collector
WORKDIR /app
COPY --from=build /out/collector .
ENTRYPOINT ["dotnet", "Ashopper.Collector.dll"]
CMD ["collect"]
