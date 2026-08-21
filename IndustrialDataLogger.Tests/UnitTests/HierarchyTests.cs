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
using IndustrialDataLogger.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace IndustrialDataLogger.Tests.UnitTests
{
    public class HierarchyTests
    {
        private (HierarchyService service, ServiceProvider provider) CreateHierarchyService()
        {
            var services = new ServiceCollection();
            var dbName = "HierarchyTestDb_" + Guid.NewGuid();
            services.AddDbContext<IndustrialDbContext>(opt => opt.UseInMemoryDatabase(dbName));

            services.AddLogging();
            var serviceProvider = services.BuildServiceProvider();

            // Seed Factories, Lines and Machines
            using (var scope = serviceProvider.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<IndustrialDbContext>();

                db.Factories.AddRange(
                    new Factory { Id = 1, Name = "Gebze Akıllı Üretim Kampüsü", Location = "Kocaeli / Gebze OSB", IsActive = true },
                    new Factory { Id = 2, Name = "İzmir Ege Entegre Tesisi", Location = "İzmir / Çiğli AOSB", IsActive = true }
                );

                db.ProductionLines.AddRange(
                    new ProductionLine { Id = 1, FactoryId = 1, Name = "Montaj & İşleme Hattı A", LineCode = "LINE-A", IsActive = true },
                    new ProductionLine { Id = 2, FactoryId = 1, Name = "Robotik Hücre Hattı B", LineCode = "LINE-B", IsActive = true },
                    new ProductionLine { Id = 3, FactoryId = 2, Name = "Paketleme & Lojistik Hattı C", LineCode = "LINE-C", IsActive = true }
                );

                db.Machines.AddRange(
                    new Machine { Id = 1, ProductionLineId = 1, MachineCode = "M1", Name = "Siemens S7-1200", Type = "InjectionMolding", PlcIp = "192.168.0.1", IsActive = true },
                    new Machine { Id = 2, ProductionLineId = 2, MachineCode = "M2", Name = "Kuka Robot", Type = "RoboticCell", PlcIp = "192.168.0.2", IsActive = true },
                    new Machine { Id = 3, ProductionLineId = 3, MachineCode = "M3", Name = "Paketleme Konveyörü", Type = "Packaging", PlcIp = "192.168.0.3", IsActive = false }
                );

                // Add an active alarm for Machine 1
                db.Alarms.Add(new AlarmLog
                {
                    Id = 1,
                    MachineId = 1,
                    AlarmType = "TEMP_HIGH",
                    Severity = AlarmSeverity.Warning,
                    Status = AlarmStatus.Active,
                    Message = "Sıcaklık Yüksek"
                });

                db.SaveChanges();
            }

            var scopeFactory = serviceProvider.GetRequiredService<IServiceScopeFactory>();
            var mockDigitalTwinService = new FakeDigitalTwinService();

            var service = new HierarchyService(
                scopeFactory,
                mockDigitalTwinService,
                NullLogger<HierarchyService>.Instance
            );

            return (service, serviceProvider);
        }

        [Fact]
        public async Task GetAssetTreeAsync_ReturnsCompleteHierarchyWithStatusAndBadges()
        {
            // Arrange
            var (service, _) = CreateHierarchyService();

            // Act
            var tree = await service.GetAssetTreeAsync();

            // Assert
            Assert.NotNull(tree);
            Assert.Equal(2, tree.Count); // 2 Factories

            var factory1 = tree.FirstOrDefault(f => f.EntityId == 1);
            Assert.NotNull(factory1);
            Assert.Equal("Gebze Akıllı Üretim Kampüsü", factory1.Name);
            Assert.Equal(2, factory1.Children.Count); // 2 Production Lines in Factory 1

            var line1 = factory1.Children.FirstOrDefault(l => l.EntityId == 1);
            Assert.NotNull(line1);
            Assert.Equal("Montaj & İşleme Hattı A", line1.Name);
            Assert.Single(line1.Children); // Machine 1 in Line 1

            var machine1 = line1.Children.First();
            Assert.Equal("Siemens S7-1200", machine1.Name);
            Assert.Equal(1, machine1.ActiveAlarmCount);
            Assert.Equal("warning", machine1.Status); // Has warning alarm
        }

        [Fact]
        public async Task GetFactoryOverviewAsync_CalculatesOverallHealthAndOnlineRatios()
        {
            // Arrange
            var (service, _) = CreateHierarchyService();

            // Act
            var overview = await service.GetFactoryOverviewAsync();

            // Assert
            Assert.NotNull(overview);
            Assert.Equal(2, overview.TotalFactories);
            Assert.Equal(3, overview.TotalLines);
            Assert.Equal(3, overview.TotalMachines);
            Assert.Equal(2, overview.OnlineMachines);
            Assert.Equal(1, overview.OfflineMachines);
            Assert.Equal(66.7, overview.OnlineRatioPercentage); // 2/3 = 66.7%
            Assert.Equal(1, overview.TotalActiveAlarms);
            Assert.True(overview.OverallFactoryHealthScore > 0);
            Assert.Equal(3, overview.LineSummaries.Count);
        }

        [Fact]
        public async Task GetProductionLineSummaryAsync_SpecificLine_ReturnsAccurateMetrics()
        {
            // Arrange
            var (service, _) = CreateHierarchyService();

            // Act
            var summary = await service.GetProductionLineSummaryAsync(1);

            // Assert
            Assert.NotNull(summary);
            Assert.Equal(1, summary.LineId);
            Assert.Equal("Montaj & İşleme Hattı A", summary.LineName);
            Assert.Equal("LINE-A", summary.LineCode);
            Assert.Equal(1, summary.MachineCount);
            Assert.Equal(1, summary.OnlineMachineCount);
            Assert.Equal(1, summary.ActiveAlarmCount);
        }

        [Fact]
        public async Task GetProductionLineSummaryAsync_NotFound_ReturnsNull()
        {
            // Arrange
            var (service, _) = CreateHierarchyService();

            // Act
            var summary = await service.GetProductionLineSummaryAsync(999);

            // Assert
            Assert.Null(summary);
        }

        private class FakeDigitalTwinService : IDigitalTwinService
        {
            public Task<DigitalTwinStateDto> GetStateAsync(CancellationToken cancellationToken = default)
            {
                return GetStateAsync(1, cancellationToken);
            }

            public Task<DigitalTwinStateDto> GetStateAsync(int machineId, CancellationToken cancellationToken = default)
            {
                var dto = new DigitalTwinStateDto
                {
                    MachineId = machineId,
                    HealthScore = machineId == 1 ? 88.0 : (machineId == 2 ? 94.0 : 70.0),
                    HealthGrade = HealthGrade.Healthy
                };
                return Task.FromResult(dto);
            }

            public Task<DigitalTwinStateDto> UpdateStateAsync(SensorData? sensorData, PlcConnectionState connectionState, CancellationToken cancellationToken = default) => GetStateAsync(1, cancellationToken);
            public Task<DigitalTwinStateDto> UpdateStateAsync(SensorData? sensorData, PlcConnectionState connectionState, int machineId, CancellationToken cancellationToken = default) => GetStateAsync(machineId, cancellationToken);
            public Task<PlantOverviewDto> GetPlantOverviewAsync(CancellationToken cancellationToken = default) => Task.FromResult(new PlantOverviewDto());
            public Task<SimulationResultDto> SimulateWhatIfScenarioAsync(SimulationRequestDto request, CancellationToken cancellationToken = default) => Task.FromResult(new SimulationResultDto());
            public (double score, HealthGrade grade, HealthScoreBreakdownDto breakdown) CalculateHealthScore(double temperature, double pressure, PlcConnectionState connectionState, int warningAlarmCount, int criticalAlarmCount) => (90.0, HealthGrade.Healthy, new HealthScoreBreakdownDto());
            public MachineOperationalStatus DetermineOperationalStatus(bool isConnected, PlcConnectionState connectionState, bool machineStatus, short errorCode, int criticalAlarmCount) => MachineOperationalStatus.Running;
        }
    }
}
