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
        private readonly IAnomalyDetectionEngine _anomalyEngine;

        // Aktif alarmları thread-safe olarak bellekte takip eder (Key: "{machineId}:{alarmType}")
        private readonly ConcurrentDictionary<string, AlarmLog> _activeAlarms = new();

        // Konfigüre edilebilir kuralların yüksek performanslı bellek içi önbelleği
        private readonly List<AlarmRule> _cachedRules = new();
        private readonly ReaderWriterLockSlim _rulesLock = new();
        private bool _rulesInitialized = false;

        public AlarmService(
            ILogger<AlarmService> logger,
            IServiceProvider serviceProvider,
            IHubContext<MonitoringHub> hubContext,
            IEventLogService eventLogService,
            IAnomalyDetectionEngine? anomalyEngine = null)
        {
            _logger = logger;
            _serviceProvider = serviceProvider;
            _hubContext = hubContext;
            _eventLogService = eventLogService;
            _anomalyEngine = anomalyEngine ?? new AnomalyDetectionEngine(Microsoft.Extensions.Logging.Abstractions.NullLogger<AnomalyDetectionEngine>.Instance);
        }

        #region Telemetri ve Eşik Değerlendirme (Kural Motoru & Anomali Tespiti)

        public async Task ProcessSensorReadingAsync(SensorData data, int machineId = 1, CancellationToken cancellationToken = default)
        {
            if (data == null) return;

            int mId = data.MachineId > 0 ? data.MachineId : machineId;

            // 1. Kuralların yüklendiğinden emin ol ve statik eşikleri değerlendir
            await EnsureRulesLoadedAsync(cancellationToken);

            var activeRules = GetActiveRulesForMachine(mId);

            // Metrik bazlı değer haritası
            var metrics = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
            {
                { "Temperature", data.Temperature },
                { "Pressure", data.Pressure },
                { "ErrorCode", data.ErrorCode },
                { "MachineStatus", data.MachineStatus ? 1.0 : 0.0 }
            };

            // Her metrik için kuralları çalıştır
            foreach (var metricGroup in activeRules.GroupBy(r => r.Metric, StringComparer.OrdinalIgnoreCase))
            {
                string metricName = metricGroup.Key;
                if (!metrics.TryGetValue(metricName, out double metricValue))
                {
                    continue;
                }

                // Kuralları ciddiyet derecesine göre sırala (Critical önce değerlendirilsin)
                var sortedRules = metricGroup.OrderByDescending(r => r.Severity).ToList();
                bool anyTriggeredInGroup = false;

                foreach (var rule in sortedRules)
                {
                    bool isTriggered = EvaluateCondition(metricValue, rule.Operator, rule.Threshold);

                    if (isTriggered)
                    {
                        anyTriggeredInGroup = true;
                        string message = FormatRuleMessage(rule, metricValue);

                        // Diğer aynı metrikteki alt seviye alarmları çöz, en kritik olanı aç
                        foreach (var otherRule in sortedRules.Where(r => r.Id != rule.Id && r.AlarmType != rule.AlarmType))
                        {
                            await TryResolveAlarmAsync(otherRule.AlarmType, mId, cancellationToken);
                        }

                        await RaiseOrUpdateAlarmAsync(
                            rule.AlarmType,
                            rule.Severity,
                            message,
                            metricValue,
                            rule.Threshold,
                            mId,
                            cancellationToken);

                        break; // Bu gruptaki en kritik kural tetiklendi, alt kurallara bakma
                    }
                }

                // Grupta hiçbir kural tetiklenmediyse ve aktif alarm varsa çözüldü (RESOLVED) yap
                if (!anyTriggeredInGroup)
                {
                    foreach (var rule in sortedRules)
                    {
                        await TryResolveAlarmAsync(rule.AlarmType, mId, cancellationToken);
                    }
                }
            }

            // 2. Zaman Serisi İstatistiksel Anomali Tespiti (Z-Score & Rate of Change)
            try
            {
                var anomalyResults = _anomalyEngine.AnalyzeSensorData(data, mId);
                foreach (var anom in anomalyResults)
                {
                    string anomAlarmType = $"ANOMALY_{anom.MetricName.ToUpperInvariant()}";
                    if (anom.HasAnomaly)
                    {
                        await RaiseOrUpdateAlarmAsync(
                            anomAlarmType,
                            AlarmSeverity.Warning,
                            anom.AnomalyReason,
                            anom.CurrentValue,
                            anom.Mean,
                            mId,
                            cancellationToken);

                        await _eventLogService.LogEventAsync(
                            "ANOMALY_DETECTED",
                            $"[Makine #{mId}] {anom.AnomalyReason}",
                            AlarmSeverity.Warning,
                            "AnomalyEngine",
                            cancellationToken);

                        await _hubContext.Clients.All.SendAsync("ReceiveAnomalyEvent", anom, cancellationToken);
                    }
                    else
                    {
                        await TryResolveAlarmAsync(anomAlarmType, mId, cancellationToken);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Anomali tespiti sırasında hata oluştu: {Message}", ex.Message);
            }
        }

        public async Task ProcessPlcStatusAsync(PlcConnectionState state, int machineId = 1, CancellationToken cancellationToken = default)
        {
            if (state == PlcConnectionState.Connected)
            {
                // PLC bağlı ise bağlantı koptu alarmını çöz
                await TryResolveAlarmAsync("PLC_CONNECTION_LOST", machineId, cancellationToken);
            }
            else if (state == PlcConnectionState.Disconnected || state == PlcConnectionState.Reconnecting)
            {
                // PLC bağlantısı koptu alarmı üret
                await RaiseOrUpdateAlarmAsync("PLC_CONNECTION_LOST", AlarmSeverity.Critical,
                    state == PlcConnectionState.Reconnecting
                        ? "PLC Bağlantısı Koptu! Otomatik yeniden bağlanma devrede..."
                        : "PLC Bağlantısı Yok! Sunucu PLC ile haberleşemiyor.",
                    null, null, machineId, cancellationToken);
            }
        }

        public static bool EvaluateCondition(double value, ComparisonOperator op, double threshold)
        {
            return op switch
            {
                ComparisonOperator.GreaterThan => value > threshold,
                ComparisonOperator.GreaterThanOrEqual => value >= threshold,
                ComparisonOperator.LessThan => value < threshold,
                ComparisonOperator.LessThanOrEqual => value <= threshold,
                ComparisonOperator.Equal => Math.Abs(value - threshold) < 0.0001,
                ComparisonOperator.NotEqual => Math.Abs(value - threshold) >= 0.0001,
                _ => false
            };
        }

        private static string FormatRuleMessage(AlarmRule rule, double value)
        {
            if (string.IsNullOrWhiteSpace(rule.MessageTemplate))
            {
                return $"[Alarm] {rule.RuleName}: Değer = {value:F1} (Eşik: {rule.Threshold:F1})";
            }

            return rule.MessageTemplate
                .Replace("{value}", value.ToString("F1"))
                .Replace("{threshold}", rule.Threshold.ToString("F1"))
                .Replace("{metric}", rule.Metric)
                .Replace("{rule}", rule.RuleName);
        }

        #endregion

        #region Alarm Yaşam Döngüsü (Lifecycle: NORMAL -> TRIGGERED -> ACTIVE -> ACKNOWLEDGED -> RESOLVED)

        private async Task RaiseOrUpdateAlarmAsync(
            string alarmType,
            AlarmSeverity severity,
            string message,
            double? triggeredValue,
            double? thresholdValue,
            int machineId,
            CancellationToken cancellationToken)
        {
            string key = $"{machineId}:{alarmType}";

            if (_activeAlarms.TryGetValue(key, out var existingAlarm))
            {
                // Alarm zaten aktif veya onaylanmış, güncel telemetri değerini güncelle
                existingAlarm.TriggeredValue = triggeredValue;
                existingAlarm.Message = message;
                return;
            }

            // Yaşam döngüsü: Alarm tetiklendi (TRIGGERED -> ACTIVE)
            var alarm = new AlarmLog
            {
                MachineId = machineId,
                AlarmType = alarmType,
                Severity = severity,
                Status = AlarmStatus.Active,
                Message = message,
                TriggeredValue = triggeredValue,
                ThresholdValue = thresholdValue,
                CreatedAt = DateTime.UtcNow
            };

            _activeAlarms[key] = alarm;

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

            _logger.LogWarning($"[ALARM AKTİF] (Makine #{machineId}) [{severity}] {alarmType}: {message}");

            // Sistem Olay Günlüğüne Kaydet
            await _eventLogService.LogEventAsync("ALARM_TRIGGERED", $"[Makine #{machineId}] [{severity}] {alarmType}: {message}", severity, "AlarmEngine", cancellationToken);

            // SignalR ile anlık yayın
            await BroadcastAlarmStateAsync(cancellationToken);
        }

        private async Task TryResolveAlarmAsync(string alarmType, int machineId, CancellationToken cancellationToken)
        {
            string key = $"{machineId}:{alarmType}";

            if (_activeAlarms.TryRemove(key, out var alarm))
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

                _logger.LogInformation($"[ALARM ÇÖZÜLDÜ] (Makine #{machineId}) {alarmType} normale döndü.");

                // Çözülme Olayını Kaydet
                await _eventLogService.LogEventAsync("ALARM_RESOLVED", $"[Makine #{machineId}] Alarm normale döndü ve çözüldü: {alarmType}", AlarmSeverity.Info, "AlarmEngine", cancellationToken);

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

                        // Onaylama Olayını Kaydet
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

        public Task<IReadOnlyList<AlarmLog>> GetActiveAlarmsAsync(int? machineId = null, CancellationToken cancellationToken = default)
        {
            var query = _activeAlarms.Values.AsEnumerable();
            if (machineId.HasValue)
            {
                query = query.Where(a => a.MachineId == machineId.Value);
            }
            IReadOnlyList<AlarmLog> list = query.OrderByDescending(a => a.CreatedAt).ToList();
            return Task.FromResult(list);
        }

        public async Task<IReadOnlyList<AlarmLog>> GetAlarmHistoryAsync(
            int limit = 50,
            AlarmSeverity? severity = null,
            AlarmStatus? status = null,
            int? machineId = null,
            CancellationToken cancellationToken = default)
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IndustrialDbContext>();

            var query = db.Alarms.AsNoTracking();

            if (machineId.HasValue)
            {
                query = query.Where(a => a.MachineId == machineId.Value);
            }

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
                        machineId = a.MachineId,
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

        #endregion

        #region Kural Yönetimi ve Önbellekleme (CRUD & Rule Engine)

        private async Task EnsureRulesLoadedAsync(CancellationToken cancellationToken)
        {
            if (_rulesInitialized) return;

            await ReloadRulesCacheAsync(cancellationToken);
        }

        public async Task ReloadRulesCacheAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var db = scope.ServiceProvider.GetService<IndustrialDbContext>();
                if (db != null)
                {
                    var rules = await db.AlarmRules.AsNoTracking().ToListAsync(cancellationToken);

                    _rulesLock.EnterWriteLock();
                    try
                    {
                        _cachedRules.Clear();
                        _cachedRules.AddRange(rules);
                        _rulesInitialized = true;
                    }
                    finally
                    {
                        _rulesLock.ExitWriteLock();
                    }

                    _logger.LogInformation($"[AlarmService] {_cachedRules.Count} adet alarm kuralı önbelleğe yüklendi.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("Alarm kuralları önbelleğe yüklenirken hata: {Message}", ex.Message);
            }
        }

        private List<AlarmRule> GetActiveRulesForMachine(int machineId)
        {
            _rulesLock.EnterReadLock();
            try
            {
                // Belirli makineye atanmış kurallar + genel (MachineId == null) kurallar
                return _cachedRules
                    .Where(r => r.Enabled && (r.MachineId == null || r.MachineId == machineId))
                    .ToList();
            }
            finally
            {
                _rulesLock.ExitReadLock();
            }
        }

        public async Task<IReadOnlyList<AlarmRule>> GetRulesAsync(int? machineId = null, bool? enabledOnly = null, CancellationToken cancellationToken = default)
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IndustrialDbContext>();

            var query = db.AlarmRules.Include(r => r.Machine).AsNoTracking();

            if (machineId.HasValue)
            {
                query = query.Where(r => r.MachineId == null || r.MachineId == machineId.Value);
            }

            if (enabledOnly.HasValue)
            {
                query = query.Where(r => r.Enabled == enabledOnly.Value);
            }

            return await query.OrderBy(r => r.Metric).ThenBy(r => r.Threshold).ToListAsync(cancellationToken);
        }

        public async Task<AlarmRule?> GetRuleByIdAsync(int id, CancellationToken cancellationToken = default)
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IndustrialDbContext>();

            return await db.AlarmRules.Include(r => r.Machine).FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        }

        public async Task<AlarmRule> CreateRuleAsync(AlarmRule rule, CancellationToken cancellationToken = default)
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IndustrialDbContext>();

            rule.CreatedAt = DateTime.UtcNow;
            await db.AlarmRules.AddAsync(rule, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);

            await ReloadRulesCacheAsync(cancellationToken);

            _logger.LogInformation($"[AlarmRule Oluşturuldu] #{rule.Id} - {rule.RuleName} ({rule.Metric} {rule.Operator} {rule.Threshold})");
            return rule;
        }

        public async Task<bool> UpdateRuleAsync(AlarmRule rule, CancellationToken cancellationToken = default)
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IndustrialDbContext>();

            var existing = await db.AlarmRules.FindAsync(new object[] { rule.Id }, cancellationToken);
            if (existing == null) return false;

            existing.MachineId = rule.MachineId;
            existing.RuleName = rule.RuleName;
            existing.Metric = rule.Metric;
            existing.Operator = rule.Operator;
            existing.Threshold = rule.Threshold;
            existing.Severity = rule.Severity;
            existing.AlarmType = rule.AlarmType;
            existing.MessageTemplate = rule.MessageTemplate;
            existing.Enabled = rule.Enabled;
            existing.UpdatedAt = DateTime.UtcNow;

            await db.SaveChangesAsync(cancellationToken);
            await ReloadRulesCacheAsync(cancellationToken);

            _logger.LogInformation($"[AlarmRule Güncellendi] #{rule.Id} - {rule.RuleName}");
            return true;
        }

        public async Task<bool> DeleteRuleAsync(int id, CancellationToken cancellationToken = default)
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IndustrialDbContext>();

            var rule = await db.AlarmRules.FindAsync(new object[] { id }, cancellationToken);
            if (rule == null) return false;

            db.AlarmRules.Remove(rule);
            await db.SaveChangesAsync(cancellationToken);
            await ReloadRulesCacheAsync(cancellationToken);

            _logger.LogInformation($"[AlarmRule Silindi] #{id}");
            return true;
        }

        public async Task<bool> ToggleRuleAsync(int id, bool enabled, CancellationToken cancellationToken = default)
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IndustrialDbContext>();

            var rule = await db.AlarmRules.FindAsync(new object[] { id }, cancellationToken);
            if (rule == null) return false;

            rule.Enabled = enabled;
            rule.UpdatedAt = DateTime.UtcNow;

            await db.SaveChangesAsync(cancellationToken);
            await ReloadRulesCacheAsync(cancellationToken);

            _logger.LogInformation($"[AlarmRule Durumu Değişti] #{id} Enabled={enabled}");
            return true;
        }

        public async Task EnsureDefaultRulesSeededAsync(CancellationToken cancellationToken = default)
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IndustrialDbContext>();

            if (!await db.AlarmRules.AnyAsync(cancellationToken))
            {
                var defaultRules = new List<AlarmRule>
                {
                    new AlarmRule
                    {
                        RuleName = "Yüksek Sıcaklık Uyarısı",
                        Metric = "Temperature",
                        Operator = ComparisonOperator.GreaterThan,
                        Threshold = 80.0,
                        Severity = AlarmSeverity.Warning,
                        AlarmType = "HIGH_TEMPERATURE",
                        MessageTemplate = "Yüksek Sıcaklık Uyarısı! Değer: {value}°C (Eşik: {threshold}°C)",
                        Enabled = true,
                        CreatedAt = DateTime.UtcNow
                    },
                    new AlarmRule
                    {
                        RuleName = "Kritik Sıcaklık Tehlikesi",
                        Metric = "Temperature",
                        Operator = ComparisonOperator.GreaterThan,
                        Threshold = 90.0,
                        Severity = AlarmSeverity.Critical,
                        AlarmType = "CRITICAL_TEMPERATURE",
                        MessageTemplate = "Kritik Sıcaklık! Değer: {value}°C (Eşik: {threshold}°C)",
                        Enabled = true,
                        CreatedAt = DateTime.UtcNow
                    },
                    new AlarmRule
                    {
                        RuleName = "Yüksek Basınç Uyarısı",
                        Metric = "Pressure",
                        Operator = ComparisonOperator.GreaterThan,
                        Threshold = 8.0,
                        Severity = AlarmSeverity.Warning,
                        AlarmType = "HIGH_PRESSURE",
                        MessageTemplate = "Yüksek Basınç Uyarısı! Değer: {value} bar (Eşik: {threshold} bar)",
                        Enabled = true,
                        CreatedAt = DateTime.UtcNow
                    },
                    new AlarmRule
                    {
                        RuleName = "Kritik Basınç Tehlikesi",
                        Metric = "Pressure",
                        Operator = ComparisonOperator.GreaterThan,
                        Threshold = 9.0,
                        Severity = AlarmSeverity.Critical,
                        AlarmType = "CRITICAL_PRESSURE",
                        MessageTemplate = "Kritik Basınç! Değer: {value} bar (Eşik: {threshold} bar)",
                        Enabled = true,
                        CreatedAt = DateTime.UtcNow
                    }
                };

                await db.AlarmRules.AddRangeAsync(defaultRules, cancellationToken);
                await db.SaveChangesAsync(cancellationToken);
                _logger.LogInformation("[AlarmService] Varsayılan alarm kuralları veritabanına başarıyla tohumlandı.");
            }

            await ReloadRulesCacheAsync(cancellationToken);
        }

        #endregion
    }
}
