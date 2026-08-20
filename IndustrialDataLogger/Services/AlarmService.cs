using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using IndustrialDataLogger.Data;
using IndustrialDataLogger.Enums;
using IndustrialDataLogger.Hubs;
using IndustrialDataLogger.Models;
using IndustrialDataLogger.Models.Entities;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace IndustrialDataLogger.Services
{
    public class AlarmService : IAlarmService
    {
        private readonly ILogger<AlarmService> _logger;
        private readonly IServiceProvider _serviceProvider;
        private readonly IHubContext<MonitoringHub> _hubContext;
        private readonly IEventLogService _eventLogService;

        // GÜN 3 (Sprint 3.1) Eşik Tanımları
        public const double CriticalTempThreshold = 90.0;
        public const double WarningTempThreshold = 80.0;
        public const double CriticalPressureThreshold = 9.0;
        public const double WarningPressureThreshold = 8.0;

        // Aktif alarmları thread-safe olarak bellekte takip eder
        private readonly ConcurrentDictionary<string, AlarmLog> _activeAlarms = new();

        public AlarmService(
            ILogger<AlarmService> logger,
            IServiceProvider serviceProvider,
            IHubContext<MonitoringHub> hubContext,
            IEventLogService eventLogService)
        {
            _logger = logger;
            _serviceProvider = serviceProvider;
            _hubContext = hubContext;
            _eventLogService = eventLogService;
        }

        public async Task ProcessSensorReadingAsync(SensorData data, CancellationToken cancellationToken = default)
        {
            if (data == null) return;

            // 1. Sıcaklık Eşik Kontrolü
            if (data.Temperature > CriticalTempThreshold)
            {
                // Warning varsa çöz, Critical aç
                await TryResolveAlarmAsync("HIGH_TEMPERATURE", cancellationToken);
                await RaiseOrUpdateAlarmAsync("CRITICAL_TEMPERATURE", AlarmSeverity.Critical,
                    $"Kritik Sıcaklık! Değer: {data.Temperature:F1}°C (Eşik: {CriticalTempThreshold}°C)",
                    data.Temperature, CriticalTempThreshold, cancellationToken);
            }
            else if (data.Temperature > WarningTempThreshold)
            {
                // Critical varsa çöz, Warning aç
                await TryResolveAlarmAsync("CRITICAL_TEMPERATURE", cancellationToken);
                await RaiseOrUpdateAlarmAsync("HIGH_TEMPERATURE", AlarmSeverity.Warning,
                    $"Yüksek Sıcaklık Uyarısı! Değer: {data.Temperature:F1}°C (Eşik: {WarningTempThreshold}°C)",
                    data.Temperature, WarningTempThreshold, cancellationToken);
            }
            else
            {
                // Sıcaklık normale döndü
                await TryResolveAlarmAsync("CRITICAL_TEMPERATURE", cancellationToken);
                await TryResolveAlarmAsync("HIGH_TEMPERATURE", cancellationToken);
            }

            // 2. Basınç Eşik Kontrolü
            if (data.Pressure > CriticalPressureThreshold)
            {
                await TryResolveAlarmAsync("HIGH_PRESSURE", cancellationToken);
                await RaiseOrUpdateAlarmAsync("CRITICAL_PRESSURE", AlarmSeverity.Critical,
                    $"Kritik Basınç! Değer: {data.Pressure:F1} bar (Eşik: {CriticalPressureThreshold} bar)",
                    data.Pressure, CriticalPressureThreshold, cancellationToken);
            }
            else if (data.Pressure > WarningPressureThreshold)
            {
                await TryResolveAlarmAsync("CRITICAL_PRESSURE", cancellationToken);
                await RaiseOrUpdateAlarmAsync("HIGH_PRESSURE", AlarmSeverity.Warning,
                    $"Yüksek Basınç Uyarısı! Değer: {data.Pressure:F1} bar (Eşik: {WarningPressureThreshold} bar)",
                    data.Pressure, WarningPressureThreshold, cancellationToken);
            }
            else
            {
                // Basınç normale döndü
                await TryResolveAlarmAsync("CRITICAL_PRESSURE", cancellationToken);
                await TryResolveAlarmAsync("HIGH_PRESSURE", cancellationToken);
            }
        }

        public async Task ProcessPlcStatusAsync(PlcConnectionState state, CancellationToken cancellationToken = default)
        {
            if (state == PlcConnectionState.Connected)
            {
                // PLC bağlı ise bağlantı koptu alarmını çöz
                await TryResolveAlarmAsync("PLC_CONNECTION_LOST", cancellationToken);
            }
            else if (state == PlcConnectionState.Disconnected || state == PlcConnectionState.Reconnecting)
            {
                // PLC bağlantısı koptu alarmı üret
                await RaiseOrUpdateAlarmAsync("PLC_CONNECTION_LOST", AlarmSeverity.Critical,
                    state == PlcConnectionState.Reconnecting
                        ? "PLC Bağlantısı Koptu! Otomatik yeniden bağlanma devrede..."
                        : "PLC Bağlantısı Yok! Sunucu PLC ile haberleşemiyor.",
                    null, null, cancellationToken);
            }
        }

        private async Task RaiseOrUpdateAlarmAsync(
            string alarmType,
            AlarmSeverity severity,
            string message,
            double? triggeredValue,
            double? thresholdValue,
            CancellationToken cancellationToken)
        {
            if (_activeAlarms.TryGetValue(alarmType, out var existingAlarm))
            {
                // Alarm zaten aktif, değer güncelleniyorsa logla
                existingAlarm.TriggeredValue = triggeredValue;
                existingAlarm.Message = message;
                return;
            }

            var alarm = new AlarmLog
            {
                AlarmType = alarmType,
                Severity = severity,
                Status = AlarmStatus.Active,
                Message = message,
                TriggeredValue = triggeredValue,
                ThresholdValue = thresholdValue,
                CreatedAt = DateTime.UtcNow
            };

            _activeAlarms[alarmType] = alarm;

            // Veritabanına kaydet
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var db = scope.ServiceProvider.GetService<IndustrialDbContext>();
                if (db != null)
                {
                    await db.Alarms.AddAsync(alarm, cancellationToken);
                    await db.SaveChangesAsync(cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("Alarm veritabanına kaydedilirken hata: {Message}", ex.Message);
            }

            _logger.LogWarning($"[ALARM ÜRETİLDİ] [{severity}] {alarmType}: {message}");

            // GÜN 3 (Sprint 3.4): Sistem Olayı Olarak Kaydet
            await _eventLogService.LogEventAsync("ALARM_TRIGGERED", $"[{severity}] {alarmType}: {message}", severity, "AlarmEngine", cancellationToken);

            // SignalR ile anlık yayın
            await BroadcastAlarmStateAsync(cancellationToken);
        }

        private async Task TryResolveAlarmAsync(string alarmType, CancellationToken cancellationToken)
        {
            if (_activeAlarms.TryRemove(alarmType, out var alarm))
            {
                alarm.Status = AlarmStatus.Resolved;
                alarm.ResolvedAt = DateTime.UtcNow;

                // Veritabanında güncelle
                try
                {
                    using var scope = _serviceProvider.CreateScope();
                    var db = scope.ServiceProvider.GetService<IndustrialDbContext>();
                    if (db != null && alarm.Id > 0)
                    {
                        var entity = await db.Alarms.FindAsync(new object[] { alarm.Id }, cancellationToken);
                        if (entity != null)
                        {
                            entity.Status = AlarmStatus.Resolved;
                            entity.ResolvedAt = DateTime.UtcNow;
                            await db.SaveChangesAsync(cancellationToken);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError("Alarm çözülme durumu veritabanına kaydedilirken hata: {Message}", ex.Message);
                }

                _logger.LogInformation($"[ALARM ÇÖZÜLDÜ] {alarmType} normale döndü.");

                // GÜN 3 (Sprint 3.4): Çözülme Olayını Kaydet
                await _eventLogService.LogEventAsync("ALARM_RESOLVED", $"Alarm normale döndü ve çözüldü: {alarmType}", AlarmSeverity.Info, "AlarmEngine", cancellationToken);

                // SignalR ile anlık yayın
                await BroadcastAlarmStateAsync(cancellationToken);
            }
        }

        public async Task<bool> AcknowledgeAlarmAsync(long alarmId, CancellationToken cancellationToken = default)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var db = scope.ServiceProvider.GetService<IndustrialDbContext>();
                if (db != null)
                {
                    var entity = await db.Alarms.FindAsync(new object[] { alarmId }, cancellationToken);
                    if (entity != null)
                    {
                        entity.Status = AlarmStatus.Acknowledged;
                        entity.AcknowledgedAt = DateTime.UtcNow;
                        await db.SaveChangesAsync(cancellationToken);

                        var activeMatch = _activeAlarms.Values.FirstOrDefault(a => a.Id == alarmId);
                        if (activeMatch != null)
                        {
                            activeMatch.Status = AlarmStatus.Acknowledged;
                            activeMatch.AcknowledgedAt = DateTime.UtcNow;
                        }

                        // GÜN 3 (Sprint 3.4): Onaylama Olayını Kaydet
                        await _eventLogService.LogEventAsync("ALARM_ACKNOWLEDGED", $"Alarm operatör tarafından onaylandı: {entity.AlarmType} (ID: {alarmId})", AlarmSeverity.Info, "Operator", cancellationToken);

                        await BroadcastAlarmStateAsync(cancellationToken);
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("Alarm onaylanırken hata: {Message}", ex.Message);
            }

            return false;
        }

        public Task<IReadOnlyList<AlarmLog>> GetActiveAlarmsAsync(CancellationToken cancellationToken = default)
        {
            IReadOnlyList<AlarmLog> list = _activeAlarms.Values.OrderByDescending(a => a.CreatedAt).ToList();
            return Task.FromResult(list);
        }

        public async Task<IReadOnlyList<AlarmLog>> GetAlarmHistoryAsync(
            int limit = 50,
            AlarmSeverity? severity = null,
            AlarmStatus? status = null,
            CancellationToken cancellationToken = default)
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IndustrialDbContext>();

            var query = db.Alarms.AsNoTracking();

            if (severity.HasValue)
            {
                query = query.Where(a => a.Severity == severity.Value);
            }

            if (status.HasValue)
            {
                query = query.Where(a => a.Status == status.Value);
            }

            var results = await query
                .OrderByDescending(a => a.CreatedAt)
                .Take(Math.Clamp(limit, 1, 500))
                .ToListAsync(cancellationToken);

            return results;
        }

        private async Task BroadcastAlarmStateAsync(CancellationToken cancellationToken)
        {
            try
            {
                var activeList = _activeAlarms.Values
                    .OrderByDescending(a => a.Severity)
                    .ThenByDescending(a => a.CreatedAt)
                    .Select(a => new
                    {
                        id = a.Id,
                        alarmType = a.AlarmType,
                        severity = a.Severity.ToString(),
                        status = a.Status.ToString(),
                        message = a.Message,
                        triggeredValue = a.TriggeredValue,
                        thresholdValue = a.ThresholdValue,
                        createdAt = a.CreatedAt
                    })
                    .ToList();

                await _hubContext.Clients.All.SendAsync("ReceiveActiveAlarms", activeList, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("SignalR alarm yayını sırasında hata: {Message}", ex.Message);
            }
        }
    }
}
