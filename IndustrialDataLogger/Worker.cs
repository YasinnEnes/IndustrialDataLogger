using IndustrialDataLogger.Services;
using Npgsql;
using Microsoft.Extensions.Configuration;

namespace IndustrialDataLogger
{
    public class Worker : BackgroundService
    {
        private readonly ILogger<Worker> _logger;
        private readonly IPlcService _plcService;
        private readonly string _connectionString;

        // IConfiguration'ı da içeri alarak appsettings.json'daki veritabanı şifremize ulaşıyoruz
        public Worker(ILogger<Worker> logger, IPlcService plcService, IConfiguration configuration)
        {
            _logger = logger;
            _plcService = plcService;
            _connectionString = configuration.GetConnectionString("PostgreSql")!;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Industrial Data Logger başlatıldı...");

            try
            {
                _plcService.Connect();
            }
            catch (Exception ex)
            {
                _logger.LogError($"İlk bağlantı denemesinde hata: {ex.Message}");
            }

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    if (_plcService.IsConnected)
                    {
                        // 1. PLC'den veriyi oku
                        // Veriyi oku
                        var data = await _plcService.ReadSensorDataAsync(); // veya senkron okuma ise ReadSensorData();

                        // EĞER VERİ NULL GELDİYSE KODUN ÇÖKMEMESİ İÇİN KONTROL EKLEYELİM
                        if (data == null)
                        {
                            _logger.LogWarning("PLC'den veri alınamadı, cihaz bağlantısı kurulamıyor olabilir.");
                            return; // Bu turu atla, çökmesini engelle
                        }

                        // Null değilse güvenle logla ve veritabanına yaz
                        _logger.LogInformation($"Okunan Değerler -> Sıcaklık: {data.Temperature}°C | Basınç: {data.Pressure} bar | Motor: {data.MachineStatus}");

                        // 3. Veritabanına kaydet
                        try
                        {
                            using var conn = new NpgsqlConnection(_connectionString);
                            await conn.OpenAsync(stoppingToken);

                            var sql = "INSERT INTO sensordata (timestamp, temperature, pressure, machinestatus) VALUES (@t, @temp, @press, @status)";
                            using var cmd = new NpgsqlCommand(sql, conn);

                            cmd.Parameters.AddWithValue("t", DateTime.Now);
                            cmd.Parameters.AddWithValue("temp", data.Temperature);
                            cmd.Parameters.AddWithValue("press", data.Pressure);
                            cmd.Parameters.AddWithValue("status", data.MachineStatus);

                            await cmd.ExecuteNonQueryAsync(stoppingToken);
                        }
                        catch (Exception dbEx)
                        {
                            _logger.LogError($"Veritabanına yazılırken hata oluştu: {dbEx.Message}");
                        }
                    }
                    else
                    {
                        _logger.LogWarning("PLC'ye bağlı değil! Yeniden bağlanmaya çalışılıyor...");
                        _plcService.Connect();
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Döngü sırasında bir hata oluştu: {ex.Message}");
                }

                // Döngünün sistemi kitlememesi için 1 saniye (1000 milisaniye) bekleme süresi
                await Task.Delay(1000, stoppingToken);
            }

            _plcService.Disconnect();
            _logger.LogInformation("Industrial Data Logger durduruldu.");
        }
    }
}