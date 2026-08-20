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

// GÜN 4 (Sprint 4.1): Security Hardening - Environment Variable & JWT Konfigürasyonu
var connectionString = Environment.GetEnvironmentVariable("POSTGRES_CONNECTION_STRING")
    ?? builder.Configuration.GetConnectionString("PostgreSql")
    ?? "Host=localhost;Port=5432;Database=IndustrialDataDB;Username=postgres;Password=1234";

builder.Services.AddDbContext<IndustrialDbContext>(options =>
    options.UseNpgsql(connectionString));

var jwtSecret = Environment.GetEnvironmentVariable("JWT_SECRET")
    ?? builder.Configuration["JwtSettings:Secret"]
    ?? "IndustrialDataLogger_SuperSecret_Jwt_Key_2026_Production_Secure_Key_998877665544";

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

            CREATE TABLE IF NOT EXISTS plctagconfigs (
                id BIGSERIAL PRIMARY KEY,
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
        ");

        var tagConfigService = scope.ServiceProvider.GetRequiredService<ITagConfigService>();
        tagConfigService.EnsureDefaultTagsSeededAsync().GetAwaiter().GetResult();
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

// GÜN 4 (Sprint 4.1): Authentication & Authorization Middleware
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

// GÜN 3 - SignalR Hub Endpoint Eşlemesi
app.MapHub<MonitoringHub>("/sensorHub");

var port = Environment.GetEnvironmentVariable("PORT") ?? "5000";
app.Run($"http://0.0.0.0:{port}");