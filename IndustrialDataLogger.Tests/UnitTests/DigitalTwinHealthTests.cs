using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using IndustrialDataLogger.Enums;
using IndustrialDataLogger.Models;
using IndustrialDataLogger.Models.Entities;
using IndustrialDataLogger.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace IndustrialDataLogger.Tests.UnitTests
{
    public class DigitalTwinHealthTests
    {
        private readonly DigitalTwinService _digitalTwinService;

        public DigitalTwinHealthTests()
        {
            var serviceCollection = new ServiceCollection();
            var serviceProvider = serviceCollection.BuildServiceProvider();
            var scopeFactory = serviceProvider.GetRequiredService<IServiceScopeFactory>();

            var alarmService = new MockAlarmService();

            _digitalTwinService = new DigitalTwinService(
                alarmService,
                scopeFactory,
                NullLogger<DigitalTwinService>.Instance);
        }

        [Fact]
        public async Task UpdateStateAsync_WithOptimalValues_ShouldReturn100PercentHealthy()
        {
            // Arrange
            var sensorData = new SensorData
            {
                Temperature = 45.0,
                Pressure = 5.0,
                MachineStatus = true
            };

            // Act
            var result = await _digitalTwinService.UpdateStateAsync(sensorData, PlcConnectionState.Connected, CancellationToken.None);

            // Assert
            Assert.Equal(100.0, result.HealthScore);
            Assert.Equal(HealthGrade.Healthy, result.HealthGrade);
            Assert.Equal(MachineOperationalStatus.Running, result.OperationalStatus);
            Assert.Equal(25.0, result.HealthBreakdown.TemperatureScore);
            Assert.Equal(20.0, result.HealthBreakdown.PressureScore);
            Assert.Equal(20.0, result.HealthBreakdown.ConnectionScore);
            Assert.Equal(35.0, result.HealthBreakdown.AlarmScore);
        }

        [Theory]
        [InlineData(45.0, 5.0, PlcConnectionState.Connected, 0, 0, 100.0, HealthGrade.Healthy)]
        [InlineData(70.0, 5.0, PlcConnectionState.Connected, 0, 0, 95.0, HealthGrade.Healthy)]       // Temp 20p + Press 20p + Conn 20p + Alarm 35p = 95p
        [InlineData(80.0, 5.0, PlcConnectionState.Connected, 1, 0, 70.0, HealthGrade.Warning)]       // Temp 10p, Alarm 20p -> 10+20+20+20 = 70p
        [InlineData(80.0, 8.0, PlcConnectionState.Connected, 2, 0, 48.0, HealthGrade.Degraded)]      // Temp 10p, Press 8p, Conn 20p, Alarm 10p = 48p
        [InlineData(95.0, 9.5, PlcConnectionState.Disconnected, 0, 1, 0.0, HealthGrade.Critical)]    // Temp 0p, Press 0p, Conn 0p, Alarm 0p = 0p
        public void CalculateHealthScore_ShouldEvaluateAccuratelyAcrossAllGrades(
            double temp,
            double pressure,
            PlcConnectionState connState,
            int warningAlarms,
            int criticalAlarms,
            double expectedScore,
            HealthGrade expectedGrade)
        {
            // Act
            var (score, grade, breakdown) = _digitalTwinService.CalculateHealthScore(
                temp, pressure, connState, warningAlarms, criticalAlarms);

            // Assert
            Assert.Equal(expectedScore, score);
            Assert.Equal(expectedGrade, grade);
        }

        [Fact]
        public async Task UpdateStateAsync_WhenDisconnected_ShouldReturnDegradedOrCritical()
        {
            // Arrange
            var sensorData = new SensorData
            {
                Temperature = 45.0,
                Pressure = 5.0,
                MachineStatus = false
            };

            // Act
            var result = await _digitalTwinService.UpdateStateAsync(sensorData, PlcConnectionState.Disconnected, CancellationToken.None);

            // Assert
            Assert.Equal(MachineOperationalStatus.Offline, result.OperationalStatus);
            Assert.Equal(0.0, result.HealthBreakdown.ConnectionScore);
            Assert.True(result.HealthScore <= 80.0);
        }

        [Fact]
        public async Task UpdateStateAsync_WhenOverheating_ShouldReduceScore()
        {
            // Arrange
            var sensorData = new SensorData
            {
                Temperature = 92.0, // Critical (>85°C -> 0p)
                Pressure = 5.0,
                MachineStatus = true
            };

            // Act
            var result = await _digitalTwinService.UpdateStateAsync(sensorData, PlcConnectionState.Connected, CancellationToken.None);

            // Assert
            Assert.Equal(0.0, result.HealthBreakdown.TemperatureScore);
            Assert.True(result.HealthScore <= 75.0);
        }

        [Fact]
        public async Task UpdateStateAsync_WithSpecificMachineId_ShouldPopulateMachineIdCorrectly()
        {
            // Arrange
            var sensorData = new SensorData
            {
                MachineId = 2,
                Temperature = 40.0,
                Pressure = 4.0,
                MachineStatus = true
            };

            // Act
            var result = await _digitalTwinService.UpdateStateAsync(sensorData, PlcConnectionState.Connected, 2, CancellationToken.None);

            // Assert
            Assert.Equal(2, result.MachineId);
            Assert.Equal(100.0, result.HealthScore);
        }

        [Fact]
        public async Task GetPlantOverviewAsync_ShouldAggregateOperationalMetrics()
        {
            // Act
            var overview = await _digitalTwinService.GetPlantOverviewAsync(CancellationToken.None);

            // Assert
            Assert.NotNull(overview);
            Assert.NotEmpty(overview.Machines);
            Assert.True(overview.TotalMachines >= 1);
        }
    }

    public class MockAlarmService : IAlarmService
    {
        public Task ProcessSensorReadingAsync(SensorData data, int machineId = 1, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task ProcessPlcStatusAsync(PlcConnectionState state, int machineId = 1, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<AlarmLog>> GetActiveAlarmsAsync(int? machineId = null, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<AlarmLog>>(new List<AlarmLog>());
        }

        public Task<IReadOnlyList<AlarmLog>> GetAlarmHistoryAsync(int limit = 50, AlarmSeverity? severity = null, AlarmStatus? status = null, int? machineId = null, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<AlarmLog>>(new List<AlarmLog>());
        }

        public Task<bool> AcknowledgeAlarmAsync(long alarmId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(true);
        }

        public Task<IReadOnlyList<AlarmRule>> GetRulesAsync(int? machineId = null, bool? enabledOnly = null, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<AlarmRule>>(new List<AlarmRule>());
        }

        public Task<AlarmRule?> GetRuleByIdAsync(int id, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<AlarmRule?>(null);
        }

        public Task<AlarmRule> CreateRuleAsync(AlarmRule rule, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(rule);
        }

        public Task<bool> UpdateRuleAsync(AlarmRule rule, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(true);
        }

        public Task<bool> DeleteRuleAsync(int id, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(true);
        }

        public Task<bool> ToggleRuleAsync(int id, bool enabled, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(true);
        }

        public Task EnsureDefaultRulesSeededAsync(CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task ReloadRulesCacheAsync(CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }
}
