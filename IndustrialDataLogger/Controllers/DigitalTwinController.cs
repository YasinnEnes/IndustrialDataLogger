using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using IndustrialDataLogger.Data;
using IndustrialDataLogger.Models.DTOs;
using IndustrialDataLogger.Services;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace IndustrialDataLogger.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class DigitalTwinController : ControllerBase
    {
        private readonly IPlcService _plcService;
        private readonly IPlcConnectionManager _connectionManager;
        private readonly IAlarmService _alarmService;
        private readonly IndustrialDbContext _dbContext;

        public DigitalTwinController(
            IPlcService plcService,
            IPlcConnectionManager connectionManager,
            IAlarmService alarmService,
            IndustrialDbContext dbContext)
        {
            _plcService = plcService;
            _connectionManager = connectionManager;
            _alarmService = alarmService;
            _dbContext = dbContext;
        }

        /// <summary>
        /// Tüm dijital ikiz durumunu (sensörler, PLC bağlantısı, alarmlar, KPI istatistikleri) tek bir modelde döner.
        /// </summary>
        [HttpGet("state")]
        public async Task<IActionResult> GetDigitalTwinState(CancellationToken cancellationToken)
        {
            var isConnected = _connectionManager.IsConnected;
            var connectionState = _connectionManager.CurrentState.ToString();

            double temp = 0;
            double pressure = 0;
            bool machineStatus = false;
            short errorCode = 0;

            if (isConnected)
            {
                var sensorData = await _plcService.ReadSensorDataAsync(cancellationToken);
                if (sensorData != null)
                {
                    temp = Math.Round(sensorData.Temperature, 2);
                    pressure = Math.Round(sensorData.Pressure, 2);
                    machineStatus = sensorData.MachineStatus;
                    errorCode = sensorData.ErrorCode;
                }
            }

            var activeAlarms = await _alarmService.GetActiveAlarmsAsync(cancellationToken);

            // KPI İstatistiklerini çek (Performans için optimize edilmiş sorgu)
            var totalCount = await _dbContext.SensorDataLogs.LongCountAsync(cancellationToken);
            double? tempMin = null, tempMax = null, tempAvg = null;
            double? pressMin = null, pressMax = null, pressAvg = null;
            double runningRatio = 0;

            if (totalCount > 0)
            {
                var statsQuery = _dbContext.SensorDataLogs.AsNoTracking();
                tempMin = Math.Round(await statsQuery.MinAsync(s => s.Temperature, cancellationToken), 2);
                tempMax = Math.Round(await statsQuery.MaxAsync(s => s.Temperature, cancellationToken), 2);
                tempAvg = Math.Round(await statsQuery.AverageAsync(s => s.Temperature, cancellationToken), 2);

                pressMin = Math.Round(await statsQuery.MinAsync(s => s.Pressure, cancellationToken), 2);
                pressMax = Math.Round(await statsQuery.MaxAsync(s => s.Pressure, cancellationToken), 2);
                pressAvg = Math.Round(await statsQuery.AverageAsync(s => s.Pressure, cancellationToken), 2);

                var runningCount = await statsQuery.LongCountAsync(s => s.MachineStatus, cancellationToken);
                runningRatio = Math.Round((double)runningCount / totalCount * 100, 2);
            }

            var dto = new DigitalTwinStateDto
            {
                MachineId = "PLC-S7-1200-UNIT-01",
                MachineName = "Siemens S7-1200 Akıllı Üretim Ünitesi",
                Temperature = temp,
                Pressure = pressure,
                MachineStatus = machineStatus,
                ErrorCode = errorCode,
                PlcConnectionState = connectionState,
                IsPlcConnected = isConnected,
                LastUpdate = DateTime.UtcNow,
                ActiveAlarmCount = activeAlarms.Count,
                ActiveAlarms = activeAlarms,
                TotalLogCount = totalCount,
                TemperatureMin = tempMin,
                TemperatureMax = tempMax,
                TemperatureAvg = tempAvg,
                PressureMin = pressMin,
                PressureMax = pressMax,
                PressureAvg = pressAvg,
                MachineRunningRatio = runningRatio
            };

            return Ok(dto);
        }
    }
}
