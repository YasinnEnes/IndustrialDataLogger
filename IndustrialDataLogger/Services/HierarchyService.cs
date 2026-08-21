using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IndustrialDataLogger.Data;
using IndustrialDataLogger.Enums;
using IndustrialDataLogger.Models.DTOs;
using IndustrialDataLogger.Models.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace IndustrialDataLogger.Services
{
    public class HierarchyService : IHierarchyService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IDigitalTwinService _digitalTwinService;
        private readonly ILogger<HierarchyService> _logger;

        public HierarchyService(
            IServiceScopeFactory scopeFactory,
            IDigitalTwinService digitalTwinService,
            ILogger<HierarchyService> logger)
        {
            _scopeFactory = scopeFactory;
            _digitalTwinService = digitalTwinService;
            _logger = logger;
        }

        public async Task<List<AssetTreeNodeDto>> GetAssetTreeAsync(CancellationToken cancellationToken = default)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IndustrialDbContext>();

            var factories = await db.Factories
                .AsNoTracking()
                .Include(f => f.ProductionLines)
                    .ThenInclude(p => p.Machines)
                .OrderBy(f => f.Id)
                .ToListAsync(cancellationToken);

            // Aktif alarmları çek
            var activeAlarms = await db.Alarms
                .AsNoTracking()
                .Where(a => a.Status == AlarmStatus.Active)
                .GroupBy(a => a.MachineId)
                .Select(g => new { MachineId = g.Key, Count = g.Count(), HasCritical = g.Any(x => x.Severity == AlarmSeverity.Critical) })
                .ToDictionaryAsync(x => x.MachineId, cancellationToken);

            var result = new List<AssetTreeNodeDto>();

            foreach (var factory in factories)
            {
                var factoryNode = new AssetTreeNodeDto
                {
                    Id = $"f_{factory.Id}",
                    EntityId = factory.Id,
                    Name = factory.Name,
                    Type = "factory",
                    ExtraInfo = factory.Location,
                    Status = factory.IsActive ? "online" : "offline"
                };

                int factoryAlarmCount = 0;
                var factoryHealthScores = new List<double>();

                foreach (var line in factory.ProductionLines.OrderBy(p => p.Id))
                {
                    var lineNode = new AssetTreeNodeDto
                    {
                        Id = $"l_{line.Id}",
                        EntityId = line.Id,
                        Name = line.Name,
                        Type = "line",
                        Code = line.LineCode,
                        ExtraInfo = line.Description,
                        Status = line.IsActive ? "online" : "offline"
                    };

                    int lineAlarmCount = 0;
                    var lineHealthScores = new List<double>();

                    foreach (var machine in line.Machines.OrderBy(m => m.Id))
                    {
                        activeAlarms.TryGetValue(machine.Id, out var alarmInfo);
                        int mAlarms = alarmInfo?.Count ?? 0;
                        bool hasCritical = alarmInfo?.HasCritical ?? false;

                        // Makine sağlık skoru
                        double healthScore = 95.0;
                        try
                        {
                            var dtState = await _digitalTwinService.GetStateAsync(machine.Id, cancellationToken);
                            if (dtState != null)
                            {
                                healthScore = dtState.HealthScore;
                            }
                        }
                        catch
                        {
                            healthScore = 90.0;
                        }

                        string mStatus = "online";
                        if (!machine.IsActive) mStatus = "offline";
                        else if (hasCritical || healthScore < 50) mStatus = "critical";
                        else if (mAlarms > 0 || healthScore < 75) mStatus = "warning";

                        var machineNode = new AssetTreeNodeDto
                        {
                            Id = $"m_{machine.Id}",
                            EntityId = machine.Id,
                            Name = machine.Name,
                            Type = "machine",
                            Code = machine.MachineCode,
                            Status = mStatus,
                            HealthScore = healthScore,
                            ActiveAlarmCount = mAlarms,
                            ExtraInfo = $"{machine.Type} ({machine.PlcIp})"
                        };

                        lineNode.Children.Add(machineNode);
                        lineAlarmCount += mAlarms;
                        lineHealthScores.Add(healthScore);
                    }

                    lineNode.ActiveAlarmCount = lineAlarmCount;
                    if (lineHealthScores.Any())
                    {
                        lineNode.HealthScore = Math.Round(lineHealthScores.Average(), 1);
                        if (lineNode.HealthScore < 50 || lineNode.Children.Any(c => c.Status == "critical")) lineNode.Status = "critical";
                        else if (lineNode.HealthScore < 75 || lineAlarmCount > 0) lineNode.Status = "warning";
                    }

                    factoryNode.Children.Add(lineNode);
                    factoryAlarmCount += lineAlarmCount;
                    factoryHealthScores.AddRange(lineHealthScores);
                }

                factoryNode.ActiveAlarmCount = factoryAlarmCount;
                if (factoryHealthScores.Any())
                {
                    factoryNode.HealthScore = Math.Round(factoryHealthScores.Average(), 1);
                    if (factoryNode.HealthScore < 50) factoryNode.Status = "critical";
                    else if (factoryNode.HealthScore < 75 || factoryAlarmCount > 0) factoryNode.Status = "warning";
                }

                result.Add(factoryNode);
            }

            return result;
        }

        public async Task<FactoryOverviewDto> GetFactoryOverviewAsync(int? factoryId = null, CancellationToken cancellationToken = default)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IndustrialDbContext>();

            var factoriesQuery = db.Factories.AsNoTracking().Include(f => f.ProductionLines).ThenInclude(p => p.Machines);
            var factories = factoryId.HasValue
                ? await factoriesQuery.Where(f => f.Id == factoryId.Value).ToListAsync(cancellationToken)
                : await factoriesQuery.ToListAsync(cancellationToken);

            var activeAlarms = await db.Alarms
                .AsNoTracking()
                .Where(a => a.Status == AlarmStatus.Active)
                .ToListAsync(cancellationToken);

            var totalFactories = factories.Count;
            var allLines = factories.SelectMany(f => f.ProductionLines).ToList();
            var allMachines = allLines.SelectMany(l => l.Machines).ToList();

            int onlineCount = allMachines.Count(m => m.IsActive);
            int offlineCount = allMachines.Count - onlineCount;
            double onlineRatio = allMachines.Count > 0 ? Math.Round((double)onlineCount / allMachines.Count * 100.0, 1) : 100.0;

            var allHealthScores = new List<double>();
            var lineSummaries = new List<ProductionLineSummaryDto>();

            foreach (var line in allLines)
            {
                var lineMachines = line.Machines.ToList();
                var lineHealthList = new List<double>();

                foreach (var m in lineMachines)
                {
                    try
                    {
                        var dtState = await _digitalTwinService.GetStateAsync(m.Id, cancellationToken);
                        if (dtState != null) lineHealthList.Add(dtState.HealthScore);
                    }
                    catch
                    {
                        lineHealthList.Add(90.0);
                    }
                }

                double avgLineHealth = lineHealthList.Any() ? Math.Round(lineHealthList.Average(), 1) : 95.0;
                allHealthScores.AddRange(lineHealthList);

                var lineAlarms = activeAlarms.Count(a => lineMachines.Any(m => m.Id == a.MachineId));

                lineSummaries.Add(new ProductionLineSummaryDto
                {
                    LineId = line.Id,
                    FactoryId = line.FactoryId,
                    FactoryName = line.Factory?.Name ?? "Ana Kampüs",
                    LineName = line.Name,
                    LineCode = line.LineCode,
                    Description = line.Description,
                    MachineCount = lineMachines.Count,
                    OnlineMachineCount = lineMachines.Count(m => m.IsActive),
                    AverageHealthScore = avgLineHealth,
                    ActiveAlarmCount = lineAlarms,
                    Status = avgLineHealth >= 80 ? "Normal" : (avgLineHealth >= 60 ? "Uyarı" : "Kritik")
                });
            }

            double overallHealth = allHealthScores.Any() ? Math.Round(allHealthScores.Average(), 1) : 92.5;
            string healthStatus = overallHealth >= 85 ? "Mükemmel" : (overallHealth >= 70 ? "İyi" : (overallHealth >= 50 ? "Dikkat" : "Kritik"));

            return new FactoryOverviewDto
            {
                TotalFactories = totalFactories,
                TotalLines = allLines.Count,
                TotalMachines = allMachines.Count,
                OnlineMachines = onlineCount,
                OfflineMachines = offlineCount,
                OnlineRatioPercentage = onlineRatio,
                TotalActiveAlarms = activeAlarms.Count,
                CriticalAlarmsCount = activeAlarms.Count(a => a.Severity == AlarmSeverity.Critical),
                WarningAlarmsCount = activeAlarms.Count(a => a.Severity == AlarmSeverity.Warning),
                OverallFactoryHealthScore = overallHealth,
                OverallHealthStatus = healthStatus,
                Timestamp = DateTime.UtcNow,
                LineSummaries = lineSummaries
            };
        }

        public async Task<ProductionLineSummaryDto?> GetProductionLineSummaryAsync(int lineId, CancellationToken cancellationToken = default)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IndustrialDbContext>();

            var line = await db.ProductionLines
                .AsNoTracking()
                .Include(p => p.Factory)
                .Include(p => p.Machines)
                .FirstOrDefaultAsync(p => p.Id == lineId, cancellationToken);

            if (line == null) return null;

            var activeAlarms = await db.Alarms
                .AsNoTracking()
                .Where(a => a.Status == AlarmStatus.Active && line.Machines.Select(m => m.Id).Contains(a.MachineId))
                .CountAsync(cancellationToken);

            var lineHealthList = new List<double>();
            foreach (var m in line.Machines)
            {
                try
                {
                    var dtState = await _digitalTwinService.GetStateAsync(m.Id, cancellationToken);
                    if (dtState != null) lineHealthList.Add(dtState.HealthScore);
                }
                catch
                {
                    lineHealthList.Add(90.0);
                }
            }

            double avgHealth = lineHealthList.Any() ? Math.Round(lineHealthList.Average(), 1) : 95.0;

            return new ProductionLineSummaryDto
            {
                LineId = line.Id,
                FactoryId = line.FactoryId,
                FactoryName = line.Factory?.Name ?? "Ana Tesis",
                LineName = line.Name,
                LineCode = line.LineCode,
                Description = line.Description,
                MachineCount = line.Machines.Count,
                OnlineMachineCount = line.Machines.Count(m => m.IsActive),
                AverageHealthScore = avgHealth,
                ActiveAlarmCount = activeAlarms,
                Status = avgHealth >= 80 ? "Normal" : (avgHealth >= 60 ? "Uyarı" : "Kritik")
            };
        }
    }
}
