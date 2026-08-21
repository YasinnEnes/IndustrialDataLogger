using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using IndustrialDataLogger.Enums;
using IndustrialDataLogger.Models.Entities;

namespace IndustrialDataLogger.Services
{
    public interface IEventLogService
    {
        Task LogEventAsync(
            string eventType,
            string description,
            AlarmSeverity severity = AlarmSeverity.Info,
            string source = "System",
            CancellationToken cancellationToken = default);

        Task LogEventAsync(
            string eventType,
            string description,
            AlarmSeverity severity,
            string source,
            int? machineId,
            CancellationToken cancellationToken = default);

        Task<IReadOnlyList<SystemEventLog>> GetRecentEventsAsync(
            int limit = 50,
            string? eventType = null,
            AlarmSeverity? severity = null,
            int? machineId = null,
            CancellationToken cancellationToken = default);
    }
}
