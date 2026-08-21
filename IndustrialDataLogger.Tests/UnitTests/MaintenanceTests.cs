using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IndustrialDataLogger.Data;
using IndustrialDataLogger.Enums;
using IndustrialDataLogger.Hubs;
using IndustrialDataLogger.Models.DTOs;
using IndustrialDataLogger.Models.Entities;
using IndustrialDataLogger.Services;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace IndustrialDataLogger.Tests.UnitTests
{
    public class MaintenanceTests
    {
        private (MaintenanceService service, ServiceProvider provider) CreateMaintenanceService()
        {
            var services = new ServiceCollection();
            var dbName = "MaintenanceTestDb_" + Guid.NewGuid();
            services.AddDbContext<IndustrialDbContext>(opt => opt.UseInMemoryDatabase(dbName));

            services.AddLogging();
            var serviceProvider = services.BuildServiceProvider();

            // Seed machine
            using (var scope = serviceProvider.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<IndustrialDbContext>();
                db.Machines.Add(new Machine
                {
                    Id = 1,
                    MachineCode = "PLC-S7-1200-UNIT-01",
                    Name = "Siemens S7-1200 Akıllı Ünite",
                    Type = "CNC",
                    PlcIp = "192.168.0.1",
                    IsActive = true
                });
                db.SaveChanges();
            }

            var scopeFactory = serviceProvider.GetRequiredService<IServiceScopeFactory>();
            var mockHubContext = new FakeSensorHubContext();
            var mockEventService = new FakeEventLogService();

            var service = new MaintenanceService(
                scopeFactory,
                mockHubContext,
                mockEventService,
                NullLogger<MaintenanceService>.Instance
            );

            return (service, serviceProvider);
        }

        [Fact]
        public async Task CreateTaskAsync_ManualCreation_StoresTaskAndCalculatesDto()
        {
            // Arrange
            var (service, _) = CreateMaintenanceService();
            var dto = new CreateMaintenanceTaskDto
            {
                MachineId = 1,
                Component = "Rulman ve Mil Yatağı",
                Reason = "Titreşim artışı ve periyodik gresleme talebi",
                Priority = MaintenancePriority.Medium,
                AssignedTo = "Usta Ahmet"
            };

            // Act
            var result = await service.CreateTaskAsync(dto);

            // Assert
            Assert.NotNull(result);
            Assert.True(result.Id > 0);
            Assert.Equal("Rulman ve Mil Yatağı", result.Component);
            Assert.Equal(MaintenanceStatus.Open, result.Status);
            Assert.Equal("Usta Ahmet", result.AssignedTo);
            Assert.Null(result.ResolvedAt);
        }

        [Fact]
        public async Task AutoCreateTaskFromAlarmAsync_CriticalAlarm_AutomaticallyGeneratesWorkOrder()
        {
            // Arrange
            var (service, _) = CreateMaintenanceService();
            var criticalAlarm = new AlarmLog
            {
                Id = 101,
                MachineId = 1,
                AlarmType = "TEMP_CRITICAL",
                Severity = AlarmSeverity.Critical,
                Status = AlarmStatus.Active,
                Message = "Sıcaklık 95.0°C Kritik Eşiği Aştı!",
                TriggeredValue = 95.0,
                ThresholdValue = 90.0
            };

            // Act
            var result = await service.AutoCreateTaskFromAlarmAsync(criticalAlarm);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(1, result.MachineId);
            Assert.Contains("Soğutma Sistemi", result.Component);
            Assert.Equal(MaintenancePriority.Critical, result.Priority);
            Assert.Equal(MaintenanceStatus.Open, result.Status);
            Assert.Equal(101, result.AlarmLogId);
        }

        [Fact]
        public async Task AutoCreateTaskFromAlarmAsync_DuplicateAlarmWithinCooldown_DeduplicatesAndDoesNotCreateSpam()
        {
            // Arrange: Unique machine to bypass static cache interference from other tests
            var (service, provider) = CreateMaintenanceService();
            using (var scope = provider.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<IndustrialDbContext>();
                db.Machines.Add(new Machine { Id = 99, MachineCode = "M-99", Name = "Özel Makine", Type = "CNC", PlcIp = "192.168.0.99" });
                db.SaveChanges();
            }

            var criticalAlarm = new AlarmLog
            {
                Id = 201,
                MachineId = 99,
                AlarmType = "PRESSURE_CRITICAL",
                Severity = AlarmSeverity.Critical,
                Message = "Pik Basınç Aşımı 11.2 bar"
            };

            // Act: 1. tetikleme -> İş emri açılır
            var firstResult = await service.AutoCreateTaskFromAlarmAsync(criticalAlarm);
            // Act: 2. tetikleme -> Cooldown devrede olduğu için null dönmeli (mükerrer kayıt engellenmeli)
            var secondResult = await service.AutoCreateTaskFromAlarmAsync(criticalAlarm);

            // Assert
            Assert.NotNull(firstResult);
            Assert.Null(secondResult);

            var tasks = await service.GetTasksAsync(machineId: 99);
            Assert.Single(tasks); // Yalnızca 1 iş emri bulunmalı
        }

        [Fact]
        public async Task UpdateTaskStatusAsync_TransitionToResolved_SetsResolvedAtAndNotes()
        {
            // Arrange
            var (service, _) = CreateMaintenanceService();
            var created = await service.CreateTaskAsync(new CreateMaintenanceTaskDto
            {
                MachineId = 1,
                Component = "Hidrolik Valf",
                Reason = "Sızdırmazlık teftişi",
                Priority = MaintenancePriority.High
            });

            // Act 1: İncelemeye al (InProgress)
            var inProgress = await service.UpdateTaskStatusAsync(created.Id, new UpdateMaintenanceTaskStatusDto
            {
                Status = MaintenanceStatus.InProgress,
                AssignedTo = "Vardiya Teknisyeni Can"
            });

            // Act 2: Çözüldü olarak kapat (Resolved)
            var resolved = await service.UpdateTaskStatusAsync(created.Id, new UpdateMaintenanceTaskStatusDto
            {
                Status = MaintenanceStatus.Resolved,
                ResolutionNotes = "O-ring contası değiştirildi, basınç testi 6.0 barda başarıyla tamamlandı."
            });

            // Assert
            Assert.NotNull(inProgress);
            Assert.Equal(MaintenanceStatus.InProgress, inProgress.Status);
            Assert.Equal("Vardiya Teknisyeni Can", inProgress.AssignedTo);
            Assert.Null(inProgress.ResolvedAt);

            Assert.NotNull(resolved);
            Assert.Equal(MaintenanceStatus.Resolved, resolved.Status);
            Assert.NotNull(resolved.ResolvedAt);
            Assert.Contains("O-ring contası değiştirildi", resolved.ResolutionNotes);
            Assert.True(resolved.ResolutionDurationMinutes.HasValue);
        }

        [Fact]
        public async Task GetStatsAsync_CalculatesKPIAndCountsAccurately()
        {
            // Arrange
            var (service, _) = CreateMaintenanceService();
            await service.CreateTaskAsync(new CreateMaintenanceTaskDto { MachineId = 1, Component = "C1", Priority = MaintenancePriority.Critical });
            await service.CreateTaskAsync(new CreateMaintenanceTaskDto { MachineId = 1, Component = "C2", Priority = MaintenancePriority.Low });
            
            var task3 = await service.CreateTaskAsync(new CreateMaintenanceTaskDto { MachineId = 1, Component = "C3", Priority = MaintenancePriority.Medium });
            await service.UpdateTaskStatusAsync(task3.Id, new UpdateMaintenanceTaskStatusDto { Status = MaintenanceStatus.Resolved, ResolutionNotes = "Giderildi" });

            // Act
            var stats = await service.GetStatsAsync(1);

            // Assert
            Assert.NotNull(stats);
            Assert.Equal(3, stats.TotalTasks);
            Assert.Equal(2, stats.OpenCount);
            Assert.Equal(1, stats.ResolvedCount);
            Assert.Equal(1, stats.CriticalPriorityCount);
        }

        private class FakeSensorHubContext : IHubContext<MonitoringHub>
        {
            public IHubClients Clients { get; } = new FakeHubClients();
            public IGroupManager Groups { get; } = new FakeGroupManager();
        }

        private class FakeHubClients : IHubClients
        {
            public IClientProxy All { get; } = new FakeClientProxy();
            public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => new FakeClientProxy();
            public IClientProxy Client(string connectionId) => new FakeClientProxy();
            public IClientProxy Clients(IReadOnlyList<string> connectionIds) => new FakeClientProxy();
            public IClientProxy Group(string groupName) => new FakeClientProxy();
            public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => new FakeClientProxy();
            public IClientProxy Groups(IReadOnlyList<string> groupNames) => new FakeClientProxy();
            public IClientProxy User(string userId) => new FakeClientProxy();
            public IClientProxy Users(IReadOnlyList<string> userIds) => new FakeClientProxy();
        }

        private class FakeClientProxy : IClientProxy
        {
            public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default) => Task.CompletedTask;
        }

        private class FakeGroupManager : IGroupManager
        {
            public Task AddToGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default) => Task.CompletedTask;
            public Task RemoveFromGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default) => Task.CompletedTask;
        }

        private class FakeEventLogService : IEventLogService
        {
            public Task LogEventAsync(string eventType, string description, AlarmSeverity severity = AlarmSeverity.Info, string source = "System", CancellationToken cancellationToken = default) => Task.CompletedTask;
            public Task LogEventAsync(string eventType, string description, AlarmSeverity severity, string source, int? machineId, CancellationToken cancellationToken = default) => Task.CompletedTask;
            public Task<IReadOnlyList<SystemEventLog>> GetRecentEventsAsync(int limit = 50, string? eventType = null, AlarmSeverity? severity = null, int? machineId = null, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<SystemEventLog>>(new List<SystemEventLog>());
            public Task<int> CleanupOldEventsAsync(int retentionDays = 30, CancellationToken cancellationToken = default) => Task.FromResult(0);
        }
    }
}
