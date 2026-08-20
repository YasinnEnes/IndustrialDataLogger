using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using IndustrialDataLogger;
using IndustrialDataLogger.Data;
using IndustrialDataLogger.Hubs;
using IndustrialDataLogger.Services;

var builder = WebApplication.CreateBuilder(args);

// 1. Temel Servisler
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// GÜN 3 - SignalR Servis Kaydı
builder.Services.AddSignalR();

// CORS Yapılandırması (SignalR WebSockets ve HTTP için)
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.SetIsOriginAllowed(_ => true)
              .AllowAnyMethod()
              .AllowAnyHeader()
              .AllowCredentials();
    });
});

// 2. PostgreSQL EF Core DbContext Yapılandırması (GÜN 2)
var connectionString = builder.Configuration.GetConnectionString("PostgreSql")
    ?? "Host=localhost;Port=5432;Database=IndustrialDataDB;Username=postgres;Password=1234";

builder.Services.AddDbContext<IndustrialDbContext>(options =>
    options.UseNpgsql(connectionString));

// 3. Servis Kayıtları
builder.Services.AddSingleton<MockPlcService>();
builder.Services.AddSingleton<PlcService>();
builder.Services.AddSingleton<IPlcConnectionManager, PlcConnectionManager>();

// IPlcService sözleşmesi için HybridPlcService eşlemesi (simülasyon ve gerçek PLC yönetimi)
builder.Services.AddSingleton<IPlcService, HybridPlcService>();

// GÜN 3 - Sistem Olay Günlüğü Servis Kaydı
builder.Services.AddSingleton<IEventLogService, EventLogService>();

// GÜN 4 - Alarm Motoru Servis Kaydı
builder.Services.AddSingleton<IAlarmService, AlarmService>();

// GÜN 2 & GÜN 5 - Digital Twin & Health Scoring Servis Kaydı
builder.Services.AddSingleton<IDigitalTwinService, DigitalTwinService>();

// 4. Arka Plan İşçisi (Worker Persistence & SignalR Push & Alarms - GÜN 2, 3, 4, 5)
builder.Services.AddHostedService<Worker>();

var app = builder.Build();

// Veritabanı ve Tablo Kontrolü / Otomatik Oluşturma (EnsureCreated & Migration)
using (var scope = app.Services.CreateScope())
{
    try
    {
        var db = scope.ServiceProvider.GetRequiredService<IndustrialDbContext>();
        db.Database.EnsureCreated();

        // Var olan tablolar varsa eksik sütunları ve indeksleri güvenle ekle
        db.Database.ExecuteSqlRaw(@"
            CREATE TABLE IF NOT EXISTS sensordata (
                id BIGSERIAL PRIMARY KEY,
                timestamp TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
                temperature DOUBLE PRECISION NOT NULL,
                pressure DOUBLE PRECISION NOT NULL,
                machinestatus BOOLEAN NOT NULL,
                errorcode SMALLINT DEFAULT 0
            );
            ALTER TABLE sensordata ADD COLUMN IF NOT EXISTS errorcode SMALLINT DEFAULT 0;
            CREATE INDEX IF NOT EXISTS ""IX_sensordata_timestamp"" ON sensordata (timestamp DESC);

            CREATE TABLE IF NOT EXISTS alarmlogs (
                id BIGSERIAL PRIMARY KEY,
                alarmtype VARCHAR(100) NOT NULL,
                severity INT NOT NULL,
                status INT NOT NULL,
                message VARCHAR(255) NOT NULL,
                triggeredvalue DOUBLE PRECISION,
                thresholdvalue DOUBLE PRECISION,
                createdat TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
                resolvedat TIMESTAMPTZ,
                acknowledgedat TIMESTAMPTZ
            );
            CREATE INDEX IF NOT EXISTS ""IX_alarmlogs_createdat"" ON alarmlogs (createdat DESC);
            CREATE INDEX IF NOT EXISTS ""IX_alarmlogs_status"" ON alarmlogs (status);

            CREATE TABLE IF NOT EXISTS systemeventlogs (
                id BIGSERIAL PRIMARY KEY,
                eventtype VARCHAR(100) NOT NULL,
                severity INT NOT NULL,
                description VARCHAR(500) NOT NULL,
                source VARCHAR(100) DEFAULT 'System',
                timestamp TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP
            );
            CREATE INDEX IF NOT EXISTS ""IX_systemeventlogs_timestamp"" ON systemeventlogs (timestamp DESC);
        ");
    }
    catch (Exception ex)
    {
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
        logger.LogWarning("PostgreSQL tablosu kontrol/güncelleme uyarısı: {Message}", ex.Message);
    }
}

app.UseCors();

// 4. Swagger Middleware
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "Industrial IoT API v1");
});

app.UseHttpsRedirection();

// 5. Statik Dosya ve Kök Dizin Yapılandırması
app.UseDefaultFiles(new DefaultFilesOptions
{
    DefaultFileNames = new List<string> { "login.html" }
});

var dashboardPath = Path.Combine(Directory.GetCurrentDirectory(), "Dashboard");
if (Directory.Exists(dashboardPath))
{
    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = new PhysicalFileProvider(dashboardPath),
        RequestPath = ""
    });
}

app.UseStaticFiles();

app.UseAuthorization();
app.MapControllers();

// GÜN 3 - SignalR Hub Endpoint Eşlemesi
app.MapHub<MonitoringHub>("/sensorHub");

app.Run("http://localhost:5000");