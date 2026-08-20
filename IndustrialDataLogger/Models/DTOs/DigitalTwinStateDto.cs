using System;
using System.Collections.Generic;
using IndustrialDataLogger.Enums;
using IndustrialDataLogger.Models.Entities;

namespace IndustrialDataLogger.Models.DTOs
{
    public class DigitalTwinStateDto
    {
        public string MachineId { get; set; } = "PLC-S7-1200-UNIT-01";
        public string MachineName { get; set; } = "Siemens S7-1200 Akıllı Üretim Ünitesi";
        public double Temperature { get; set; }
        public double Pressure { get; set; }
        public bool MachineStatus { get; set; }
        public string PlcConnectionState { get; set; } = "Disconnected";
        public bool IsPlcConnected { get; set; }
        public DateTime LastUpdate { get; set; } = DateTime.UtcNow;
        public short ErrorCode { get; set; }

        // GÜN 2 (Sprint 2.1 & 2.2): Makine Operasyonel Durumu & Sağlık Skoru
        public MachineOperationalStatus OperationalStatus { get; set; } = MachineOperationalStatus.Offline;
        public double HealthScore { get; set; } = 100.0;
        public HealthGrade HealthGrade { get; set; } = HealthGrade.Healthy;
        public HealthScoreBreakdownDto HealthBreakdown { get; set; } = new HealthScoreBreakdownDto();

        public int ActiveAlarmCount { get; set; }
        public IReadOnlyList<AlarmLog> ActiveAlarms { get; set; } = new List<AlarmLog>();

        // İstatistikler (KPI)
        public double? TemperatureMin { get; set; }
        public double? TemperatureMax { get; set; }
        public double? TemperatureAvg { get; set; }

        public double? PressureMin { get; set; }
        public double? PressureMax { get; set; }
        public double? PressureAvg { get; set; }

        public double MachineRunningRatio { get; set; }
        public long TotalLogCount { get; set; }
    }

    public class HealthScoreBreakdownDto
    {
        public double TemperatureScore { get; set; } = 25.0; // Maks: 25 (%25)
        public double PressureScore { get; set; } = 20.0;    // Maks: 20 (%20)
        public double ConnectionScore { get; set; } = 20.0;  // Maks: 20 (%20)
        public double AlarmScore { get; set; } = 35.0;       // Maks: 35 (%35)
        public double TotalScore => Math.Round(TemperatureScore + PressureScore + ConnectionScore + AlarmScore, 1);
    }
}
