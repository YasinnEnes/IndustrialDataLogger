# 1. Build Aşaması (SDK)
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Proje dosyalarını kopyala ve restore et (Önbellekleme optimizasyonu)
COPY ["IndustrialDataLogger/IndustrialDataLogger.csproj", "IndustrialDataLogger/"]
COPY ["IndustrialMonitoring.API/IndustrialMonitoring.API.csproj", "IndustrialMonitoring.API/"]
RUN dotnet restore "IndustrialDataLogger/IndustrialDataLogger.csproj"

# Tüm kaynak kodları kopyala ve derle
COPY . .
WORKDIR "/src/IndustrialDataLogger"
RUN dotnet publish "IndustrialDataLogger.csproj" -c Release -o /app/publish /p:UseAppHost=false

# 2. Çalışma Zamanı Aşaması (Lightweight ASP.NET Runtime)
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

# Healthcheck için curl paketini yükle
RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*

# Derlenen çıktıları ve Dashboard statik dosyalarını kopyala
COPY --from=build /app/publish .

# Ortam değişkenleri
ENV ASPNETCORE_URLS=http://+:5000
ENV ASPNETCORE_ENVIRONMENT=Production
ENV PORT=5000

EXPOSE 5000

# Sağlık kontrolü
HEALTHCHECK --interval=30s --timeout=5s --start-period=10s --retries=3 \
  CMD curl -f http://localhost:5000/health || exit 1

ENTRYPOINT ["dotnet", "IndustrialDataLogger.dll"]
