using Microsoft.Extensions.FileProviders;
using System.IO;
using IndustrialDataLogger;
using IndustrialDataLogger.Services;

var builder = WebApplication.CreateBuilder(args);

// 1. Web API ve Swagger
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// 2. Servis Kayıtları (Tek ve Net Hali)
builder.Services.AddSingleton<MockPlcService>();
builder.Services.AddSingleton<PlcService>();
builder.Services.AddSingleton<IPlcService, HybridPlcService>();

// 3. Arka Planda Veritabanına Yazan Worker
builder.Services.AddHostedService<Worker>();

var app = builder.Build();

// 4. Swagger Middleware
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "Industrial IoT API v1");
});

// 5. Statik Dosyalar (Dashboard)
app.UseStaticFiles();
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(Path.Combine(Directory.GetCurrentDirectory(), "Dashboard")),
    RequestPath = ""
});

app.UseAuthorization();
app.MapControllers();
app.Run("http://localhost:5000");