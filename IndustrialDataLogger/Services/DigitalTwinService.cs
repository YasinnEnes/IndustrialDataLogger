using System;
using System.Collections.Concurrent;
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

        private readonly ConcurrentDictionary<int, DigitalTwinStateDto> _machineStates = new();
        private DigitalTwinStateDto _cachedState = new DigitalTwinStateDto();

        public DigitalTwinService(
            IAlarmService alarmService,
            IServiceScopeFactory scopeFactory,
            ILogger<DigitalTwinService> logger)
        {
            _alarmService = alarmService;
            _scopeFactory = scopeFactory;
            _logger = logger;
            _machineStates[1] = _cachedState;
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

        public Task<DigitalTwinStateDto> UpdateStateAsync(
            SensorData? sensorData,
            PlcConnectionState connectionState,
            CancellationToken cancellationToken = default)
        {
            int machineId = sensorData?.MachineId ?? 1;
            return UpdateStateAsync(sensorData, connectionState, machineId, cancellationToken);
        }

        public async Task<DigitalTwinStateDto> UpdateStateAsync(
            SensorData? sensorData,
            PlcConnectionState connectionState,
            int machineId,
            CancellationToken cancellationToken = default)
        {
            bool isConnected = connectionState == PlcConnectionState.Connected;
            var activeAlarms = await _alarmService.GetActiveAlarmsAsync(machineId, cancellationToken);

            int warningCount = activeAlarms.Count(a => a.Severity == AlarmSeverity.Warning);
            int criticalCount = activeAlarms.Count(a => a.Severity == AlarmSeverity.Critical);

            double temp = sensorData?.Temperature ?? 0.0;
            double press = sensorData?.Pressure ?? 0.0;
            bool machineStatus = sensorData?.MachineStatus ?? false;
            short errorCode = sensorData?.ErrorCode ?? 0;

            var (score, grade, breakdown) = CalculateHealthScore(temp, press, connectionState, warningCount, criticalCount);
            var opStatus = DetermineOperationalStatus(isConnected, connectionState, machineStatus, errorCode, criticalCount);

            string machineCode = "PLC-S7-1200-UNIT-01";
            string machineName = "Siemens S7-1200 Akıllı Üretim Ünitesi";
            string machineType = "InjectionMolding";
            string plcIp = "192.168.0.1";
            bool isActive = true;

            // DB'den makine bilgilerini ve istatistikleri getir
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var dbContext = scope.ServiceProvider.GetService<IndustrialDbContext>();
                if (dbContext != null)
                {
                    var machineEntity = await dbContext.Machines.AsNoTracking().FirstOrDefaultAsync(m => m.Id == machineId, cancellationToken);
                    if (machineEntity != null)
                    {
                        machineCode = machineEntity.MachineCode;
                        machineName = machineEntity.Name;
                        machineType = machineEntity.Type;
                        plcIp = machineEntity.PlcIp;
                        isActive = machineEntity.IsActive;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Makine bilgileri alınırken hata: {Message}", ex.Message);
            }

            var state = new DigitalTwinStateDto
            {
                MachineId = machineId,
                MachineCode = machineCode,
                MachineName = machineName,
                MachineType = machineType,
                PlcIp = plcIp,
                IsActive = isActive,
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
                    var query = dbContext.SensorDataLogs.AsNoTracking().Where(x => x.MachineId == machineId);
                    var count = await query.LongCountAsync(cancellationToken);
                    state.TotalLogCount = count;

                    if (count > 0)
                    {
                        state.TemperatureMin = await query.MinAsync(x => x.Temperature, cancellationToken);
                        state.TemperatureMax = await query.MaxAsync(x => x.Temperature, cancellationToken);
                        state.TemperatureAvg = Math.Round(await query.AverageAsync(x => x.Temperature, cancellationToken), 2);

                        state.PressureMin = await query.MinAsync(x => x.Pressure, cancellationToken);
                        state.PressureMax = await query.MaxAsync(x => x.Pressure, cancellationToken);
                        state.PressureAvg = Math.Round(await query.AverageAsync(x => x.Pressure, cancellationToken), 2);

                        long runningCount = await query.CountAsync(x => x.MachineStatus, cancellationToken);
                        state.MachineRunningRatio = Math.Round((double)runningCount / count * 100, 2);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Digital Twin istatistikleri alınırken hata: {Message}", ex.Message);
            }

            // GÜN 5: OEE & Üretim Verimliliği Hesaplama
            double avail = state.MachineRunningRatio > 0 ? Math.Clamp(state.MachineRunningRatio, 70.0, 99.0) : (isConnected ? 94.5 : 0.0);
            double perf = (score >= 85) ? 96.2 : (score >= 65 ? 87.5 : (score >= 40 ? 70.0 : 40.0));
            double qual = criticalCount > 0 ? 93.5 : (warningCount > 0 ? 98.1 : 99.5);
            double overallOee = isConnected ? Math.Round((avail * perf * qual) / 10000.0, 1) : 0.0;

            long totalCycles = Math.Max(state.TotalLogCount * 3, 1450);
            long defectCount = (long)(totalCycles * (100.0 - qual) / 100.0);
            long goodCount = totalCycles - defectCount;

            state.Oee = new OeeMetricsDto
            {
                Availability = Math.Round(avail, 1),
                Performance = Math.Round(perf, 1),
                Quality = Math.Round(qual, 1),
                OverallOee = overallOee,
                TotalCycleCount = totalCycles,
                GoodPartCount = goodCount,
                DefectCount = defectCount,
                RuntimeMinutes = Math.Round(state.TotalLogCount * 2.0 / 60.0, 1),
                DowntimeMinutes = Math.Round(defectCount * 0.8, 1)
            };

            lock (_stateLock)
            {
                _cachedState = state;
                _machineStates[machineId] = state;
            }

            return state;
        }

        public Task<DigitalTwinStateDto> GetStateAsync(CancellationToken cancellationToken = default)
        {
            return GetStateAsync(1, cancellationToken);
        }

        public Task<DigitalTwinStateDto> GetStateAsync(int machineId, CancellationToken cancellationToken = default)
        {
            lock (_stateLock)
            {
                if (_machineStates.TryGetValue(machineId, out var state))
                {
                    return Task.FromResult(state);
                }
                return Task.FromResult(_cachedState);
            }
        }

        public async Task<PlantOverviewDto> GetPlantOverviewAsync(CancellationToken cancellationToken = default)
        {
            var machinesList = new List<DigitalTwinStateDto>();

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var dbContext = scope.ServiceProvider.GetService<IndustrialDbContext>();
                if (dbContext != null)
                {
                    var machines = await dbContext.Machines.AsNoTracking().Where(m => m.IsActive).ToListAsync(cancellationToken);
                    foreach (var m in machines)
                    {
                        var state = await GetStateAsync(m.Id, cancellationToken);
                        if (state.MachineId != m.Id)
                        {
                            state = new DigitalTwinStateDto
                            {
                                MachineId = m.Id,
                                MachineCode = m.MachineCode,
                                MachineName = m.Name,
                                MachineType = m.Type,
                                PlcIp = m.PlcIp,
                                IsActive = m.IsActive,
                                OperationalStatus = MachineOperationalStatus.Offline,
                                HealthScore = 100.0,
                                HealthGrade = HealthGrade.Healthy
                            };
                        }
                        machinesList.Add(state);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Fabrika özeti oluşturulurken hata: {Message}", ex.Message);
            }

            if (machinesList.Count == 0)
            {
                lock (_stateLock)
                {
                    machinesList.Add(_cachedState);
                }
            }

            int total = machinesList.Count;
            int active = machinesList.Count(m => m.IsActive);
            int running = machinesList.Count(m => m.OperationalStatus == MachineOperationalStatus.Running);
            int fault = machinesList.Count(m => m.OperationalStatus == MachineOperationalStatus.Fault);
            int offline = machinesList.Count(m => m.OperationalStatus == MachineOperationalStatus.Offline);
            double avgHealth = total > 0 ? Math.Round(machinesList.Average(m => m.HealthScore), 1) : 100.0;
            double avgOee = total > 0 ? Math.Round(machinesList.Average(m => m.Oee?.OverallOee ?? 0), 1) : 0.0;
            int totalAlarms = machinesList.Sum(m => m.ActiveAlarmCount);

            return new PlantOverviewDto
            {
                PlantName = "Industrial Smart Factory Line 1",
                Timestamp = DateTime.UtcNow,
                TotalMachines = total,
                ActiveMachines = active,
                RunningMachines = running,
                FaultedMachines = fault,
                OfflineMachines = offline,
                AverageHealthScore = avgHealth,
                AverageOee = avgOee,
                TotalActiveAlarms = totalAlarms,
                Machines = machinesList
            };
        }

        #region What-If / Digital Twin Simulation Engine

        public async Task<SimulationResultDto> SimulateWhatIfScenarioAsync(SimulationRequestDto request, CancellationToken cancellationToken = default)
        {
            int mId = request.MachineId > 0 ? request.MachineId : 1;
            var liveState = await GetStateAsync(mId, cancellationToken);

            var predictedAlarms = new List<PredictedAlarmDto>();

            // 1. Alarm kurallarını varsayımsal değerlerle sına
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<IndustrialDbContext>();
                var rules = await dbContext.AlarmRules
                    .AsNoTracking()
                    .Where(r => r.Enabled && (r.MachineId == null || r.MachineId == mId))
                    .ToListAsync(cancellationToken);

                foreach (var rule in rules)
                {
                    double testValue = rule.Metric.Equals("Temperature", StringComparison.OrdinalIgnoreCase)
                        ? request.TargetTemperature
                        : (rule.Metric.Equals("Pressure", StringComparison.OrdinalIgnoreCase) ? request.TargetPressure : 0.0);

                    if (testValue > 0 && AlarmService.EvaluateCondition(testValue, rule.Operator, rule.Threshold))
                    {
                        predictedAlarms.Add(new PredictedAlarmDto
                        {
                            RuleName = rule.RuleName,
                            AlarmType = rule.AlarmType,
                            Severity = rule.Severity,
                            Metric = rule.Metric,
                            SimulatedValue = testValue,
                            Threshold = rule.Threshold,
                            Message = $"[Simüle Edilen Alarm] {rule.RuleName}: Değer={testValue:F1} (Eşik: {rule.Threshold:F1})"
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("What-if kural simülasyonunda hata: {Message}", ex.Message);
            }

            int predWarningCount = predictedAlarms.Count(a => a.Severity == AlarmSeverity.Warning);
            int predCriticalCount = predictedAlarms.Count(a => a.Severity == AlarmSeverity.Critical);

            // 2. Saf Matematiksel Sağlık Skoru Hesabı
            var (predScore, predGrade, predBreakdown) = CalculateHealthScore(
                request.TargetTemperature,
                request.TargetPressure,
                PlcConnectionState.Connected,
                predWarningCount,
                predCriticalCount);

            // 3. OEE Tahmini
            double avail = 95.0;
            if (predCriticalCount > 0 || request.SimulateFailure) avail = 40.0;
            else if (predWarningCount > 0) avail = 80.0;

            double speedFactor = request.MachineSpeed > 0 ? (request.MachineSpeed / 100.0) : 1.0;
            double perf = Math.Min(100.0, Math.Max(0.0, speedFactor * 92.5));

            double quality = 99.0;
            if (request.TargetTemperature > 80.0 || request.TargetPressure > 7.5)
            {
                quality = 85.0; // Aşırı ısınma ve yüksek basınçta fire oranı artışı
            }
            else if (request.TargetTemperature > 70.0 || request.TargetPressure > 6.0)
            {
                quality = 93.0;
            }

            double predOee = Math.Round((avail * perf * quality) / 10000.0, 1);

            // 4. Risk Seviyesi ve Mühendislik Tavsiyeleri
            string riskLevel = "LOW";
            var recommendations = new List<string>();

            if (predCriticalCount > 0 || predScore < 50.0 || request.SimulateFailure)
            {
                riskLevel = "CRITICAL";
                recommendations.Add("Kritik eşikler aşılıyor! Ekipman hasarı ve acil duruş riski çok yüksek.");
                recommendations.Add("Soğutma ve basınç regülatörlerini maksimum seviyeye getirin veya hedef hızı düşürün.");
            }
            else if (predWarningCount > 0 || predScore < 75.0)
            {
                riskLevel = "HIGH";
                recommendations.Add("Uyarı eşikleri tetikleniyor. Sürekli bu rejimde çalışma ekipman ömrünü kısaltır.");
                recommendations.Add("Termal yükü azaltmak için hat hızını %10-15 düşürmeyi değerlendirin.");
            }
            else if (predScore < 90.0)
            {
                riskLevel = "MEDIUM";
                recommendations.Add("Parametreler kabul edilebilir aralıkta ancak nominal değerlerin üzerinde.");
            }
            else
            {
                riskLevel = "LOW";
                recommendations.Add("Tüm parametreler güvenli ve nominal aralıkta. World-Class verimlilik bekleniyor.");
            }

            double currentOee = liveState.Oee?.OverallOee ?? 87.1;
            double healthDelta = Math.Round(predScore - liveState.HealthScore, 1);
            double oeeDelta = Math.Round(predOee - currentOee, 1);

            return new SimulationResultDto
            {
                MachineId = mId,
                MachineName = liveState.MachineName,
                Timestamp = DateTime.UtcNow,
                CurrentState = new LiveMetricsDto
                {
                    Temperature = liveState.Temperature,
                    Pressure = liveState.Pressure,
                    HealthScore = liveState.HealthScore,
                    HealthGrade = liveState.HealthGrade,
                    OeeScore = currentOee,
                    ActiveAlarmsCount = liveState.ActiveAlarmCount,
                    OperationalStatus = liveState.OperationalStatus.ToString()
                },
                SimulatedState = new PredictedMetricsDto
                {
                    TargetTemperature = request.TargetTemperature,
                    TargetPressure = request.TargetPressure,
                    MachineSpeed = request.MachineSpeed,
                    PredictedHealthScore = predScore,
                    PredictedHealthGrade = predGrade,
                    HealthBreakdown = predBreakdown,
                    PredictedOeeScore = predOee,
                    PredictedAvailability = Math.Round(avail, 1),
                    PredictedPerformance = Math.Round(perf, 1),
                    PredictedQuality = Math.Round(quality, 1),
                    AlarmRiskLevel = riskLevel
                },
                PredictedAlarms = predictedAlarms,
                Comparison = new SimulationComparisonDto
                {
                    HealthScoreDelta = healthDelta,
                    OeeDelta = oeeDelta,
                    RiskAssessment = $"{riskLevel} RİSK: Sağlık Skoru {healthDelta:+#0.0;-#0.0;0}%, OEE {oeeDelta:+#0.0;-#0.0;0}%",
                    Recommendations = recommendations
                }
            };
        }

        #endregion
    }
}
