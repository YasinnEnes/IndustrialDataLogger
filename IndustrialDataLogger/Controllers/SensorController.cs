using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using IndustrialDataLogger.Models;
using IndustrialDataLogger.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace IndustrialDataLogger.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class SensorController : ControllerBase
    {
        private readonly IPlcService _plcService;
        private readonly string _connectionString;
        private readonly ILogger<SensorController> _logger;

        public SensorController(IPlcService plcService, IConfiguration configuration, ILogger<SensorController> logger)
        {
            _plcService = plcService;
            _connectionString = configuration.GetConnectionString("PostgreSql") ?? string.Empty;
            _logger = logger;
        }

        // 1. En son veriyi doğrudan PLC'den anlık okur
        [HttpGet("latest")]
        public async Task<IActionResult> GetLatestData(CancellationToken cancellationToken)
        {
            var data = await _plcService.ReadSensorDataAsync(cancellationToken);

            // Eğer servis null dönerse (bağlantı yoksa), 404 veya 503 dön
            if (data == null)
            {
                return StatusCode(503, new { message = "PLC bağlantısı yok, veri alınamıyor." });
            }
            return Ok(data);
        }

        // 2. PostgreSQL'den son N adet geçmiş kaydı getirir
        [HttpGet("history")]
        public async Task<IActionResult> GetHistory([FromQuery] int limit = 50, CancellationToken cancellationToken = default)
        {
            var result = new List<SensorData>();

            try
            {
                using var conn = new NpgsqlConnection(_connectionString);
                await conn.OpenAsync(cancellationToken);

                var sql = "SELECT timestamp, temperature, pressure, machinestatus FROM sensordata ORDER BY timestamp DESC LIMIT @limit";
                using var cmd = new NpgsqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("limit", limit);

                using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    result.Add(new SensorData
                    {
                        Timestamp = reader.GetDateTime(0),
                        Temperature = Math.Round(reader.GetDouble(1), 2),
                        Pressure = Math.Round(reader.GetDouble(2), 2),
                        MachineStatus = reader.GetBoolean(3)
                    });
                }

                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Geçmiş veri okuma hatası: {ex.Message}");
                return StatusCode(500, new { message = "Veritabanı okuma hatası." });
            }
        }

        // 3. PLC'ye değişken/komut yazar
        [HttpPost("write")]
        public async Task<IActionResult> WritePlcData([FromBody] PlcWriteRequest request, CancellationToken cancellationToken)
        {
            if (!_plcService.IsConnected)
            {
                return StatusCode(503, new { message = "PLC bağlantısı yok." });
            }

            var success = await _plcService.WriteDataAsync(request, cancellationToken);
            if (success)
            {
                return Ok(new { message = $"'{request.VariableName}' adresine değer başarıyla yazıldı.", value = request.Value });
            }

            return BadRequest(new { message = "PLC yazma işlemi başarısız oldu." });
        }

        // 4. Mod Yönetimi (Simülasyon / Gerçek PLC)
        [HttpGet("mode")]
        public IActionResult GetMode()
        {
            return Ok(new { isSimulation = HybridPlcService.IsSimulationMode });
        }

        [HttpPost("mode")]
        public IActionResult SetMode([FromBody] ModeModel model)
        {
            HybridPlcService.IsSimulationMode = model.IsSimulation;
            return Ok(new { success = true, isSimulation = HybridPlcService.IsSimulationMode });
        }

        public class ModeModel
        {
            public bool IsSimulation { get; set; }
        }

        // 5. PLC Bağlantı Durumu ve Yönetim Uç Noktaları
        [HttpGet("connection-status")]
        public IActionResult GetConnectionStatus()
        {
            return Ok(new { isConnected = _plcService.IsConnected });
        }

        [HttpPost("connect")]
        public async Task<IActionResult> ConnectPlc(CancellationToken cancellationToken)
        {
            try
            {
                await _plcService.ConnectAsync(cancellationToken);
                return Ok(new { success = true, message = "PLC bağlantısı başarıyla kuruldu." });
            }
            catch (Exception ex)
            {
                return BadRequest(new { success = false, error = ex.Message });
            }
        }

        [HttpPost("disconnect")]
        public IActionResult DisconnectPlc() // Hiçbir parametre ve async/await yok
        {
            try
            {
                _plcService.Disconnect(); // Senkron çağırıyoruz (daha garanti)
                return Ok(new { success = true, message = "PLC bağlantısı kesildi." });
            }
            catch (Exception ex)
            {
                return BadRequest(new { success = false, error = ex.Message });
            }
        }
    }
}