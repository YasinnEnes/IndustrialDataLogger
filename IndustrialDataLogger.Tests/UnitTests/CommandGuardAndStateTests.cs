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
    public class CommandGuardAndStateTests
    {
        [Fact]
        public async Task CommandGuard_WhenPlcDisconnected_WriteDataAsyncReturnsFalse()
        {
            // Arrange
            var mockPlc = new MockPlcService();
            mockPlc.Disconnect();

            // Act
            var result = await mockPlc.WriteDataAsync("DB1.DBD0", 65.5f);

            // Assert
            Assert.False(result, "PLC çevrimdışıyken komut yazma işlemi reddedilmelidir (Command Guard).");
        }

        [Fact]
        public async Task CommandGuard_WhenPlcFailureScenario_WriteDataAsyncReturnsFalse()
        {
            // Arrange
            var mockPlc = new MockPlcService();
            mockPlc.SetScenario(SimulationScenario.PlcFailure);

            // Act
            var result = await mockPlc.WriteDataAsync("DB1.DBD4", 5.2f);

            // Assert
            Assert.False(result, "PLC Failure senaryosunda komut yazma işlemi engellenmelidir.");
            Assert.False(mockPlc.IsConnected);
        }

        [Fact]
        public async Task CommandGuard_WhenPlcDisconnectScenario_WriteDataAsyncReturnsFalse()
        {
            // Arrange
            var mockPlc = new MockPlcService();
            mockPlc.SetScenario(SimulationScenario.PlcDisconnect);

            // Act
            var result = await mockPlc.WriteDataAsync("DB1.DBX8.0", true);

            // Assert
            Assert.False(result, "PLC Disconnect senaryosunda yazma isteği başarısız dönmelidir.");
        }

        [Fact]
        public void ScenarioSwitch_FromOverheatingToNormal_ShouldResetTelemetryStateImmediately()
        {
            // Arrange
            var mockPlc = new MockPlcService();
            mockPlc.SetScenario(SimulationScenario.Overheating);
            mockPlc.ReadSensorData();
            mockPlc.ReadSensorData(); // Sıcaklık 80°C+ üzerine tırmandı

            // Act: Normal senaryoya geçiş
            mockPlc.SetScenario(SimulationScenario.Normal);
            var resetData = mockPlc.ReadSensorData();

            // Assert: Sıcaklık anında bayat 95°C'den nominal aralığa sıfırlanmalıdır
            Assert.NotNull(resetData);
            Assert.True(resetData!.Temperature <= 65.0, $"Senaryo normale döndüğünde sıcaklık düşmeli, okunan: {resetData.Temperature}°C");
        }

        [Fact]
        public void ScenarioSwitch_FromHighPressureToNormal_ShouldResetPressureImmediately()
        {
            // Arrange
            var mockPlc = new MockPlcService();
            mockPlc.SetScenario(SimulationScenario.HighPressure);
            mockPlc.ReadSensorData();
            mockPlc.ReadSensorData(); // Basınç 8.0 bar+ üzerine çıktı

            // Act: Normal senaryoya geçiş
            mockPlc.SetScenario(SimulationScenario.Normal);
            var resetData = mockPlc.ReadSensorData();

            // Assert: Basınç anında nominal aralığa sıfırlanmalıdır
            Assert.NotNull(resetData);
            Assert.True(resetData!.Pressure <= 7.0, $"Senaryo normale döndüğünde basınç nominal olmalı, okunan: {resetData.Pressure} bar");
        }

        [Fact]
        public async Task HealthScore_WhenCriticalAlarmTriggered_DegradesScoreImmediately()
        {
            // Arrange
            var serviceCollection = new ServiceCollection();
            var serviceProvider = serviceCollection.BuildServiceProvider();
            var scopeFactory = serviceProvider.GetRequiredService<IServiceScopeFactory>();

            var mockAlarm = new TestAlarmService();
            mockAlarm.ActiveAlarms.Add(new AlarmLog
            {
                MachineId = 1,
                AlarmType = "OVERHEATING",
                Severity = AlarmSeverity.Critical,
                Status = AlarmStatus.Active,
                Message = "Aşırı sıcaklık algılandı"
            });

            var dtService = new DigitalTwinService(mockAlarm, scopeFactory, NullLogger<DigitalTwinService>.Instance);
            var sensorData = new SensorData { Temperature = 92.0, Pressure = 5.0, MachineStatus = true };

            // Act
            var state = await dtService.UpdateStateAsync(sensorData, PlcConnectionState.Connected, CancellationToken.None);

            // Assert: Kritik alarm ve yüksek sıcaklık varken sağlık puanı düşük olmalıdır (<= 60)
            Assert.True(state.HealthScore <= 60.0, $"Kritik alarm varken sağlık puanı düşük olmalı, hesaplanan: {state.HealthScore}");
            Assert.True(state.HealthGrade == HealthGrade.Degraded || state.HealthGrade == HealthGrade.Critical);
        }

        [Fact]
        public async Task HealthScore_WhenAlarmsResolved_RecoversScoreBackToHealthy()
        {
            // Arrange
            var serviceCollection = new ServiceCollection();
            var serviceProvider = serviceCollection.BuildServiceProvider();
            var scopeFactory = serviceProvider.GetRequiredService<IServiceScopeFactory>();

            var mockAlarm = new TestAlarmService(); // 0 Alarms

            var dtService = new DigitalTwinService(mockAlarm, scopeFactory, NullLogger<DigitalTwinService>.Instance);
            var normalData = new SensorData { Temperature = 42.0, Pressure = 5.0, MachineStatus = true };

            // Act
            var state = await dtService.UpdateStateAsync(normalData, PlcConnectionState.Connected, CancellationToken.None);

            // Assert: Alarmlar temizlendiğinde sağlık puanı yüksek (100) olmalıdır
            Assert.Equal(100.0, state.HealthScore);
            Assert.Equal(HealthGrade.Healthy, state.HealthGrade);
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
