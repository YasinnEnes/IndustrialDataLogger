using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using IndustrialDataLogger.Models;
using Microsoft.Extensions.Logging;

namespace IndustrialDataLogger.Services
{
    public class AnomalyDetectionEngine : IAnomalyDetectionEngine
    {
        private readonly ILogger<AnomalyDetectionEngine> _logger;
        private readonly int _windowCapacity;
        private readonly double _zScoreThreshold;

        // Key: $"{machineId}:{metricName}"
        private readonly ConcurrentDictionary<string, RollingWindowBuffer> _buffers = new(StringComparer.OrdinalIgnoreCase);

        // Metrik bazlı maksimum izin verilen değişim hızı eşikleri (Birim / Saniye)
        private readonly Dictionary<string, double> _maxRateOfChangeThresholds = new(StringComparer.OrdinalIgnoreCase)
        {
            { "Temperature", 2.5 },   // 2.5°C/sn üzerinde ani tırmanış
            { "Pressure", 1.2 },      // 1.2 bar/sn üzerinde ani basınç darbesi (Pressure surge)
            { "Default", 5.0 }
        };

        public AnomalyDetectionEngine(ILogger<AnomalyDetectionEngine> logger, int windowCapacity = 30, double zScoreThreshold = 2.58)
        {
            _logger = logger;
            _windowCapacity = windowCapacity > 5 ? windowCapacity : 30;
            _zScoreThreshold = zScoreThreshold > 0 ? zScoreThreshold : 2.58;
        }

        public AnomalyAnalysisResult AnalyzeReading(int machineId, string metricName, double value, DateTime timestamp)
        {
            if (string.IsNullOrWhiteSpace(metricName))
            {
                metricName = "Unknown";
            }

            string bufferKey = $"{machineId}:{metricName}";
            var buffer = _buffers.GetOrAdd(bufferKey, _ => new RollingWindowBuffer(_windowCapacity));

            // Tampona ekle ve istatistikleri al
            var (mean, stdDev, roc, sampleCount) = buffer.AddReading(value, timestamp);

            var result = new AnomalyAnalysisResult
            {
                MachineId = machineId,
                MetricName = metricName,
                CurrentValue = value,
                Timestamp = timestamp,
                Mean = Math.Round(mean, 2),
                StandardDeviation = Math.Round(stdDev, 3),
                RateOfChange = Math.Round(roc, 2)
            };

            // En az 5 örnek olmadan istatistiksel Z-Score güvenilir olmaz
            if (sampleCount < 5)
            {
                result.ZScore = 0.0;
                result.IsZScoreAnomaly = false;
                result.IsRateOfChangeAnomaly = false;
                return result;
            }

            // 1. Z-Score Hesaplaması (Z = (x - μ) / σ)
            double zScore = 0.0;
            if (stdDev > 0.001)
            {
                zScore = (value - mean) / stdDev;
            }
            result.ZScore = Math.Round(zScore, 2);

            // Z-Score Anomali Kontrolü (|Z| >= 2.58)
            if (Math.Abs(zScore) >= _zScoreThreshold)
            {
                result.IsZScoreAnomaly = true;
                result.AnomalyType = "STATISTICAL_ZSCORE_ANOMALY";
                result.AnomalyReason = $"[Z-Score Sapması] {metricName} değeri ({value:F1}) hareketli ortalamadan ({mean:F1}) {Math.Abs(zScore):F2}σ saptı (Eşik: {_zScoreThreshold:F2}σ).";
                _logger.LogWarning("İstatistiksel Z-Score Anomalisi Tespit Edildi: Makine #{MachineId}, {Reason}", machineId, result.AnomalyReason);
            }

            // 2. Rate of Change (Eğim / Değişim Hızı) Kontrolü
            double maxRoc = _maxRateOfChangeThresholds.TryGetValue(metricName, out var limit) ? limit : _maxRateOfChangeThresholds["Default"];
            if (Math.Abs(roc) >= maxRoc)
            {
                result.IsRateOfChangeAnomaly = true;
                if (string.IsNullOrEmpty(result.AnomalyType))
                {
                    result.AnomalyType = "RATE_OF_CHANGE_SPIKE";
                }
                else
                {
                    result.AnomalyType += "_AND_ROC_SPIKE";
                }

                string rocReason = $"[Ani Değişim Hızı] {metricName} değişim hızı ({Math.Abs(roc):F2}/sn) izin verilen eşiği ({maxRoc:F2}/sn) aştı.";
                result.AnomalyReason = string.IsNullOrEmpty(result.AnomalyReason) ? rocReason : $"{result.AnomalyReason} | {rocReason}";
                _logger.LogWarning("Ani Değişim Hızı Anomalisi Tespit Edildi: Makine #{MachineId}, {Reason}", machineId, rocReason);
            }

            return result;
        }

        public IReadOnlyList<AnomalyAnalysisResult> AnalyzeSensorData(SensorData data, int machineId = 1)
        {
            if (data == null) return Array.Empty<AnomalyAnalysisResult>();

            int mId = data.MachineId > 0 ? data.MachineId : machineId;
            var timestamp = data.Timestamp != default ? data.Timestamp : DateTime.UtcNow;

            var results = new List<AnomalyAnalysisResult>
            {
                AnalyzeReading(mId, "Temperature", data.Temperature, timestamp),
                AnalyzeReading(mId, "Pressure", data.Pressure, timestamp)
            };

            return results;
        }

        public RollingMetricsSummary? GetRollingMetrics(int machineId, string metricName)
        {
            string bufferKey = $"{machineId}:{metricName}";
            if (_buffers.TryGetValue(bufferKey, out var buffer))
            {
                var stats = buffer.GetSummary();
                return new RollingMetricsSummary
                {
                    MachineId = machineId,
                    MetricName = metricName,
                    SampleCount = stats.SampleCount,
                    WindowCapacity = _windowCapacity,
                    Mean = Math.Round(stats.Mean, 2),
                    StandardDeviation = Math.Round(stats.StdDev, 3),
                    LastValue = Math.Round(stats.LastValue, 2),
                    LastRateOfChange = Math.Round(stats.LastRoc, 2),
                    LastZScore = Math.Round(stats.LastZScore, 2),
                    LastUpdate = stats.LastTimestamp
                };
            }
            return null;
        }

        public IReadOnlyDictionary<string, RollingMetricsSummary> GetAllMetricsForMachine(int machineId)
        {
            var dict = new Dictionary<string, RollingMetricsSummary>(StringComparer.OrdinalIgnoreCase);
            foreach (var kvp in _buffers)
            {
                var parts = kvp.Key.Split(':');
                if (parts.Length == 2 && int.TryParse(parts[0], out var mId) && mId == machineId)
                {
                    var summary = GetRollingMetrics(machineId, parts[1]);
                    if (summary != null)
                    {
                        dict[parts[1]] = summary;
                    }
                }
            }
            return dict;
        }

        public void Reset(int? machineId = null)
        {
            if (machineId.HasValue)
            {
                var keysToRemove = _buffers.Keys.Where(k => k.StartsWith($"{machineId.Value}:")).ToList();
                foreach (var k in keysToRemove) _buffers.TryRemove(k, out _);
            }
            else
            {
                _buffers.Clear();
            }
        }

        #region Halka Tampon Sınıfı (Rolling Window Buffer)

        private class RollingWindowBuffer
        {
            private readonly int _capacity;
            private readonly Queue<DataPoint> _queue = new();
            private readonly object _lock = new();

            private double _lastValue = 0.0;
            private DateTime _lastTimestamp = DateTime.MinValue;
            private double _lastRoc = 0.0;
            private double _lastZScore = 0.0;

            public RollingWindowBuffer(int capacity)
            {
                _capacity = capacity;
            }

            public (double Mean, double StdDev, double Roc, int Count) AddReading(double value, DateTime timestamp)
            {
                lock (_lock)
                {
                    // Rate of Change Hesaplaması (Δx / Δt)
                    double roc = 0.0;
                    if (_lastTimestamp != DateTime.MinValue && timestamp > _lastTimestamp)
                    {
                        double dtSeconds = (timestamp - _lastTimestamp).TotalSeconds;
                        if (dtSeconds > 0.05) // En az 50ms aralık
                        {
                            roc = (value - _lastValue) / dtSeconds;
                        }
                    }

                    _lastValue = value;
                    _lastTimestamp = timestamp;
                    _lastRoc = roc;

                    // Halka bellek yönetimi
                    _queue.Enqueue(new DataPoint(value, timestamp));
                    while (_queue.Count > _capacity)
                    {
                        _queue.Dequeue();
                    }

                    int count = _queue.Count;
                    if (count == 0) return (0, 0, 0, 0);

                    // Aritmetik Ortalama (Mean - μ)
                    double sum = 0.0;
                    foreach (var dp in _queue) sum += dp.Value;
                    double mean = sum / count;

                    // Standart Sapma (Standard Deviation - σ)
                    double stdDev = 0.0;
                    if (count > 1)
                    {
                        double varianceSum = 0.0;
                        foreach (var dp in _queue)
                        {
                            double diff = dp.Value - mean;
                            varianceSum += diff * diff;
                        }
                        stdDev = Math.Sqrt(varianceSum / (count - 1));
                    }

                    if (stdDev > 0.001)
                    {
                        _lastZScore = (value - mean) / stdDev;
                    }
                    else
                    {
                        _lastZScore = 0.0;
                    }

                    return (mean, stdDev, roc, count);
                }
            }

            public (int SampleCount, double Mean, double StdDev, double LastValue, double LastRoc, double LastZScore, DateTime LastTimestamp) GetSummary()
            {
                lock (_lock)
                {
                    int count = _queue.Count;
                    if (count == 0) return (0, 0, 0, 0, 0, 0, DateTime.MinValue);

                    double sum = 0.0;
                    foreach (var dp in _queue) sum += dp.Value;
                    double mean = sum / count;

                    double stdDev = 0.0;
                    if (count > 1)
                    {
                        double varianceSum = 0.0;
                        foreach (var dp in _queue)
                        {
                            double diff = dp.Value - mean;
                            varianceSum += diff * diff;
                        }
                        stdDev = Math.Sqrt(varianceSum / (count - 1));
                    }

                    return (count, mean, stdDev, _lastValue, _lastRoc, _lastZScore, _lastTimestamp);
                }
            }

            private readonly struct DataPoint
            {
                public double Value { get; }
                public DateTime Timestamp { get; }

                public DataPoint(double value, DateTime timestamp)
                {
                    Value = value;
                    Timestamp = timestamp;
                }
            }
        }

        #endregion
    }
}
