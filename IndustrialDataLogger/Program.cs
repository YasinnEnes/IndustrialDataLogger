using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using IndustrialDataLogger;
using IndustrialDataLogger.Data;
using IndustrialDataLogger.HealthChecks;
using IndustrialDataLogger.Hubs;
using IndustrialDataLogger.Models.Entities;
using IndustrialDataLogger.Services;

var builder = WebApplication.CreateBuilder(args);

// 1. Temel Servisler & API
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

// GÜN 4 (Sprint 4.1): Swagger JWT Bearer Desteği
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "Industrial IoT & Digital Twin API", Version = "v1" });
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "JWT Authorization header using the Bearer scheme. Example: \"Authorization: Bearer {token}\"",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.ApiKey,
        Scheme = "Bearer"
    });
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

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

// GÜN 4 (Sprint 4.1): Security Hardening - Environment Variable & User Secrets & JWT Konfigürasyonu
var connectionString = Environment.GetEnvironmentVariable("POSTGRES_CONNECTION_STRING")
    ?? builder.Configuration.GetConnectionString("PostgreSql");

if (string.IsNullOrWhiteSpace(connectionString) || connectionString.Contains("YOUR_POSTGRES_PASSWORD"))
{
    var pgHost = Environment.GetEnvironmentVariable("POSTGRES_HOST") ?? "localhost";
    var pgPort = Environment.GetEnvironmentVariable("POSTGRES_PORT") ?? "5432";
    var pgDb = Environment.GetEnvironmentVariable("POSTGRES_DB") ?? "IndustrialDataDB";
    var pgUser = Environment.GetEnvironmentVariable("POSTGRES_USER") ?? "postgres";
    var pgPass = Environment.GetEnvironmentVariable("POSTGRES_PASSWORD") ?? "postgres";
    connectionString = $"Host={pgHost};Port={pgPort};Database={pgDb};Username={pgUser};Password={pgPass}";
}

bool isPgAvailable = false;
try
{
    using var testConn = new Npgsql.NpgsqlConnection(connectionString);
    testConn.Open();
    isPgAvailable = true;
    testConn.Close();
}
catch (Exception pgEx)
{
    isPgAvailable = false;
    Console.WriteLine($"[PostgreSQL Bağlantı Uyarısı] Yerel PostgreSQL bağlantısı sağlanamadı ({pgEx.Message}).");
    Console.WriteLine("[BİLGİ] Otomatik In-Memory Veritabanı moduna geçildi. Web paneli, SignalR ve simülasyon motoru tam işlevsel çalışıyor.");
}

builder.Services.AddDbContext<IndustrialDbContext>(options =>
{
    if (isPgAvailable)
    {
        options.UseNpgsql(connectionString);
    }
    else
    {
        options.UseInMemoryDatabase("IndustrialDataDB_InMemory");
    }
});

var jwtSecret = Environment.GetEnvironmentVariable("JWT_SECRET")
    ?? builder.Configuration["JwtSettings:Secret"]
    ?? builder.Configuration["JwtSettings:SecretKey"];

if (string.IsNullOrWhiteSpace(jwtSecret) || jwtSecret.Contains("YOUR_SECURE_JWT_SECRET_KEY") || jwtSecret.Length < 32)
{
    jwtSecret = "IndustrialDataLogger_Development_LocalSecretKey_2026_Min32Chars!";
}

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.RequireHttpsMetadata = false;
    options.SaveToken = true;
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
        ValidateIssuer = true,
        ValidIssuer = builder.Configuration["JwtSettings:Issuer"] ?? "IndustrialDataLoggerAPI",
        ValidateAudience = true,
        ValidAudience = builder.Configuration["JwtSettings:Audience"] ?? "IndustrialDataLoggerDashboard",
        ClockSkew = TimeSpan.Zero
    };
});

builder.Services.AddAuthorization();

// Observability & Structured Logging
builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(options =>
{
    options.IncludeScopes = true;
    options.SingleLine = true;
    options.TimestampFormat = "[yyyy-MM-dd HH:mm:ss.fff] ";
});

// GÜN 4 (Sprint 4.2): Observability & Health Checks
builder.Services.AddHealthChecks()
    .AddCheck<PlcHealthCheck>("plc_health_check");

// 3. Servis Kayıtları
builder.Services.AddSingleton<MockPlcService>();
builder.Services.AddSingleton<PlcService>();
builder.Services.AddSingleton<IPlcConnectionManager, PlcConnectionManager>();
builder.Services.AddSingleton<IPlcService, HybridPlcService>();
builder.Services.AddSingleton<IJwtTokenService, JwtTokenService>();
builder.Services.AddSingleton<IEventLogService, EventLogService>();
builder.Services.AddSingleton<IAnomalyDetectionEngine, AnomalyDetectionEngine>();
builder.Services.AddSingleton<IAlarmService, AlarmService>();
builder.Services.AddSingleton<IDigitalTwinService, DigitalTwinService>();
builder.Services.AddSingleton<ITagConfigService, TagConfigService>();

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

        if (isPgAvailable)
        {
            // Var olan tablolar varsa eksik sütunları ve indeksleri güvenle ekle
            db.Database.ExecuteSqlRaw(@"
                CREATE TABLE IF NOT EXISTS machines (
                    id SERIAL PRIMARY KEY,
                    machinecode VARCHAR(50) UNIQUE NOT NULL,
                    name VARCHAR(100) NOT NULL,
                    type VARCHAR(50) NOT NULL,
                    plcip VARCHAR(50) NOT NULL,
                    isactive BOOLEAN DEFAULT TRUE,
                    description VARCHAR(255),
                    createdat TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
                    updatedat TIMESTAMPTZ
                );
                CREATE UNIQUE INDEX IF NOT EXISTS ""IX_machines_machinecode"" ON machines (machinecode);

                INSERT INTO machines (id, machinecode, name, type, plcip, isactive, description, createdat)
                VALUES (1, 'PLC-S7-1200-UNIT-01', 'Siemens S7-1200 Akıllı Üretim Ünitesi', 'InjectionMolding', '192.168.0.1', TRUE, 'Ana PLC Üretim Hattı İstasyonu', CURRENT_TIMESTAMP)
                ON CONFLICT (id) DO NOTHING;

                CREATE TABLE IF NOT EXISTS sensordata (
                    id BIGSERIAL PRIMARY KEY,
                    machineid INT NOT NULL DEFAULT 1,
                    timestamp TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
                    temperature DOUBLE PRECISION NOT NULL,
                    pressure DOUBLE PRECISION NOT NULL,
                    machinestatus BOOLEAN NOT NULL,
                    errorcode SMALLINT DEFAULT 0
                );
                ALTER TABLE sensordata ADD COLUMN IF NOT EXISTS machineid INT NOT NULL DEFAULT 1;
                ALTER TABLE sensordata ADD COLUMN IF NOT EXISTS errorcode SMALLINT DEFAULT 0;
                CREATE INDEX IF NOT EXISTS ""IX_sensordata_timestamp"" ON sensordata (timestamp DESC);
                CREATE INDEX IF NOT EXISTS ""IX_sensordata_machineid"" ON sensordata (machineid);

                CREATE TABLE IF NOT EXISTS alarmlogs (
                    id BIGSERIAL PRIMARY KEY,
                    machineid INT NOT NULL DEFAULT 1,
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
                ALTER TABLE alarmlogs ADD COLUMN IF NOT EXISTS machineid INT NOT NULL DEFAULT 1;
                CREATE INDEX IF NOT EXISTS ""IX_alarmlogs_createdat"" ON alarmlogs (createdat DESC);
                CREATE INDEX IF NOT EXISTS ""IX_alarmlogs_status"" ON alarmlogs (status);
                CREATE INDEX IF NOT EXISTS ""IX_alarmlogs_machineid"" ON alarmlogs (machineid);

                CREATE TABLE IF NOT EXISTS systemeventlogs (
                    id BIGSERIAL PRIMARY KEY,
                    machineid INT,
                    eventtype VARCHAR(100) NOT NULL,
                    severity INT NOT NULL,
                    description VARCHAR(500) NOT NULL,
                    source VARCHAR(100) DEFAULT 'System',
                    timestamp TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP
                );
                ALTER TABLE systemeventlogs ADD COLUMN IF NOT EXISTS machineid INT;
                CREATE INDEX IF NOT EXISTS ""IX_systemeventlogs_timestamp"" ON systemeventlogs (timestamp DESC);
                CREATE INDEX IF NOT EXISTS ""IX_systemeventlogs_machineid"" ON systemeventlogs (machineid);

                CREATE TABLE IF NOT EXISTS plctagconfigs (
                    id BIGSERIAL PRIMARY KEY,
                    machineid INT NOT NULL DEFAULT 1,
                    tagname VARCHAR(100) NOT NULL,
                    dbnumber INT NOT NULL DEFAULT 1,
                    byteoffset INT NOT NULL DEFAULT 0,
                    bitoffset INT NOT NULL DEFAULT 0,
                    datatype VARCHAR(20) NOT NULL DEFAULT 'REAL',
                    unit VARCHAR(50),
                    description VARCHAR(255),
                    iswritable BOOLEAN DEFAULT TRUE,
                    ismonitored BOOLEAN DEFAULT TRUE,
                    createdat TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
                    updatedat TIMESTAMPTZ
                );
                ALTER TABLE plctagconfigs ADD COLUMN IF NOT EXISTS machineid INT NOT NULL DEFAULT 1;
                CREATE INDEX IF NOT EXISTS ""IX_plctagconfigs_machineid"" ON plctagconfigs (machineid);

                CREATE TABLE IF NOT EXISTS alarmrules (
                    id SERIAL PRIMARY KEY,
                    machineid INT,
                    rulename VARCHAR(100) NOT NULL,
                    metric VARCHAR(50) NOT NULL DEFAULT 'Temperature',
                    operator INT NOT NULL DEFAULT 0,
                    threshold DOUBLE PRECISION NOT NULL,
                    severity INT NOT NULL DEFAULT 2,
                    alarmtype VARCHAR(100) NOT NULL,
                    messagetemplate VARCHAR(255) NOT NULL,
                    enabled BOOLEAN NOT NULL DEFAULT TRUE,
                    createdat TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
                    updatedat TIMESTAMPTZ
                );
                ALTER TABLE alarmrules ADD COLUMN IF NOT EXISTS machineid INT;
                CREATE INDEX IF NOT EXISTS ""IX_alarmrules_machineid"" ON alarmrules (machineid);
                CREATE INDEX IF NOT EXISTS ""IX_alarmrules_metric"" ON alarmrules (metric);
                CREATE INDEX IF NOT EXISTS ""IX_alarmrules_enabled"" ON alarmrules (enabled);
            ");

            Console.WriteLine("[PostgreSQL] Veritabanı tabloları ve varsayılan kayıtlar hazırlandı.");
        }
        else
        {
            if (!db.Machines.Any())
            {
                db.Machines.Add(new Machine
                {
                    Id = 1,
                    MachineCode = "PLC-S7-1200-UNIT-01",
                    Name = "Siemens S7-1200 Akıllı Üretim Ünitesi",
                    Type = "InjectionMolding",
                    PlcIp = "192.168.0.1",
                    IsActive = true,
                    Description = "Ana PLC Üretim Hattı İstasyonu",
                    CreatedAt = DateTime.UtcNow
                });
                db.SaveChanges();
            }
            Console.WriteLine("[In-Memory DB] Varsayılan makineler, tagler ve alarm kuralları belleğe yüklendi.");
        }

        var tagConfigService = scope.ServiceProvider.GetRequiredService<ITagConfigService>();
        tagConfigService.EnsureDefaultTagsSeededAsync().GetAwaiter().GetResult();

        var alarmService = scope.ServiceProvider.GetRequiredService<IAlarmService>();
        alarmService.EnsureDefaultRulesSeededAsync().GetAwaiter().GetResult();
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[Uyarı] Veritabanı başlatılırken hata: {ex.Message}");
    }
}

app.UseCors();

// 4. Swagger Middleware
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "Industrial IoT API v1");
});

// 5. Statik Dosya ve Kök Dizin Yapılandırması (Dashboard Çözümleyici)
var dashboardCandidates = new[]
{
    Path.Combine(builder.Environment.ContentRootPath, "Dashboard"),
    Path.Combine(Directory.GetCurrentDirectory(), "Dashboard"),
    Path.Combine(Directory.GetCurrentDirectory(), "IndustrialDataLogger", "Dashboard"),
    Path.Combine(AppContext.BaseDirectory, "Dashboard")
};

string? resolvedDashboardPath = dashboardCandidates.FirstOrDefault(Directory.Exists);

if (!string.IsNullOrEmpty(resolvedDashboardPath))
{
    app.UseDefaultFiles(new DefaultFilesOptions
    {
        FileProvider = new PhysicalFileProvider(resolvedDashboardPath),
        DefaultFileNames = new List<string> { "login.html", "index.html" }
    });

    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = new PhysicalFileProvider(resolvedDashboardPath),
        RequestPath = ""
    });
}
else
{
    app.UseDefaultFiles();
}

app.UseStaticFiles();

// GÜN 4 (Sprint 4.1): Authentication & Authorization Middleware
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHealthChecks("/healthz");

// GÜN 3 - SignalR Hub Endpoint Eşlemesi
app.MapHub<MonitoringHub>("/sensorHub");

var port = Environment.GetEnvironmentVariable("PORT") ?? "5000";

// Geliştirme ortamında sunucu ayağa kalktığında tarayıcıyı otomatik aç
if (app.Environment.IsDevelopment())
{
    app.Lifetime.ApplicationStarted.Register(() =>
    {
        try
        {
            var browserUrl = $"http://localhost:{port}/index.html";
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = browserUrl,
                UseShellExecute = true
            });
        }
        catch { }
    });
}

app.Run($"http://0.0.0.0:{port}");