using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IndustrialDataLogger.Data;
using IndustrialDataLogger.Enums;
using IndustrialDataLogger.Hubs;
using IndustrialDataLogger.Models;
using IndustrialDataLogger.Models.Entities;
using IndustrialDataLogger.Services;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace IndustrialDataLogger.Tests.IntegrationTests
{
    public class EndToEndDataPipelineTests
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly MockPlcService _mockPlc;
        private readonly PlcConnectionManager _connectionManager;
        private readonly IAlarmService _alarmService;
        private readonly IDigitalTwinService _digitalTwinService;
        private readonly IEventLogService _eventLogService;

        public EndToEndDataPipelineTests()
        {
            var dbName = "TestDb_" + Guid.NewGuid().ToString();
            var services = new ServiceCollection();

            services.AddDbContext<IndustrialDbContext>(options =>
                options.UseInMemoryDatabase(dbName));

            services.AddLogging(builder => builder.AddProvider(NullLoggerProvider.Instance));
            services.AddSignalR();

            services.AddSingleton<MockPlcService>();
            services.AddSingleton<IEventLogService, EventLogService>();
            services.AddSingleton<IAlarmService, AlarmService>();
            services.AddSingleton<IDigitalTwinService, DigitalTwinService>();

            _serviceProvider = services.BuildServiceProvider();

            _mockPlc = _serviceProvider.GetRequiredService<MockPlcService>();
            _connectionManager = new PlcConnectionManager(null!, _mockPlc, NullLogger<PlcConnectionManager>.Instance);
            _alarmService = _serviceProvider.GetRequiredService<IAlarmService>();
            _digitalTwinService = _serviceProvider.GetRequiredService<IDigitalTwinService>();
            _eventLogService = _serviceProvider.GetRequiredService<IEventLogService>();

            // Seed default rules and reload cache
            _alarmService.EnsureDefaultRulesSeededAsync().GetAwaiter().GetResult();
        }

        [Fact]
        public async Task E2E_FullDataPipeline_NormalScenario_ShouldPropagateCleanly()
        {
            // 1. Arrange: Normal senaryo ve bağlantı
            _mockPlc.SetScenario(SimulationScenario.Normal);
            await _connectionManager.ConnectAsync();
            int machineId = 1;

            // 2. Act: Telemetri oku ve boru hattından geçir
            var sensorData = _mockPlc.ReadSensorData();
            Assert.NotNull(sensorData);
            sensorData!.MachineId = machineId;

            // Kural motorundan geçir
            await _alarmService.ProcessSensorReadingAsync(sensorData, machineId, CancellationToken.None);

            // Digital Twin durumunu güncelle
            var twinState = await _digitalTwinService.UpdateStateAsync(
                sensorData, _connectionManager.CurrentState, machineId, CancellationToken.None);

            // 3. Assert
            Assert.True(twinState.IsPlcConnected);
            Assert.Equal(100.0, twinState.HealthScore);
            Assert.Equal(HealthGrade.Healthy, twinState.HealthGrade);
            Assert.Equal(MachineOperationalStatus.Running, twinState.OperationalStatus);
            Assert.Equal(0, twinState.ActiveAlarmCount);
        }

        [Fact]
        public async Task E2E_OverheatingEscalation_AndAlarmLifecycle_ShouldTriggerAcknowledgeAndResolve()
        {
            // 1. Arrange: Aşırı ısınma verisi
            int machineId = 1;
            await _connectionManager.ConnectAsync();

            var hotData = new SensorData
            {
                MachineId = machineId,
                Temperature = 95.0, // Kritik sıcaklık eşiği > 90°C
                Pressure = 5.0,
                MachineStatus = true,
                Timestamp = DateTime.UtcNow
            };

            // 2. Act: Sıcaklık tırmanışını kural motoruna ver
            await _alarmService.ProcessSensorReadingAsync(hotData, machineId, CancellationToken.None);

            // Aktif alarmları doğrula
            var activeAlarms = await _alarmService.GetActiveAlarmsAsync(machineId, CancellationToken.None);
            Assert.NotEmpty(activeAlarms);
            var firstAlarm = activeAlarms.First();
            Assert.Equal(AlarmStatus.Active, firstAlarm.Status);
            Assert.Equal("CRITICAL_TEMPERATURE", firstAlarm.AlarmType);

            // Digital Twin durumunu güncelle (Puanın düştüğünü doğrula)
            var twinState = await _digitalTwinService.UpdateStateAsync(
                hotData, _connectionManager.CurrentState, machineId, CancellationToken.None);
            Assert.True(twinState.HealthScore < 100.0);
            Assert.True(twinState.ActiveAlarmCount > 0);

            // 3. Alarm Onaylama (Acknowledge)
            bool ackSuccess = await _alarmService.AcknowledgeAlarmAsync(firstAlarm.Id, CancellationToken.None);
            Assert.True(ackSuccess);
            Assert.Equal(AlarmStatus.Acknowledged, firstAlarm.Status);

            // 4. Normal duruma dönüş ve otomatik çözülme (Auto-Resolve)
            var normalData = new SensorData
            {
                MachineId = machineId,
                Temperature = 45.0, // Normal nominal değer
                Pressure = 5.0,
                MachineStatus = true,
                Timestamp = DateTime.UtcNow
            };

            await _alarmService.ProcessSensorReadingAsync(normalData, machineId, CancellationToken.None);
            var remainingActiveAlarms = await _alarmService.GetActiveAlarmsAsync(machineId, CancellationToken.None);
            Assert.Empty(remainingActiveAlarms);
        }

        [Fact]
        public async Task E2E_PlcDisconnectionAndReconnection_ShouldManageStateAndAlarmsAccurately()
        {
            // 1. Arrange: Bağlı sistem
            _mockPlc.SetScenario(SimulationScenario.Normal);
            await _connectionManager.ConnectAsync();
            int machineId = 1;

            // 2. Act: PLC Bağlantısı kopuyor
            await _connectionManager.DisconnectAsync();
            Assert.Equal(PlcConnectionState.Disconnected, _connectionManager.CurrentState);

            // Alarm motoruna ve Digital Twin'e bağlantı kesintisini bildir
            await _alarmService.ProcessPlcStatusAsync(_connectionManager.CurrentState, machineId, CancellationToken.None);
            var twinStateOffline = await _digitalTwinService.UpdateStateAsync(
                null, _connectionManager.CurrentState, machineId, CancellationToken.None);

            // Assert: Offline durum ve PLC Alarmı
            Assert.False(twinStateOffline.IsPlcConnected);
            Assert.Equal(MachineOperationalStatus.Offline, twinStateOffline.OperationalStatus);
            Assert.Equal(0.0, twinStateOffline.HealthBreakdown.ConnectionScore);

            var connectionAlarms = await _alarmService.GetActiveAlarmsAsync(machineId, CancellationToken.None);
            Assert.Contains(connectionAlarms, a => a.AlarmType == "PLC_CONNECTION_LOST");

            // 3. Act: PLC Yeniden Bağlanıyor (Reconnection)
            await _connectionManager.ConnectAsync();
            Assert.Equal(PlcConnectionState.Connected, _connectionManager.CurrentState);

            // Bağlantı geri geldiğinde durum güncelle
            await _alarmService.ProcessPlcStatusAsync(_connectionManager.CurrentState, machineId, CancellationToken.None);
            var sensorData = _mockPlc.ReadSensorData();
            var twinStateRestored = await _digitalTwinService.UpdateStateAsync(
                sensorData, _connectionManager.CurrentState, machineId, CancellationToken.None);

            // Assert: Sistem normale döndü
            Assert.True(twinStateRestored.IsPlcConnected);
            Assert.Equal(MachineOperationalStatus.Running, twinStateRestored.OperationalStatus);
            var restoredAlarms = await _alarmService.GetActiveAlarmsAsync(machineId, CancellationToken.None);
            Assert.DoesNotContain(restoredAlarms, a => a.AlarmType == "PLC_CONNECTION_LOST");
        }
    }
}
