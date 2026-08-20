using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IndustrialDataLogger.Data;
using IndustrialDataLogger.Enums;
using IndustrialDataLogger.Models;
using IndustrialDataLogger.Models.DTOs;
using IndustrialDataLogger.Models.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace IndustrialDataLogger.Services
{
    public class DigitalTwinService : IDigitalTwinService
    {
        private readonly IAlarmService _alarmService;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<DigitalTwinService> _logger;
        private readonly object _stateLock = new object();

        private DigitalTwinStateDto _cachedState = new DigitalTwinStateDto();

        public DigitalTwinService(
            IAlarmService alarmService,
            IServiceScopeFactory scopeFactory,
            ILogger<DigitalTwinService> logger)
        {
            _alarmService = alarmService;
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        public (double score, HealthGrade grade, HealthScoreBreakdownDto breakdown) CalculateHealthScore(
            double temperature,
            double pressure,
            PlcConnectionState connectionState,
            int warningAlarmCount,
            int criticalAlarmCount)
        {
            var breakdown = new HealthScoreBreakdownDto();

            // 1. Sıcaklık Puanı (Maks: 25)
            if (temperature >= 20.0 && temperature <= 60.0)
            {
                breakdown.TemperatureScore = 25.0;
            }
            else if (temperature > 60.0 && temperature <= 70.0)
            {
                breakdown.TemperatureScore = 20.0;
            }
            else if (temperature > 70.0 && temperature <= 85.0)
            {
                breakdown.TemperatureScore = 10.0;
            }
            else
            {
                breakdown.TemperatureScore = 0.0; // >85°C veya aşırı donma
            }

            // 2. Basınç Puanı (Maks: 20)
            if (pressure >= 2.0 && pressure <= 6.0)
            {
                breakdown.PressureScore = 20.0;
            }
            else if (pressure > 6.0 && pressure <= 7.5)
            {
                breakdown.PressureScore = 15.0;
            }
            else if (pressure > 7.5 && pressure <= 9.0)
            {
                breakdown.PressureScore = 8.0;
            }
            else
            {
                breakdown.PressureScore = 0.0; // >9.0 bar kritik
            }

            // 3. Bağlantı Puanı (Maks: 20)
            if (connectionState == PlcConnectionState.Connected)
            {
                breakdown.ConnectionScore = 20.0;
            }
            else if (connectionState == PlcConnectionState.Connecting || connectionState == PlcConnectionState.Reconnecting)
            {
                breakdown.ConnectionScore = 5.0;
            }
            else
            {
                breakdown.ConnectionScore = 0.0;
            }

            // 4. Alarm Puanı (Maks: 35)
            if (criticalAlarmCount > 0)
            {
                breakdown.AlarmScore = 0.0; // Kritik alarm varsa alarm puanı sıfır
            }
            else if (warningAlarmCount == 1)
            {
                breakdown.AlarmScore = 20.0;
            }
            else if (warningAlarmCount >= 2)
            {
                breakdown.AlarmScore = 10.0;
            }
            else
            {
                breakdown.AlarmScore = 35.0; // Hiç alarm yoksa tam puan
            }

            double totalScore = breakdown.TotalScore;

            HealthGrade grade;
            if (totalScore >= 85.0)
            {
                grade = HealthGrade.Healthy;
            }
            else if (totalScore >= 65.0)
            {
                grade = HealthGrade.Warning;
            }
            else if (totalScore >= 40.0)
            {
                grade = HealthGrade.Degraded;
            }
            else
            {
                grade = HealthGrade.Critical;
            }

            return (totalScore, grade, breakdown);
        }

        public MachineOperationalStatus DetermineOperationalStatus(
            bool isConnected,
            PlcConnectionState connectionState,
            bool machineStatus,
            short errorCode,
            int criticalAlarmCount)
        {
            if (!isConnected || connectionState == PlcConnectionState.Disconnected)
            {
                return MachineOperationalStatus.Offline;
            }

            if (criticalAlarmCount > 0 || errorCode != 0)
            {
                return MachineOperationalStatus.Fault;
            }

            return machineStatus ? MachineOperationalStatus.Running : MachineOperationalStatus.Stopped;
        }

        public async Task<DigitalTwinStateDto> UpdateStateAsync(
            SensorData? sensorData,
            PlcConnectionState connectionState,
            CancellationToken cancellationToken = default)
        {
            bool isConnected = connectionState == PlcConnectionState.Connected;
            var activeAlarms = await _alarmService.GetActiveAlarmsAsync(cancellationToken);

            int warningCount = activeAlarms.Count(a => a.Severity == AlarmSeverity.Warning);
            int criticalCount = activeAlarms.Count(a => a.Severity == AlarmSeverity.Critical);

            double temp = sensorData?.Temperature ?? 0.0;
            double press = sensorData?.Pressure ?? 0.0;
            bool machineStatus = sensorData?.MachineStatus ?? false;
            short errorCode = sensorData?.ErrorCode ?? 0;

            var (score, grade, breakdown) = CalculateHealthScore(temp, press, connectionState, warningCount, criticalCount);
            var opStatus = DetermineOperationalStatus(isConnected, connectionState, machineStatus, errorCode, criticalCount);

            var state = new DigitalTwinStateDto
            {
                MachineId = "PLC-S7-1200-UNIT-01",
                MachineName = "Siemens S7-1200 Akıllı Üretim Ünitesi",
                Temperature = Math.Round(temp, 2),
                Pressure = Math.Round(press, 2),
                MachineStatus = machineStatus,
                PlcConnectionState = connectionState.ToString(),
                IsPlcConnected = isConnected,
                LastUpdate = DateTime.UtcNow,
                ErrorCode = errorCode,
                OperationalStatus = opStatus,
                HealthScore = score,
                HealthGrade = grade,
                HealthBreakdown = breakdown,
                ActiveAlarmCount = activeAlarms.Count,
                ActiveAlarms = activeAlarms
            };

            // DB İstatistiklerini getir
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var dbContext = scope.ServiceProvider.GetService<IndustrialDbContext>();
                if (dbContext != null)
                {
                    var count = await dbContext.SensorDataLogs.AsNoTracking().LongCountAsync(cancellationToken);
                    state.TotalLogCount = count;

                    if (count > 0)
                    {
                        state.TemperatureMin = await dbContext.SensorDataLogs.AsNoTracking().MinAsync(x => x.Temperature, cancellationToken);
                        state.TemperatureMax = await dbContext.SensorDataLogs.AsNoTracking().MaxAsync(x => x.Temperature, cancellationToken);
                        state.TemperatureAvg = Math.Round(await dbContext.SensorDataLogs.AsNoTracking().AverageAsync(x => x.Temperature, cancellationToken), 2);

                        state.PressureMin = await dbContext.SensorDataLogs.AsNoTracking().MinAsync(x => x.Pressure, cancellationToken);
                        state.PressureMax = await dbContext.SensorDataLogs.AsNoTracking().MaxAsync(x => x.Pressure, cancellationToken);
                        state.PressureAvg = Math.Round(await dbContext.SensorDataLogs.AsNoTracking().AverageAsync(x => x.Pressure, cancellationToken), 2);

                        long runningCount = await dbContext.SensorDataLogs.AsNoTracking().CountAsync(x => x.MachineStatus, cancellationToken);
                        state.MachineRunningRatio = Math.Round((double)runningCount / count * 100, 2);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Digital Twin istatistikleri alınırken hata: {Message}", ex.Message);
            }

            lock (_stateLock)
            {
                _cachedState = state;
            }

            return state;
        }

        public Task<DigitalTwinStateDto> GetStateAsync(CancellationToken cancellationToken = default)
        {
            lock (_stateLock)
            {
                return Task.FromResult(_cachedState);
            }
        }
    }
}
