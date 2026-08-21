using System;
using System.Collections.Generic;
using IndustrialDataLogger.Enums;

namespace IndustrialDataLogger.Models.DTOs
{
    public class SimulationRequestDto
    {
        public int MachineId { get; set; } = 1;
        public double TargetTemperature { get; set; } = 45.0; // °C
        public double TargetPressure { get; set; } = 5.0;       // bar
        public double MachineSpeed { get; set; } = 100.0;       // % Hız Faktörü (Örn: 100% = Nominal, 120% = Aşırı Hız)
        public int DurationMinutes { get; set; } = 60;          // Simülasyon projeksiyon süresi
        public bool SimulateFailure { get; set; } = false;
    }

    public class SimulationResultDto
    {
        public int MachineId { get; set; }
        public string MachineName { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;

        public LiveMetricsDto CurrentState { get; set; } = new();
        public PredictedMetricsDto SimulatedState { get; set; } = new();
        public List<PredictedAlarmDto> PredictedAlarms { get; set; } = new();
        public SimulationComparisonDto Comparison { get; set; } = new();
    }

    public class LiveMetricsDto
    {
        public double Temperature { get; set; }
        public double Pressure { get; set; }
        public double HealthScore { get; set; }
        public HealthGrade HealthGrade { get; set; }
        public double OeeScore { get; set; }
        public int ActiveAlarmsCount { get; set; }
        public string OperationalStatus { get; set; } = string.Empty;
    }

    public class PredictedMetricsDto
    {
        public double TargetTemperature { get; set; }
        public double TargetPressure { get; set; }
        public double MachineSpeed { get; set; }
        public double PredictedHealthScore { get; set; }
        public HealthGrade PredictedHealthGrade { get; set; }
        public HealthScoreBreakdownDto HealthBreakdown { get; set; } = new();
        public double PredictedOeeScore { get; set; }
        public double PredictedAvailability { get; set; }
        public double PredictedPerformance { get; set; }
        public double PredictedQuality { get; set; }
        public string AlarmRiskLevel { get; set; } = "LOW"; // LOW, MEDIUM, HIGH, CRITICAL
    }

    public class PredictedAlarmDto
    {
        public string RuleName { get; set; } = string.Empty;
        public string AlarmType { get; set; } = string.Empty;
        public AlarmSeverity Severity { get; set; }
        public string Metric { get; set; } = string.Empty;
        public double SimulatedValue { get; set; }
        public double Threshold { get; set; }
        public string Message { get; set; } = string.Empty;
    }

    public class SimulationComparisonDto
    {
        public double HealthScoreDelta { get; set; } // Simulated - Current
        public double OeeDelta { get; set; }         // Simulated - Current
        public string RiskAssessment { get; set; } = string.Empty;
        public List<string> Recommendations { get; set; } = new();
    }
}
