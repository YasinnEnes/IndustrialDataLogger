using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IndustrialDataLogger.Data;
using IndustrialDataLogger.Enums;
using IndustrialDataLogger.Hubs;
using IndustrialDataLogger.Models.Entities;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace IndustrialDataLogger.Services
{
    public class EventLogService : IEventLogService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IHubContext<MonitoringHub> _hubContext;
        private readonly ILogger<EventLogService> _logger;

        public EventLogService(
            IServiceScopeFactory scopeFactory,
            IHubContext<MonitoringHub> hubContext,
            ILogger<EventLogService> logger)
        {
            _scopeFactory = scopeFactory;
            _hubContext = hubContext;
            _logger = logger;
        }

        public Task LogEventAsync(
            string eventType,
            string description,
            AlarmSeverity severity = AlarmSeverity.Info,
            string source = "System",
            CancellationToken cancellationToken = default)
        {
            return LogEventAsync(eventType, description, severity, source, null, cancellationToken);
        }

        public async Task LogEventAsync(
            string eventType,
            string description,
            AlarmSeverity severity,
            string source,
            int? machineId,
            CancellationToken cancellationToken = default)
        {
            var eventLog = new SystemEventLog
            {
                EventType = eventType,
                Description = description,
                Severity = severity,
                Source = source,
                MachineId = machineId ?? 1,
                Timestamp = DateTime.UtcNow
            };

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<IndustrialDbContext>();
                await dbContext.SystemEvents.AddAsync(eventLog, cancellationToken);
                await dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Sistem olayı DB'ye kaydedilirken hata: {Message}", ex.Message);
            }

            // SignalR ile anlık yayın
            try
            {
                await _hubContext.Clients.All.SendAsync("ReceiveSystemEvent", new
                {
                    id = eventLog.Id,
                    machineId = eventLog.MachineId,
                    eventType = eventLog.EventType,
                    description = eventLog.Description,
                    severity = eventLog.Severity.ToString(),
                    source = eventLog.Source,
                    timestamp = eventLog.Timestamp
                }, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Sistem olayı SignalR yayını yapılırken hata: {Message}", ex.Message);
            }

            _logger.LogInformation("[SİSTEM OLAYI] [{Severity}] {EventType} ({Source}) [Makine #{MachineId}]: {Description}", severity, eventType, source, eventLog.MachineId, description);
        }

        public async Task<IReadOnlyList<SystemEventLog>> GetRecentEventsAsync(
            int limit = 50,
            string? eventType = null,
            AlarmSeverity? severity = null,
            int? machineId = null,
            CancellationToken cancellationToken = default)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<IndustrialDbContext>();

                var query = dbContext.SystemEvents.AsNoTracking();

                if (!string.IsNullOrWhiteSpace(eventType))
                {
                    query = query.Where(e => e.EventType == eventType);
                }

                if (severity.HasValue)
                {
                    query = query.Where(e => e.Severity == severity.Value);
                }

                if (machineId.HasValue)
                {
                    query = query.Where(e => e.MachineId == machineId.Value);
                }

                return await query
                    .OrderByDescending(e => e.Timestamp)
                    .Take(Math.Clamp(limit, 1, 500))
                    .ToListAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError("Son sistem olayları çekilirken hata: {Message}", ex.Message);
                return Array.Empty<SystemEventLog>();
            }
        }
    }
}
