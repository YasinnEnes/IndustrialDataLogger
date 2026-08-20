using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using IndustrialDataLogger.Data;
using IndustrialDataLogger.Services;
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace IndustrialDataLogger.Controllers
{
    [ApiController]
    public class HealthController : ControllerBase
    {
        private readonly IndustrialDbContext _dbContext;
        private readonly IPlcConnectionManager _connectionManager;
        private readonly IDigitalTwinService _digitalTwinService;
        private static readonly DateTime _appStartTime = DateTime.UtcNow;

        public HealthController(
            IndustrialDbContext dbContext,
            IPlcConnectionManager connectionManager,
            IDigitalTwinService digitalTwinService)
        {
            _dbContext = dbContext;
            _connectionManager = connectionManager;
            _digitalTwinService = digitalTwinService;
        }

        /// <summary>
        /// API, Veritabanı ve PLC sağlık durumlarını tek bakışta gösteren gözlemlenebilirlik endpoint'i.
        /// </summary>
        [HttpGet("health")]
        [HttpGet("api/health")]
        public async Task<IActionResult> GetHealth(CancellationToken cancellationToken)
        {
            var sw = Stopwatch.StartNew();
            bool dbHealthy = false;
            string dbMessage = "OK";

            try
            {
                dbHealthy = await _dbContext.Database.CanConnectAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                dbHealthy = false;
                dbMessage = ex.Message;
            }

            var plcConnected = _connectionManager.IsConnected;
            var plcState = _connectionManager.CurrentState.ToString();
            var twinState = await _digitalTwinService.GetStateAsync(cancellationToken);
            sw.Stop();

            var process = Process.GetCurrentProcess();
            var memoryMb = Math.Round((double)process.WorkingSet64 / (1024 * 1024), 2);
            var uptime = DateTime.UtcNow - _appStartTime;

            string overallStatus = (dbHealthy && plcConnected) ? "Healthy" : (dbHealthy ? "Degraded" : "Unhealthy");

            var response = new
            {
                status = overallStatus,
                timestamp = DateTime.UtcNow,
                responseTimeMs = sw.ElapsedMilliseconds,
                uptime = $"{uptime.Days}d {uptime.Hours}h {uptime.Minutes}m {uptime.Seconds}s",
                components = new
                {
                    api = new
                    {
                        status = "Healthy",
                        version = "1.0.0",
                        framework = ".NET 10.0",
                        memoryUsageMb = memoryMb
                    },
                    database = new
                    {
                        status = dbHealthy ? "Healthy" : "Unhealthy",
                        provider = "PostgreSQL (Npgsql)",
                        details = dbMessage
                    },
                    plc = new
                    {
                        status = plcConnected ? "Healthy" : "Degraded",
                        connectionState = plcState,
                        isSimulation = PlcConnectionManager.IsSimulationMode,
                        scenario = _connectionManager.CurrentScenario.ToString()
                    },
                    digitalTwin = new
                    {
                        operationalStatus = twinState.OperationalStatus.ToString(),
                        healthScore = twinState.HealthScore,
                        healthGrade = twinState.HealthGrade.ToString(),
                        activeAlarms = twinState.ActiveAlarmCount
                    }
                }
            };

            return overallStatus == "Unhealthy" ? StatusCode(503, response) : Ok(response);
        }
    }
}
