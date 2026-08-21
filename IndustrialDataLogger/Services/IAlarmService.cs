using IndustrialDataLogger.Enums;
using IndustrialDataLogger.Models;
using IndustrialDataLogger.Models.Entities;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace IndustrialDataLogger.Services
{
    public interface IAlarmService
    {
        Task ProcessSensorReadingAsync(SensorData data, int machineId = 1, CancellationToken cancellationToken = default);
        Task ProcessPlcStatusAsync(PlcConnectionState state, int machineId = 1, CancellationToken cancellationToken = default);
        Task<IReadOnlyList<AlarmLog>> GetActiveAlarmsAsync(int? machineId = null, CancellationToken cancellationToken = default);
        Task<IReadOnlyList<AlarmLog>> GetAlarmHistoryAsync(int limit = 50, AlarmSeverity? severity = null, AlarmStatus? status = null, int? machineId = null, CancellationToken cancellationToken = default);
        Task<bool> AcknowledgeAlarmAsync(long alarmId, CancellationToken cancellationToken = default);

        // Kural Motoru (Rule Engine) Yönetim Metotları
        Task<IReadOnlyList<AlarmRule>> GetRulesAsync(int? machineId = null, bool? enabledOnly = null, CancellationToken cancellationToken = default);
        Task<AlarmRule?> GetRuleByIdAsync(int id, CancellationToken cancellationToken = default);
        Task<AlarmRule> CreateRuleAsync(AlarmRule rule, CancellationToken cancellationToken = default);
        Task<bool> UpdateRuleAsync(AlarmRule rule, CancellationToken cancellationToken = default);
        Task<bool> DeleteRuleAsync(int id, CancellationToken cancellationToken = default);
        Task<bool> ToggleRuleAsync(int id, bool enabled, CancellationToken cancellationToken = default);
        Task EnsureDefaultRulesSeededAsync(CancellationToken cancellationToken = default);
        Task ReloadRulesCacheAsync(CancellationToken cancellationToken = default);
    }
}
