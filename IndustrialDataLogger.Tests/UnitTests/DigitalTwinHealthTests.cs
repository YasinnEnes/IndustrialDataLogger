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

            // Mock alarm service
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
                Temperature = 92.0, // Critical
                Pressure = 5.0,
                MachineStatus = true
            };

            // Act
            var result = await _digitalTwinService.UpdateStateAsync(sensorData, PlcConnectionState.Connected, CancellationToken.None);

            // Assert
            Assert.Equal(0.0, result.HealthBreakdown.TemperatureScore); // 0 out of 25
            Assert.True(result.HealthScore <= 75.0);
        }
    }

    public class MockAlarmService : IAlarmService
    {
        public Task ProcessSensorReadingAsync(SensorData data, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task ProcessPlcStatusAsync(PlcConnectionState state, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<AlarmLog>> GetActiveAlarmsAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<AlarmLog>>(new List<AlarmLog>());
        }

        public Task<IReadOnlyList<AlarmLog>> GetAlarmHistoryAsync(int limit = 50, AlarmSeverity? severity = null, AlarmStatus? status = null, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<AlarmLog>>(new List<AlarmLog>());
        }

        public Task<bool> AcknowledgeAlarmAsync(long alarmId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(true);
        }
    }
}
