using System;
using System.Threading;
using System.Threading.Tasks;
using IndustrialDataLogger.Enums;
using IndustrialDataLogger.Models;
using IndustrialDataLogger.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace IndustrialDataLogger.Tests.UnitTests
{
    public class AnomalyDetectionTests
    {
        [Fact]
        public void RollingWindow_CalculatesAccurateMeanAndStandardDeviation()
        {
            // Arrange: 10 adet stabil sıcaklık verisi (40.0 ± 0.5°C)
            var engine = new AnomalyDetectionEngine(NullLogger<AnomalyDetectionEngine>.Instance, windowCapacity: 30, zScoreThreshold: 2.58);
            var now = DateTime.UtcNow;

            for (int i = 0; i < 10; i++)
            {
                engine.AnalyzeReading(1, "Temperature", 40.0 + (i % 2 == 0 ? 0.2 : -0.2), now.AddSeconds(i));
            }

            // Act
            var stats = engine.GetRollingMetrics(1, "Temperature");

            // Assert
            Assert.NotNull(stats);
            Assert.Equal(10, stats!.SampleCount);
            Assert.InRange(stats.Mean, 39.8, 40.2);
            Assert.True(stats.StandardDeviation > 0.1 && stats.StandardDeviation < 0.3);
        }

        [Fact]
        public void SuddenTemperatureJump_TriggersZScoreAnomaly()
        {
            // Arrange: 20 adet stabil sıcaklık verisi (40°C civarında çok dar sapma)
            var engine = new AnomalyDetectionEngine(NullLogger<AnomalyDetectionEngine>.Instance, windowCapacity: 30, zScoreThreshold: 2.58);
            var now = DateTime.UtcNow;

            for (int i = 0; i < 20; i++)
            {
                engine.AnalyzeReading(1, "Temperature", 40.0 + ((i % 3) * 0.1), now.AddSeconds(i));
            }

            // Act: Statik sınır (80°C) henüz aşılmamış olsa bile, aniden 55.0°C'ye sıçrama
            var anomalyResult = engine.AnalyzeReading(1, "Temperature", 55.0, now.AddSeconds(21));

            // Assert: Z-Score 3.0'ın çok üzerine çıkmalı ve istatistiksel anomali tetiklenmelidir
            Assert.True(anomalyResult.IsZScoreAnomaly, $"Z-Score anomalisi tetiklenmeliydi. Z={anomalyResult.ZScore}, Ortalama={anomalyResult.Mean}, Std={anomalyResult.StandardDeviation}");
            Assert.True(anomalyResult.ZScore > 3.0);
            Assert.True(anomalyResult.HasAnomaly);
            Assert.Contains("Z-Score Sapması", anomalyResult.AnomalyReason);
        }

        [Fact]
        public void RapidRateOfChange_TriggersRateOfChangeAnomaly()
        {
            // Arrange: 10 adet veri
            var engine = new AnomalyDetectionEngine(NullLogger<AnomalyDetectionEngine>.Instance, windowCapacity: 30);
            var now = DateTime.UtcNow;

            for (int i = 0; i < 10; i++)
            {
                engine.AnalyzeReading(1, "Temperature", 40.0, now.AddSeconds(i));
            }

            // Act: 1 saniyede 40.0°C -> 45.0°C ani tırmanış (RoC = 5.0°C/sn, Eşik: 2.5°C/sn)
            var result = engine.AnalyzeReading(1, "Temperature", 45.0, now.AddSeconds(10));

            // Assert
            Assert.True(result.IsRateOfChangeAnomaly, $"Eğim anomalisi tetiklenmeliydi. RoC={result.RateOfChange}°C/sn");
            Assert.True(result.HasAnomaly);
            Assert.Contains("Ani Değişim Hızı", result.AnomalyReason);
        }

        [Fact]
        public void StableNominalTelemetry_ProducesNoAnomaly()
        {
            // Arrange: Kararlı ve normal dalgalanan veri
            var engine = new AnomalyDetectionEngine(NullLogger<AnomalyDetectionEngine>.Instance, windowCapacity: 30, zScoreThreshold: 2.58);
            var now = DateTime.UtcNow;

            for (int i = 0; i < 15; i++)
            {
                engine.AnalyzeReading(1, "Pressure", 4.5 + (Math.Sin(i) * 0.2), now.AddSeconds(i));
            }

            // Act: Normal aralıkta yeni okuma
            var result = engine.AnalyzeReading(1, "Pressure", 4.6, now.AddSeconds(16));

            // Assert
            Assert.False(result.IsZScoreAnomaly);
            Assert.False(result.IsRateOfChangeAnomaly);
            Assert.False(result.HasAnomaly);
        }

        [Fact]
        public void AnalyzeSensorData_EvaluatesBothTemperatureAndPressure()
        {
            // Arrange
            var engine = new AnomalyDetectionEngine(NullLogger<AnomalyDetectionEngine>.Instance);
            var data = new SensorData
            {
                MachineId = 1,
                Temperature = 45.0,
                Pressure = 5.0,
                Timestamp = DateTime.UtcNow
            };

            // Act
            var results = engine.AnalyzeSensorData(data, 1);

            // Assert
            Assert.Equal(2, results.Count);
            Assert.Contains(results, r => r.MetricName == "Temperature");
            Assert.Contains(results, r => r.MetricName == "Pressure");
        }

        [Fact]
        public void Reset_ClearsStoredRollingBuffers()
        {
            // Arrange
            var engine = new AnomalyDetectionEngine(NullLogger<AnomalyDetectionEngine>.Instance);
            engine.AnalyzeReading(1, "Temperature", 45.0, DateTime.UtcNow);

            // Act
            engine.Reset(1);
            var stats = engine.GetRollingMetrics(1, "Temperature");

            // Assert
            Assert.Null(stats);
        }
    }
}
