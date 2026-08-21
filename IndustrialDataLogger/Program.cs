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
using IndustrialDataLogger.Enums;
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
builder.Services.AddSingleton<IMaintenanceService, MaintenanceService>();
builder.Services.AddSingleton<IHierarchyService, HierarchyService>();
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
                CREATE TABLE IF NOT EXISTS factories (
                    id SERIAL PRIMARY KEY,
                    name VARCHAR(100) NOT NULL,
                    location VARCHAR(100),
                    description VARCHAR(255),
                    isactive BOOLEAN DEFAULT TRUE,
                    createdat TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP
                );

                CREATE TABLE IF NOT EXISTS productionlines (
                    id SERIAL PRIMARY KEY,
                    factoryid INT NOT NULL,
                    name VARCHAR(100) NOT NULL,
                    linecode VARCHAR(50) NOT NULL,
                    description VARCHAR(255),
                    isactive BOOLEAN DEFAULT TRUE,
                    createdat TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP
                );
                CREATE INDEX IF NOT EXISTS ""IX_productionlines_factoryid"" ON productionlines (factoryid);
                CREATE INDEX IF NOT EXISTS ""IX_productionlines_linecode"" ON productionlines (linecode);

                CREATE TABLE IF NOT EXISTS machines (
                    id SERIAL PRIMARY KEY,
                    productionlineid INT,
                    machinecode VARCHAR(50) UNIQUE NOT NULL,
                    name VARCHAR(100) NOT NULL,
                    type VARCHAR(50) NOT NULL,
                    plcip VARCHAR(50) NOT NULL,
                    isactive BOOLEAN DEFAULT TRUE,
                    description VARCHAR(255),
                    createdat TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
                    updatedat TIMESTAMPTZ
                );
                ALTER TABLE machines ADD COLUMN IF NOT EXISTS productionlineid INT;
                CREATE UNIQUE INDEX IF NOT EXISTS ""IX_machines_machinecode"" ON machines (machinecode);
                CREATE INDEX IF NOT EXISTS ""IX_machines_productionlineid"" ON machines (productionlineid);

                INSERT INTO factories (id, name, location, description, isactive, createdat)
                VALUES 
                    (1, 'Gebze Akıllı Üretim Kampüsü', 'Kocaeli / Gebze OSB', 'Ana Otomasyon ve CNC İşleme Merkezi', TRUE, CURRENT_TIMESTAMP),
                    (2, 'İzmir Ege Entegre Tesisi', 'İzmir / Çiğli AOSB', 'Robotik Montaj ve Lojistik Kampüsü', TRUE, CURRENT_TIMESTAMP)
                ON CONFLICT (id) DO NOTHING;

                INSERT INTO productionlines (id, factoryid, name, linecode, description, isactive, createdat)
                VALUES
                    (1, 1, 'Montaj & İşleme Hattı A', 'LINE-A', 'Enjeksiyon kalıplama ve CNC frezeleme', TRUE, CURRENT_TIMESTAMP),
                    (2, 1, 'Robotik Hücre Hattı B', 'LINE-B', 'Kuka 6-eksen robotik malzeme besleme', TRUE, CURRENT_TIMESTAMP),
                    (3, 2, 'Paketleme & Lojistik Hattı C', 'LINE-C', 'Akıllı konveyör ve paletleme istasyonu', TRUE, CURRENT_TIMESTAMP)
                ON CONFLICT (id) DO NOTHING;

                INSERT INTO machines (id, productionlineid, machinecode, name, type, plcip, isactive, description, createdat)
                VALUES 
                    (1, 1, 'PLC-S7-1200-UNIT-01', 'Siemens S7-1200 Akıllı Üretim Ünitesi', 'InjectionMolding', '192.168.0.1', TRUE, 'Ana PLC Üretim Hattı İstasyonu', CURRENT_TIMESTAMP),
                    (2, 2, 'KUKA-KR6-ROBOT-02', 'Kuka Robotik Hücre', 'RoboticCell', '192.168.0.2', TRUE, 'Robotik Besleme İstasyonu', CURRENT_TIMESTAMP),
                    (3, 3, 'PACK-CONVEYOR-03', 'Akıllı Konveyör & Paketleme', 'Packaging', '192.168.0.3', TRUE, 'Paletleme İstasyonu', CURRENT_TIMESTAMP)
                ON CONFLICT (id) DO UPDATE SET productionlineid = EXCLUDED.productionlineid;

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
                    timestamp TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
                    alarmtype VARCHAR(50) NOT NULL,
                    severity INT NOT NULL DEFAULT 1,
                    status INT NOT NULL DEFAULT 1,
                    message VARCHAR(255) NOT NULL,
                    triggeredvalue DOUBLE PRECISION,
                    thresholdvalue DOUBLE PRECISION,
                    acknowledgedby VARCHAR(100),
                    acknowledgedat TIMESTAMPTZ,
                    resolvedat TIMESTAMPTZ
                );
                ALTER TABLE alarmlogs ADD COLUMN IF NOT EXISTS machineid INT NOT NULL DEFAULT 1;
                ALTER TABLE alarmlogs ADD COLUMN IF NOT EXISTS triggeredvalue DOUBLE PRECISION;
                ALTER TABLE alarmlogs ADD COLUMN IF NOT EXISTS thresholdvalue DOUBLE PRECISION;
                ALTER TABLE alarmlogs ADD COLUMN IF NOT EXISTS acknowledgedby VARCHAR(100);
                ALTER TABLE alarmlogs ADD COLUMN IF NOT EXISTS acknowledgedat TIMESTAMPTZ;
                ALTER TABLE alarmlogs ADD COLUMN IF NOT EXISTS resolvedat TIMESTAMPTZ;
                CREATE INDEX IF NOT EXISTS ""IX_alarmlogs_timestamp"" ON alarmlogs (timestamp DESC);
                CREATE INDEX IF NOT EXISTS ""IX_alarmlogs_status"" ON alarmlogs (status);
                CREATE INDEX IF NOT EXISTS ""IX_alarmlogs_machineid"" ON alarmlogs (machineid);

                CREATE TABLE IF NOT EXISTS systemevents (
                    id BIGSERIAL PRIMARY KEY,
                    machineid INT,
                    timestamp TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
                    eventtype VARCHAR(50) NOT NULL,
                    description VARCHAR(500) NOT NULL,
                    severity INT NOT NULL DEFAULT 0,
                    source VARCHAR(100) NOT NULL DEFAULT 'System'
                );
                CREATE INDEX IF NOT EXISTS ""IX_systemevents_timestamp"" ON systemevents (timestamp DESC);
                CREATE INDEX IF NOT EXISTS ""IX_systemevents_eventtype"" ON systemevents (eventtype);
                CREATE INDEX IF NOT EXISTS ""IX_systemevents_machineid"" ON systemevents (machineid);

                CREATE TABLE IF NOT EXISTS plctagconfigs (
                    id SERIAL PRIMARY KEY,
                    machineid INT NOT NULL DEFAULT 1,
                    tagname VARCHAR(100) NOT NULL,
                    displayname VARCHAR(100) NOT NULL,
                    dbnumber INT NOT NULL,
                    startbyte INT NOT NULL,
                    bitnumber INT,
                    datatype INT NOT NULL,
                    unittype VARCHAR(20) NOT NULL,
                    isactive BOOLEAN NOT NULL DEFAULT TRUE,
                    iswritable BOOLEAN NOT NULL DEFAULT FALSE,
                    description VARCHAR(255),
                    createdat TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
                    updatedat TIMESTAMPTZ
                );
                CREATE INDEX IF NOT EXISTS ""IX_plctagconfigs_machineid"" ON plctagconfigs (machineid);
                CREATE INDEX IF NOT EXISTS ""IX_plctagconfigs_tagname"" ON plctagconfigs (tagname);

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

                CREATE TABLE IF NOT EXISTS maintenancetasks (
                    id SERIAL PRIMARY KEY,
                    machineid INT NOT NULL DEFAULT 1,
                    component VARCHAR(100) NOT NULL,
                    reason VARCHAR(500) NOT NULL,
                    priority INT NOT NULL DEFAULT 2,
                    status INT NOT NULL DEFAULT 1,
                    assignedto VARCHAR(100),
                    createdat TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
                    resolvedat TIMESTAMPTZ,
                    resolutionnotes VARCHAR(1000),
                    alarmlogid BIGINT
                );
                CREATE INDEX IF NOT EXISTS ""IX_maintenancetasks_machineid"" ON maintenancetasks (machineid);
                CREATE INDEX IF NOT EXISTS ""IX_maintenancetasks_status"" ON maintenancetasks (status);
                CREATE INDEX IF NOT EXISTS ""IX_maintenancetasks_priority"" ON maintenancetasks (priority);
            ");

            Console.WriteLine("[PostgreSQL] Veritabanı tabloları, hiyerarşi ve varsayılan kayıtlar hazırlandı.");
        }
        else
        {
            if (!db.Factories.Any())
            {
                db.Factories.AddRange(
                    new Factory { Id = 1, Name = "Gebze Akıllı Üretim Kampüsü", Location = "Kocaeli / Gebze OSB", Description = "Ana Otomasyon ve CNC İşleme Merkezi", IsActive = true },
                    new Factory { Id = 2, Name = "İzmir Ege Entegre Tesisi", Location = "İzmir / Çiğli AOSB", Description = "Robotik Montaj ve Lojistik Kampüsü", IsActive = true }
                );
                db.SaveChanges();
            }

            if (!db.ProductionLines.Any())
            {
                db.ProductionLines.AddRange(
                    new ProductionLine { Id = 1, FactoryId = 1, Name = "Montaj & İşleme Hattı A", LineCode = "LINE-A", Description = "Enjeksiyon kalıplama ve CNC frezeleme", IsActive = true },
                    new ProductionLine { Id = 2, FactoryId = 1, Name = "Robotik Hücre Hattı B", LineCode = "LINE-B", Description = "Kuka 6-eksen robotik malzeme besleme", IsActive = true },
                    new ProductionLine { Id = 3, FactoryId = 2, Name = "Paketleme & Lojistik Hattı C", LineCode = "LINE-C", Description = "Akıllı konveyör ve paletleme istasyonu", IsActive = true }
                );
                db.SaveChanges();
            }

            if (!db.Machines.Any())
            {
                db.Machines.AddRange(
                    new Machine
                    {
                        Id = 1,
                        ProductionLineId = 1,
                        MachineCode = "PLC-S7-1200-UNIT-01",
                        Name = "Siemens S7-1200 Akıllı Üretim Ünitesi",
                        Type = "InjectionMolding",
                        PlcIp = "192.168.0.1",
                        IsActive = true,
                        Description = "Ana PLC Üretim Hattı İstasyonu",
                        CreatedAt = DateTime.UtcNow
                    },
                    new Machine
                    {
                        Id = 2,
                        ProductionLineId = 2,
                        MachineCode = "KUKA-KR6-ROBOT-02",
                        Name = "Kuka Robotik Hücre",
                        Type = "RoboticCell",
                        PlcIp = "192.168.0.2",
                        IsActive = true,
                        Description = "Robotik Besleme İstasyonu",
                        CreatedAt = DateTime.UtcNow
                    },
                    new Machine
                    {
                        Id = 3,
                        ProductionLineId = 3,
                        MachineCode = "PACK-CONVEYOR-03",
                        Name = "Akıllı Konveyör & Paketleme",
                        Type = "Packaging",
                        PlcIp = "192.168.0.3",
                        IsActive = true,
                        Description = "Paletleme İstasyonu",
                        CreatedAt = DateTime.UtcNow
                    }
                );
                db.SaveChanges();
            }

            if (!db.MaintenanceTasks.Any())
            {
                db.MaintenanceTasks.AddRange(
                    new MaintenanceTask
                    {
                        MachineId = 1,
                        Component = "Soğutma Sistemi & Termal Eşanjör",
                        Reason = "Periyodik soğutma sıvısı seviyesi ve fan filtre kontrolü",
                        Priority = MaintenancePriority.Medium,
                        Status = MaintenanceStatus.InProgress,
                        AssignedTo = "Vardiya Bakım Ekibi A",
                        CreatedAt = DateTime.UtcNow.AddHours(-2)
                    },
                    new MaintenanceTask
                    {
                        MachineId = 1,
                        Component = "Hidrolik Pompa & Basınç Valfleri",
                        Reason = "Pik basınç dalgalanması tespiti sonrası sızdırmazlık teftişi",
                        Priority = MaintenancePriority.High,
                        Status = MaintenanceStatus.Open,
                        AssignedTo = "Mehmet Usta",
                        CreatedAt = DateTime.UtcNow.AddMinutes(-35)
                    }
                );
                db.SaveChanges();
            }

            Console.WriteLine("[In-Memory DB] Varsayılan fabrikalar, hatlar, makineler, tagler, bakım görevleri ve alarm kuralları belleğe yüklendi.");
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