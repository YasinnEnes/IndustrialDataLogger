using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using IndustrialDataLogger.Data;
using IndustrialDataLogger.Enums;
using IndustrialDataLogger.Models;
using IndustrialDataLogger.Models.DTOs;
using IndustrialDataLogger.Models.Entities;
using IndustrialDataLogger.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace IndustrialDataLogger.Tests.UnitTests
{
    public class WhatIfSimulationTests
    {
        private (DigitalTwinService service, ServiceProvider provider) CreateDigitalTwinService()
        {
            var services = new ServiceCollection();
            var dbName = "WhatIfTestDb_" + Guid.NewGuid();
            services.AddDbContext<IndustrialDbContext>(opt => opt.UseInMemoryDatabase(dbName));

            services.AddLogging();
            var serviceProvider = services.BuildServiceProvider();

            // Seed sample machine and alarm rules
            using (var scope = serviceProvider.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<IndustrialDbContext>();
                db.Machines.Add(new Machine
                {
                    Id = 1,
                    MachineCode = "M-01",
                    Name = "Ana CNC Freze & Enjeksiyon",
                    Type = "CNC",
                    PlcIp = "192.168.0.1",
                    IsActive = true
                });

                db.AlarmRules.AddRange(
                    new AlarmRule
                    {
                        Id = 1,
                        MachineId = 1,
                        RuleName = "Yüksek Sıcaklık Uyarısı",
                        AlarmType = "TEMP_WARNING",
                        Metric = "Temperature",
                        Operator = ComparisonOperator.GreaterThan,
                        Threshold = 80.0,
                        Severity = AlarmSeverity.Warning,
                        Enabled = true
                    },
                    new AlarmRule
                    {
                        Id = 2,
                        MachineId = 1,
                        RuleName = "Kritik Sıcaklık Tehlikesi",
                        AlarmType = "TEMP_CRITICAL",
                        Metric = "Temperature",
                        Operator = ComparisonOperator.GreaterThan,
                        Threshold = 90.0,
                        Severity = AlarmSeverity.Critical,
                        Enabled = true
                    },
                    new AlarmRule
                    {
                        Id = 3,
                        MachineId = 1,
                        RuleName = "Yüksek Basınç Uyarısı",
                        AlarmType = "PRESSURE_WARNING",
                        Metric = "Pressure",
                        Operator = ComparisonOperator.GreaterThan,
                        Threshold = 7.0,
                        Severity = AlarmSeverity.Warning,
                        Enabled = true
                    }
                );
                db.SaveChanges();
            }

            var mockAlarm = new TestAlarmService();
            var scopeFactory = serviceProvider.GetRequiredService<IServiceScopeFactory>();
            var digitalTwinService = new DigitalTwinService(mockAlarm, scopeFactory, NullLogger<DigitalTwinService>.Instance);

            return (digitalTwinService, serviceProvider);
        }

        [Fact]
        public async Task WhatIf_NominalValues_PredictsHighHealthAndLowRisk()
        {
            // Arrange
            var (service, provider) = CreateDigitalTwinService();
            var req = new SimulationRequestDto
            {
                MachineId = 1,
                TargetTemperature = 45.0,
                TargetPressure = 4.8,
                MachineSpeed = 100.0
            };

            // Act
            var result = await service.SimulateWhatIfScenarioAsync(req);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(100.0, result.SimulatedState.PredictedHealthScore);
            Assert.Equal("LOW", result.SimulatedState.AlarmRiskLevel);
            Assert.Empty(result.PredictedAlarms);
            Assert.True(result.SimulatedState.PredictedOeeScore > 80.0);
        }

        [Fact]
        public async Task WhatIf_OverheatingValues_PredictsDegradedHealthAndWarningAlarm()
        {
            // Arrange: 85°C (Warning eşiğini aşar, Critical 90°C'nin altındadır)
            var (service, provider) = CreateDigitalTwinService();
            var req = new SimulationRequestDto
            {
                MachineId = 1,
                TargetTemperature = 85.0,
                TargetPressure = 5.0,
                MachineSpeed = 100.0
            };

            // Act
            var result = await service.SimulateWhatIfScenarioAsync(req);

            // Assert
            Assert.NotNull(result);
            Assert.True(result.SimulatedState.PredictedHealthScore < 100.0, "Sağlık skoru düşmeli");
            Assert.NotEmpty(result.PredictedAlarms);
            Assert.Contains(result.PredictedAlarms, a => a.AlarmType == "TEMP_WARNING");
            Assert.DoesNotContain(result.PredictedAlarms, a => a.AlarmType == "TEMP_CRITICAL");
            Assert.True(result.SimulatedState.AlarmRiskLevel == "HIGH" || result.SimulatedState.AlarmRiskLevel == "CRITICAL");
            Assert.True(result.Comparison.HealthScoreDelta < 0, "Health delta negatif olmalı");
        }

        [Fact]
        public async Task WhatIf_ExtremeValues_PredictsCriticalRiskAndSignificantDowntime()
        {
            // Arrange: 95°C & 8.5 bar (Hem Temp Critical hem Pressure Warning)
            var (service, provider) = CreateDigitalTwinService();
            var req = new SimulationRequestDto
            {
                MachineId = 1,
                TargetTemperature = 95.0,
                TargetPressure = 8.5,
                MachineSpeed = 120.0
            };

            // Act
            var result = await service.SimulateWhatIfScenarioAsync(req);

            // Assert
            Assert.NotNull(result);
            Assert.Equal("CRITICAL", result.SimulatedState.AlarmRiskLevel);
            Assert.Contains(result.PredictedAlarms, a => a.Severity == AlarmSeverity.Critical);
            Assert.Contains(result.PredictedAlarms, a => a.AlarmType == "TEMP_CRITICAL");
            Assert.Contains(result.PredictedAlarms, a => a.AlarmType == "PRESSURE_WARNING");
            Assert.True(result.SimulatedState.PredictedAvailability <= 50.0, "Kritik durumda kullanılabilirlik düşmeli");
        }

        [Fact]
        public async Task WhatIf_SpeedAdjustment_CalculatesPerformanceImpact()
        {
            // Arrange
            var (service, provider) = CreateDigitalTwinService();
            var reqHighSpeed = new SimulationRequestDto { MachineId = 1, TargetTemperature = 45.0, TargetPressure = 4.8, MachineSpeed = 100.0 };
            var reqLowSpeed = new SimulationRequestDto { MachineId = 1, TargetTemperature = 45.0, TargetPressure = 4.8, MachineSpeed = 60.0 };

            // Act
            var resultHigh = await service.SimulateWhatIfScenarioAsync(reqHighSpeed);
            var resultLow = await service.SimulateWhatIfScenarioAsync(reqLowSpeed);

            // Assert
            Assert.True(resultHigh.SimulatedState.PredictedPerformance > resultLow.SimulatedState.PredictedPerformance);
            Assert.True(resultHigh.SimulatedState.PredictedOeeScore > resultLow.SimulatedState.PredictedOeeScore);
        }

        private class TestAlarmService : IAlarmService
        {
            public List<AlarmLog> ActiveAlarms { get; set; } = new List<AlarmLog>();

            public Task ProcessSensorReadingAsync(SensorData data, int machineId = 1, CancellationToken cancellationToken = default) => Task.CompletedTask;
            public Task ProcessPlcStatusAsync(PlcConnectionState state, int machineId = 1, CancellationToken cancellationToken = default) => Task.CompletedTask;
            public Task<IReadOnlyList<AlarmLog>> GetActiveAlarmsAsync(int? machineId = null, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<AlarmLog>>(ActiveAlarms);
            public Task<IReadOnlyList<AlarmLog>> GetAlarmHistoryAsync(int limit = 50, AlarmSeverity? severity = null, AlarmStatus? status = null, int? machineId = null, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<AlarmLog>>(ActiveAlarms);
            public Task<bool> AcknowledgeAlarmAsync(long alarmId, CancellationToken cancellationToken = default) => Task.FromResult(true);
            public Task<IReadOnlyList<AlarmRule>> GetRulesAsync(int? machineId = null, bool? enabledOnly = null, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<AlarmRule>>(new List<AlarmRule>());
            public Task<AlarmRule?> GetRuleByIdAsync(int id, CancellationToken cancellationToken = default) => Task.FromResult<AlarmRule?>(null);
            public Task<AlarmRule> CreateRuleAsync(AlarmRule rule, CancellationToken cancellationToken = default) => Task.FromResult(rule);
            public Task<bool> UpdateRuleAsync(AlarmRule rule, CancellationToken cancellationToken = default) => Task.FromResult(true);
            public Task<bool> DeleteRuleAsync(int id, CancellationToken cancellationToken = default) => Task.FromResult(true);
            public Task<bool> ToggleRuleAsync(int id, bool enabled, CancellationToken cancellationToken = default) => Task.FromResult(true);
            public Task EnsureDefaultRulesSeededAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
            public Task ReloadRulesCacheAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        }
    }
}
