using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IndustrialDataLogger.Data;
using IndustrialDataLogger.Models;
using IndustrialDataLogger.Models.Entities;
using IndustrialDataLogger.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace IndustrialDataLogger.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class SensorController : ControllerBase
    {
        private readonly IPlcService _plcService;
        private readonly IPlcConnectionManager _plcConnectionManager;
        private readonly IndustrialDbContext _dbContext;
        private readonly IEventLogService _eventLogService;
        private readonly ILogger<SensorController> _logger;

        public SensorController(
            IPlcService plcService,
            IPlcConnectionManager plcConnectionManager,
            IndustrialDbContext dbContext,
            IEventLogService eventLogService,
            ILogger<SensorController> logger)
        {
            _plcService = plcService;
            _plcConnectionManager = plcConnectionManager;
            _dbContext = dbContext;
            _eventLogService = eventLogService;
            _logger = logger;
        }

        [HttpGet("latest")]
        public async Task<IActionResult> GetLatestData(CancellationToken cancellationToken)
        {
            if (!_plcConnectionManager.IsConnected)
            {
                return StatusCode(503, new { message = "PLC bağlantısı kurulmadığı için veri alınamıyor." });
            }

            var data = await _plcService.ReadSensorDataAsync(cancellationToken);
            if (data == null)
            {
                return StatusCode(503, new { message = "PLC bağlantısı yok, veri alınamıyor." });
            }
            return Ok(data);
        }

        // Sprint 2.4 & 2.5: Optimize Edilmiş Historical Data API (Tarih filtreleme, Sayfalama, Makine Filtreleme, AsNoTracking)
        [HttpGet("history")]
        public async Task<IActionResult> GetHistory(
            [FromQuery] DateTime? startDate,
            [FromQuery] DateTime? endDate,
            [FromQuery] int? machineId,
            [FromQuery] int limit = 50,
            [FromQuery] int skip = 0,
            [FromQuery] bool? machineStatus = null,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var query = _dbContext.SensorDataLogs.AsNoTracking();

                if (machineId.HasValue)
                {
                    query = query.Where(x => x.MachineId == machineId.Value);
                }

                if (startDate.HasValue)
                {
                    var startUtc = DateTime.SpecifyKind(startDate.Value, DateTimeKind.Utc);
                    query = query.Where(x => x.Timestamp >= startUtc);
                }

                if (endDate.HasValue)
                {
                    var endUtc = DateTime.SpecifyKind(endDate.Value, DateTimeKind.Utc);
                    query = query.Where(x => x.Timestamp <= endUtc);
                }

                if (machineStatus.HasValue)
                {
                    query = query.Where(x => x.MachineStatus == machineStatus.Value);
                }

                int safeLimit = Math.Clamp(limit, 1, 500);
                int safeSkip = Math.Max(0, skip);

                var data = await query
                    .OrderByDescending(x => x.Timestamp)
                    .Skip(safeSkip)
                    .Take(safeLimit)
                    .Select(x => new
                    {
                        x.Id,
                        x.MachineId,
                        x.Timestamp,
                        Temperature = Math.Round(x.Temperature, 2),
                        Pressure = Math.Round(x.Pressure, 2),
                        x.MachineStatus,
                        x.ErrorCode
                    })
                    .ToListAsync(cancellationToken);

                return Ok(data);
            }
            catch (Exception ex)
            {
                _logger.LogError("Geçmiş veri okuma hatası: {Message}", ex.Message);
                return StatusCode(500, new { message = "Veritabanı okuma hatası: " + ex.Message });
            }
        }

        // Sprint 2.4: İstatistik & Özet Veri Endpoint'i
        [HttpGet("history/stats")]
        public async Task<IActionResult> GetHistoryStats(
            [FromQuery] DateTime? startDate,
            [FromQuery] DateTime? endDate,
            [FromQuery] int? machineId,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var query = _dbContext.SensorDataLogs.AsNoTracking();

                if (machineId.HasValue)
                {
                    query = query.Where(x => x.MachineId == machineId.Value);
                }

                if (startDate.HasValue)
                {
                    var startUtc = DateTime.SpecifyKind(startDate.Value, DateTimeKind.Utc);
                    query = query.Where(x => x.Timestamp >= startUtc);
                }

                if (endDate.HasValue)
                {
                    var endUtc = DateTime.SpecifyKind(endDate.Value, DateTimeKind.Utc);
                    query = query.Where(x => x.Timestamp <= endUtc);
                }

                var count = await query.CountAsync(cancellationToken);
                if (count == 0)
                {
                    return Ok(new
                    {
                        totalRecords = 0,
                        message = "Belirtilen aralıkta veri bulunamadı."
                    });
                }

                var minTemp = await query.MinAsync(x => x.Temperature, cancellationToken);
                var maxTemp = await query.MaxAsync(x => x.Temperature, cancellationToken);
                var avgTemp = await query.AverageAsync(x => x.Temperature, cancellationToken);

                var minPress = await query.MinAsync(x => x.Pressure, cancellationToken);
                var maxPress = await query.MaxAsync(x => x.Pressure, cancellationToken);
                var avgPress = await query.AverageAsync(x => x.Pressure, cancellationToken);

                var runningCount = await query.CountAsync(x => x.MachineStatus, cancellationToken);

                return Ok(new
                {
                    totalRecords = count,
                    temperature = new
                    {
                        min = Math.Round(minTemp, 2),
                        max = Math.Round(maxTemp, 2),
                        avg = Math.Round(avgTemp, 2)
                    },
                    pressure = new
                    {
                        min = Math.Round(minPress, 2),
                        max = Math.Round(maxPress, 2),
                        avg = Math.Round(avgPress, 2)
                    },
                    machineRunningRatio = Math.Round((double)runningCount / count * 100, 2)
                });
            }
            catch (Exception ex)
            {
                _logger.LogError("İstatistik hesaplama hatası: {Message}", ex.Message);
                return StatusCode(500, new { message = "İstatistik hesaplama hatası: " + ex.Message });
            }
        }

        [HttpPost("write")]
        public async Task<IActionResult> WritePlcData([FromBody] PlcWriteRequest request, CancellationToken cancellationToken)
        {
            if (!_plcConnectionManager.IsConnected)
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

        [HttpGet("mode")]
        public IActionResult GetMode()
        {
            return Ok(new { isSimulation = HybridPlcService.IsSimulationMode });
        }

        [HttpPost("mode")]
        public async Task<IActionResult> SetMode([FromBody] ModeModel model, CancellationToken cancellationToken)
        {
            await _plcConnectionManager.SetModeAsync(model.IsSimulation, cancellationToken);
            return Ok(new { success = true, isSimulation = PlcConnectionManager.IsSimulationMode, isConnected = _plcConnectionManager.IsConnected });
        }

        public class ModeModel
        {
            public bool IsSimulation { get; set; }
        }

        // Sprint 1.4 & 1.5: Simülasyon Senaryo Yönetimi API
        [HttpGet("scenario")]
        public IActionResult GetScenario()
        {
            return Ok(new
            {
                scenario = _plcConnectionManager.CurrentScenario.ToString(),
                availableScenarios = Enum.GetNames<IndustrialDataLogger.Enums.SimulationScenario>()
            });
        }

        [HttpPost("scenario")]
        public async Task<IActionResult> SetScenario([FromBody] ScenarioRequestModel model, CancellationToken cancellationToken)
        {
            if (Enum.TryParse<IndustrialDataLogger.Enums.SimulationScenario>(model.Scenario, true, out var scenario))
            {
                _plcConnectionManager.SetSimulationScenario(scenario);
                if (scenario == IndustrialDataLogger.Enums.SimulationScenario.PlcDisconnect || scenario == IndustrialDataLogger.Enums.SimulationScenario.PlcFailure)
                {
                    await _plcConnectionManager.DisconnectAsync();
                }
                await _eventLogService.LogEventAsync("SCENARIO_CHANGED", $"Simülasyon senaryosu '{scenario}' olarak değiştirildi.", IndustrialDataLogger.Enums.AlarmSeverity.Info, "SimulationEngine", cancellationToken);
                return Ok(new { success = true, scenario = scenario.ToString(), isConnected = _plcConnectionManager.IsConnected, message = $"Simülasyon senaryosu '{scenario}' olarak ayarlandı." });
            }
            return BadRequest(new { success = false, message = $"Geçersiz senaryo: {model.Scenario}. Geçerli senaryolar: {string.Join(", ", Enum.GetNames<IndustrialDataLogger.Enums.SimulationScenario>())}" });
        }

        public class ScenarioRequestModel
        {
            public string Scenario { get; set; } = "Normal";
        }

        [HttpGet("connection-status")]
        public IActionResult GetConnectionStatus()
        {
            return Ok(new { isConnected = _plcConnectionManager.IsConnected, state = _plcConnectionManager.CurrentState.ToString() });
        }

        [HttpPost("connect")]
        public async Task<IActionResult> ConnectPlc(CancellationToken cancellationToken)
        {
            try
            {
                if (PlcConnectionManager.SimulateConnectionError)
                {
                    return BadRequest(new { success = false, message = "Bağlantı Kurulamadı: Ağ kablosu takılı değil veya TIA Portal projesindeki DB yapıları uyuşmuyor!" });
                }

                var success = await _plcConnectionManager.ConnectAsync(cancellationToken);
                if (success)
                {
                    string modeText = HybridPlcService.IsSimulationMode ? "Simülasyon Bağlantısı" : "Gerçek PLC Bağlantısı";
                    return Ok(new { success = true, message = $"{modeText} başarıyla kuruldu." });
                }

                return BadRequest(new { success = false, message = "PLC bağlantısı kurulamadı!" });
            }
            catch (Exception ex)
            {
                return BadRequest(new { success = false, message = ex.Message });
            }
        }

        [HttpPost("disconnect")]
        public async Task<IActionResult> DisconnectPlc()
        {
            try
            {
                await _plcConnectionManager.DisconnectAsync();
                return Ok(new { success = true, message = "PLC bağlantısı tamamen kesildi." });
            }
            catch (Exception ex)
            {
                return BadRequest(new { success = false, message = ex.Message });
            }
        }

        [HttpPost("send-command")]
        public async Task<IActionResult> SendCommand([FromBody] CommandModel model, CancellationToken cancellationToken)
        {
            if (!_plcConnectionManager.IsConnected)
            {
                return StatusCode(503, new { success = false, message = "PLC bağlantısı kapalı olduğu için komut iletilemez. Lütfen önce PLC bağlantısını kurun." });
            }

            string varName = !string.IsNullOrWhiteSpace(model.VariableName) ? model.VariableName.Trim() : "DB1.DBD0";
            string dataType = !string.IsNullOrWhiteSpace(model.DataType) ? model.DataType.ToUpperInvariant() : "REAL";
            object typedValue;

            try
            {
                if (!string.IsNullOrWhiteSpace(model.Value))
                {
                    switch (dataType)
                    {
                        case "BOOL":
                            typedValue = model.Value.Equals("true", StringComparison.OrdinalIgnoreCase) || model.Value == "1";
                            break;
                        case "INT":
                            typedValue = short.Parse(model.Value);
                            break;
                        case "DINT":
                            typedValue = int.Parse(model.Value);
                            break;
                        case "STRING":
                            typedValue = model.Value;
                            break;
                        case "REAL":
                        case "FLOAT":
                        default:
                            typedValue = float.Parse(model.Value.Replace(',', '.'), System.Globalization.CultureInfo.InvariantCulture);
                            break;
                    }
                }
                else if (model.Setpoint.HasValue)
                {
                    typedValue = (float)model.Setpoint.Value;
                }
                else
                {
                    return BadRequest(new { success = false, message = "Yazılacak değer boş olamaz." });
                }
            }
            catch (Exception ex)
            {
                return BadRequest(new { success = false, message = $"'{model.Value}' değeri '{dataType}' veri tipine dönüştürülemedi: {ex.Message}" });
            }

            var request = new PlcWriteRequest
            {
                VariableName = varName,
                DataType = dataType,
                Value = typedValue
            };

            var success = await _plcService.WriteDataAsync(request, cancellationToken);
            if (success)
            {
                string logMsg = $"PLC Değişkeni Yazıldı: {varName} ({dataType}) = {typedValue}";
                await _eventLogService.LogEventAsync("PLC_COMMAND_SENT", logMsg, IndustrialDataLogger.Enums.AlarmSeverity.Info, "ControlPanel", cancellationToken);

                return Ok(new
                {
                    success = true,
                    message = $"Komut başarıyla iletildi. {varName} ({dataType}) adresine '{typedValue}' yazıldı.",
                    variableName = varName,
                    dataType = dataType,
                    value = typedValue,
                    timestamp = DateTime.UtcNow
                });
            }

            return BadRequest(new { success = false, message = $"PLC '{varName}' adresine yazma işlemi başarısız oldu." });
        }
    }
}