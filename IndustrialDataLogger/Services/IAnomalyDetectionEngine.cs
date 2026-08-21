using System;
using System.Collections.Generic;
using IndustrialDataLogger.Models;

namespace IndustrialDataLogger.Services
{
    public interface IAnomalyDetectionEngine
    {
        /// <summary>
        /// Gelen telemetri verisini analiz ederek Z-Score ve Rate of Change (Eğim) anomalilerini tespit eder.
        /// </summary>
        AnomalyAnalysisResult AnalyzeReading(int machineId, string metricName, double value, DateTime timestamp);

        /// <summary>
        /// Sensör okumasının tüm sayısal parametrelerini (Sıcaklık, Basınç vb.) toplu analiz eder.
        /// </summary>
        IReadOnlyList<AnomalyAnalysisResult> AnalyzeSensorData(SensorData data, int machineId = 1);

        /// <summary>
        /// Belirli bir makine ve metriğin hareketli pencere istatistiklerini getirir.
        /// </summary>
        RollingMetricsSummary? GetRollingMetrics(int machineId, string metricName);

        /// <summary>
        /// Belirli bir makinenin tüm metrik özetlerini getirir.
        /// </summary>
        IReadOnlyDictionary<string, RollingMetricsSummary> GetAllMetricsForMachine(int machineId);

        /// <summary>
        /// Halka belleği (ring-buffer) sıfırlar.
        /// </summary>
        void Reset(int? machineId = null);
    }

    public class AnomalyAnalysisResult
    {
        public int MachineId { get; set; }
        public string MetricName { get; set; } = string.Empty;
        public double CurrentValue { get; set; }
        public DateTime Timestamp { get; set; }

        public double Mean { get; set; }
        public double StandardDeviation { get; set; }
        public double ZScore { get; set; }
        public double RateOfChange { get; set; } // Birim zaman başına değişim (örn: °C/sn veya bar/sn)

        public bool IsZScoreAnomaly { get; set; }
        public bool IsRateOfChangeAnomaly { get; set; }
        public bool HasAnomaly => IsZScoreAnomaly || IsRateOfChangeAnomaly;

        public string AnomalyReason { get; set; } = string.Empty;
        public string AnomalyType { get; set; } = string.Empty;
    }

    public class RollingMetricsSummary
    {
        public int MachineId { get; set; }
        public string MetricName { get; set; } = string.Empty;
        public int SampleCount { get; set; }
        public int WindowCapacity { get; set; }
        public double Mean { get; set; }
        public double StandardDeviation { get; set; }
        public double LastValue { get; set; }
        public double LastRateOfChange { get; set; }
        public double LastZScore { get; set; }
        public DateTime LastUpdate { get; set; }
    }
}
