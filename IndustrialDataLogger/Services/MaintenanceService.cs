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
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace IndustrialDataLogger.Services
{
    public class MaintenanceService : IMaintenanceService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IHubContext<MonitoringHub> _hubContext;
        private readonly IEventLogService _eventLogService;
        private readonly ILogger<MaintenanceService> _logger;

        // Dedup / Cooldown Cache: key = $"{machineId}_{component}", value = lastCreationUtc
        private static readonly Dictionary<string, DateTime> _cooldownCache = new();
        private static readonly object _cacheLock = new();
        private static readonly TimeSpan CooldownDuration = TimeSpan.FromMinutes(10);

        public MaintenanceService(
            IServiceScopeFactory scopeFactory,
            IHubContext<MonitoringHub> hubContext,
            IEventLogService eventLogService,
            ILogger<MaintenanceService> logger)
        {
            _scopeFactory = scopeFactory;
            _hubContext = hubContext;
            _eventLogService = eventLogService;
            _logger = logger;
        }

        public async Task<MaintenanceTaskResponseDto> CreateTaskAsync(CreateMaintenanceTaskDto dto, CancellationToken cancellationToken = default)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IndustrialDbContext>();

            var entity = new MaintenanceTask
            {
                MachineId = dto.MachineId > 0 ? dto.MachineId : 1,
                Component = string.IsNullOrWhiteSpace(dto.Component) ? "Genel Mekanik" : dto.Component.Trim(),
                Reason = string.IsNullOrWhiteSpace(dto.Reason) ? "Planlı / Manuel Bakım Talebi" : dto.Reason.Trim(),
                Priority = dto.Priority,
                Status = MaintenanceStatus.Open,
                AssignedTo = dto.AssignedTo?.Trim(),
                CreatedAt = DateTime.UtcNow,
                AlarmLogId = dto.AlarmLogId
            };

            db.MaintenanceTasks.Add(entity);
            await db.SaveChangesAsync(cancellationToken);

            var machine = await db.Machines.AsNoTracking().FirstOrDefaultAsync(m => m.Id == entity.MachineId, cancellationToken);
            var response = MapToResponseDto(entity, machine);

            _logger.LogInformation("[İŞ EMRİ OLUŞTURULDU] ID #{Id} - Makine #{MachineId} ({Component}) - Öncelik: {Priority}",
                entity.Id, entity.MachineId, entity.Component, entity.Priority);

            await _eventLogService.LogEventAsync(
                "MAINTENANCE_TASK_CREATED",
                $"[Makine #{entity.MachineId}] {entity.Component} için yeni bakım iş emri açıldı (#{entity.Id}): {entity.Reason}",
                entity.Priority == MaintenancePriority.Critical ? AlarmSeverity.Critical : AlarmSeverity.Warning,
                "MaintenanceEngine",
                cancellationToken);

            await _hubContext.Clients.All.SendAsync("ReceiveMaintenanceTaskCreated", response, cancellationToken);

            return response;
        }

        public async Task<MaintenanceTaskResponseDto?> AutoCreateTaskFromAlarmAsync(AlarmLog alarm, CancellationToken cancellationToken = default)
        {
            if (alarm == null) return null;

            int machineId = alarm.MachineId > 0 ? alarm.MachineId : 1;
            string component = DetermineComponentFromAlarm(alarm.AlarmType);

            // Cooldown & Deduplication Kontrolü
            string cacheKey = $"{machineId}_{component}";
            lock (_cacheLock)
            {
                if (_cooldownCache.TryGetValue(cacheKey, out var lastTime))
                {
                    if (DateTime.UtcNow - lastTime < CooldownDuration)
                    {
                        _logger.LogDebug("[BAKIM COOLDOWN] {Key} için son 10 dakika içinde iş emri açılmış. Mükerrer kayıt engellendi.", cacheKey);
                        return null;
                    }
                }
                _cooldownCache[cacheKey] = DateTime.UtcNow;
            }

            var dto = new CreateMaintenanceTaskDto
            {
                MachineId = machineId,
                Component = component,
                Reason = $"[Otomatik Tetikleme] {alarm.AlarmType}: {alarm.Message}",
                Priority = alarm.Severity == AlarmSeverity.Critical ? MaintenancePriority.Critical : MaintenancePriority.High,
                AssignedTo = "Nöbetçi Bakım Ekibi",
                AlarmLogId = alarm.Id
            };

            return await CreateTaskAsync(dto, cancellationToken);
        }

        public async Task<IReadOnlyList<MaintenanceTaskResponseDto>> GetTasksAsync(
            int? machineId = null,
            MaintenanceStatus? status = null,
            MaintenancePriority? priority = null,
            CancellationToken cancellationToken = default)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IndustrialDbContext>();

            var query = db.MaintenanceTasks
                .AsNoTracking()
                .Include(t => t.Machine)
                .AsQueryable();

            if (machineId.HasValue && machineId.Value > 0)
            {
                query = query.Where(t => t.MachineId == machineId.Value);
            }

            if (status.HasValue)
            {
                query = query.Where(t => t.Status == status.Value);
            }

            if (priority.HasValue)
            {
                query = query.Where(t => t.Priority == priority.Value);
            }

            var list = await query
                .OrderByDescending(t => t.Priority)
                .ThenByDescending(t => t.CreatedAt)
                .ToListAsync(cancellationToken);

            return list.Select(t => MapToResponseDto(t, t.Machine)).ToList();
        }

        public async Task<MaintenanceTaskResponseDto?> GetTaskByIdAsync(int id, CancellationToken cancellationToken = default)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IndustrialDbContext>();

            var entity = await db.MaintenanceTasks
                .AsNoTracking()
                .Include(t => t.Machine)
                .FirstOrDefaultAsync(t => t.Id == id, cancellationToken);

            return entity == null ? null : MapToResponseDto(entity, entity.Machine);
        }

        public async Task<MaintenanceTaskResponseDto?> UpdateTaskStatusAsync(
            int id,
            UpdateMaintenanceTaskStatusDto dto,
            CancellationToken cancellationToken = default)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IndustrialDbContext>();

            var entity = await db.MaintenanceTasks.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
            if (entity == null) return null;

            entity.Status = dto.Status;
            if (!string.IsNullOrWhiteSpace(dto.AssignedTo))
            {
                entity.AssignedTo = dto.AssignedTo.Trim();
            }

            if (!string.IsNullOrWhiteSpace(dto.ResolutionNotes))
            {
                entity.ResolutionNotes = dto.ResolutionNotes.Trim();
            }

            if (dto.Status == MaintenanceStatus.Resolved || dto.Status == MaintenanceStatus.Cancelled)
            {
                entity.ResolvedAt = DateTime.UtcNow;
            }

            await db.SaveChangesAsync(cancellationToken);

            var machine = await db.Machines.AsNoTracking().FirstOrDefaultAsync(m => m.Id == entity.MachineId, cancellationToken);
            var response = MapToResponseDto(entity, machine);

            _logger.LogInformation("[İŞ EMRİ GÜNCELLENDİ] ID #{Id} - Yeni Durum: {Status}", id, dto.Status);

            await _eventLogService.LogEventAsync(
                "MAINTENANCE_TASK_UPDATED",
                $"[İş Emri #{id}] Durum '{dto.Status}' olarak güncellendi (Atanan: {entity.AssignedTo ?? "Yok"}).",
                AlarmSeverity.Info,
                "MaintenanceEngine",
                cancellationToken);

            await _hubContext.Clients.All.SendAsync("ReceiveMaintenanceTaskUpdated", response, cancellationToken);

            return response;
        }

        public async Task<MaintenanceStatsDto> GetStatsAsync(int? machineId = null, CancellationToken cancellationToken = default)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IndustrialDbContext>();

            var query = db.MaintenanceTasks.AsNoTracking().AsQueryable();
            if (machineId.HasValue && machineId.Value > 0)
            {
                query = query.Where(t => t.MachineId == machineId.Value);
            }

            var tasks = await query.ToListAsync(cancellationToken);

            var resolvedTasksWithDuration = tasks
                .Where(t => t.Status == MaintenanceStatus.Resolved && t.ResolvedAt.HasValue)
                .Select(t => (t.ResolvedAt!.Value - t.CreatedAt).TotalMinutes)
                .ToList();

            double? avgDuration = resolvedTasksWithDuration.Any()
                ? Math.Round(resolvedTasksWithDuration.Average(), 1)
                : null;

            return new MaintenanceStatsDto
            {
                TotalTasks = tasks.Count,
                OpenCount = tasks.Count(t => t.Status == MaintenanceStatus.Open),
                InProgressCount = tasks.Count(t => t.Status == MaintenanceStatus.InProgress),
                ResolvedCount = tasks.Count(t => t.Status == MaintenanceStatus.Resolved),
                CriticalPriorityCount = tasks.Count(t => t.Priority == MaintenancePriority.Critical && t.Status != MaintenanceStatus.Resolved),
                AvgResolutionTimeMinutes = avgDuration
            };
        }

        private static string DetermineComponentFromAlarm(string alarmType)
        {
            if (string.IsNullOrWhiteSpace(alarmType)) return "Genel Mekanik";

            string upper = alarmType.ToUpperInvariant();
            if (upper.Contains("TEMP") || upper.Contains("HEAT") || upper.Contains("SICAK"))
            {
                return "Soğutma Sistemi & Termal Eşanjör";
            }
            if (upper.Contains("PRESS") || upper.Contains("BASIN"))
            {
                return "Hidrolik Pompa & Basınç Valfleri";
            }
            if (upper.Contains("PLC") || upper.Contains("CONN") || upper.Contains("COMM"))
            {
                return "PLC İletişim Hattı & Ethernet Modülü";
            }
            if (upper.Contains("ANOMALY"))
            {
                return "Sensör & Geri Besleme Hattı";
            }
            return "Ana Motor & Mekanik Tahrik";
        }

        private static MaintenanceTaskResponseDto MapToResponseDto(MaintenanceTask entity, Machine? machine)
        {
            return new MaintenanceTaskResponseDto
            {
                Id = entity.Id,
                MachineId = entity.MachineId,
                MachineCode = machine?.MachineCode ?? $"M-0{entity.MachineId}",
                MachineName = machine?.Name ?? $"Makine #{entity.MachineId}",
                Component = entity.Component,
                Reason = entity.Reason,
                Priority = entity.Priority,
                Status = entity.Status,
                AssignedTo = entity.AssignedTo,
                CreatedAt = entity.CreatedAt,
                ResolvedAt = entity.ResolvedAt,
                ResolutionNotes = entity.ResolutionNotes,
                AlarmLogId = entity.AlarmLogId
            };
        }
    }
}
