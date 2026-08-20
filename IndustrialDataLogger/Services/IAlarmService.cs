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
        Task ProcessSensorReadingAsync(SensorData data, CancellationToken cancellationToken = default);
        Task ProcessPlcStatusAsync(PlcConnectionState state, CancellationToken cancellationToken = default);
        Task<IReadOnlyList<AlarmLog>> GetActiveAlarmsAsync(CancellationToken cancellationToken = default);
        Task<IReadOnlyList<AlarmLog>> GetAlarmHistoryAsync(int limit = 50, AlarmSeverity? severity = null, AlarmStatus? status = null, CancellationToken cancellationToken = default);
        Task<bool> AcknowledgeAlarmAsync(long alarmId, CancellationToken cancellationToken = default);
    }
}
