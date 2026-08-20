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
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

// 2. Dependency Injection Kaydı (AuthService & Repositories)
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IndustrialMonitoring.API.Repositories.Interfaces.IRepository<IndustrialMonitoring.API.Models.Entities.SensorData>, IndustrialMonitoring.API.Repositories.SensorRepository>();

// 3. JWT Bearer Authentication Yapılandırması
var jwtSettings = builder.Configuration.GetSection("JwtSettings");
var secretKey = Encoding.UTF8.GetBytes(jwtSettings["SecretKey"] ?? throw new InvalidOperationException("SecretKey is missing"));

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
        ValidIssuer = jwtSettings["Issuer"],
        ValidAudience = jwtSettings["Audience"],
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