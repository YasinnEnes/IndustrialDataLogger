using System.Text;
using IndustrialMonitoring.API.Data;
using IndustrialMonitoring.API.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.IdentityModel.Tokens;
using System.IO;

var builder = WebApplication.CreateBuilder(args);

// 1. PostgreSQL DbContext Bağlantısı
var connectionString = Environment.GetEnvironmentVariable("POSTGRES_CONNECTION_STRING")
    ?? builder.Configuration.GetConnectionString("DefaultConnection");

if (string.IsNullOrWhiteSpace(connectionString) || connectionString.Contains("YOUR_POSTGRES_PASSWORD"))
{
    var pgHost = Environment.GetEnvironmentVariable("POSTGRES_HOST") ?? "localhost";
    var pgPort = Environment.GetEnvironmentVariable("POSTGRES_PORT") ?? "5432";
    var pgDb = Environment.GetEnvironmentVariable("POSTGRES_DB") ?? "IndustrialMonitoringDb";
    var pgUser = Environment.GetEnvironmentVariable("POSTGRES_USER") ?? "postgres";
    var pgPass = Environment.GetEnvironmentVariable("POSTGRES_PASSWORD") ?? "postgres";
    connectionString = $"Host={pgHost};Port={pgPort};Database={pgDb};Username={pgUser};Password={pgPass}";
}

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(connectionString));

// 2. Dependency Injection Kaydı (AuthService & Repositories)
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IndustrialMonitoring.API.Repositories.Interfaces.IRepository<IndustrialMonitoring.API.Models.Entities.SensorData>, IndustrialMonitoring.API.Repositories.SensorRepository>();

// 3. JWT Bearer Authentication Yapılandırması
var jwtSettings = builder.Configuration.GetSection("JwtSettings");
var secretKeyStr = Environment.GetEnvironmentVariable("JWT_SECRET")
    ?? jwtSettings["SecretKey"]
    ?? jwtSettings["Secret"];

if (string.IsNullOrWhiteSpace(secretKeyStr) || secretKeyStr.Contains("YOUR_SECURE_JWT_SECRET_KEY") || secretKeyStr.Length < 32)
{
    secretKeyStr = "IndustrialDigitalTwinSuperSecretKeyThatIsLongEnough123!";
}

var secretKey = Encoding.UTF8.GetBytes(secretKeyStr);

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = jwtSettings["Issuer"] ?? "IndustrialMonitoringAPI",
        ValidAudience = jwtSettings["Audience"] ?? "IndustrialMonitoringClient",
        IssuerSigningKey = new SymmetricSecurityKey(secretKey)
    };
});

builder.Services.AddControllers();

var app = builder.Build();

app.UseHttpsRedirection();

// 4. KÖK DİZİN VE STATİK DOSYA SUNUMU
app.UseDefaultFiles(new DefaultFilesOptions
{
    DefaultFileNames = new List<string> { "login.html" }
});

app.UseStaticFiles(); // wwwroot varsa sunar

// Dashboard klasörü mevcutsa web üzerinden erişilebilir yapar
var dashboardPath = Path.Combine(Directory.GetCurrentDirectory(), "Dashboard");
if (Directory.Exists(dashboardPath))
{
    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = new PhysicalFileProvider(dashboardPath),
        RequestPath = ""
    });
}

// KRİTİK MİMARİ KURAL: UseAuthentication, UseAuthorization'dan ÖNCE yazılmalıdır!
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

// Port ayarı (İsteğe bağlı olarak sabitlenebilir)
app.Run("http://localhost:5000");