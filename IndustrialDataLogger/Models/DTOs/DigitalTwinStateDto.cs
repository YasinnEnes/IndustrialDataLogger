using System;
using System.Collections.Generic;
using IndustrialDataLogger.Models.Entities;

namespace IndustrialDataLogger.Models.DTOs
{
    public class DigitalTwinStateDto
    {
        public string MachineId { get; set; } = "PLC-S7-1200-UNIT-01";
        public string MachineName { get; set; } = "Siemens S7-1200 Üretim Hattı";
        public double Temperature { get; set; }
        public double Pressure { get; set; }
        public bool MachineStatus { get; set; }
        public string PlcConnectionState { get; set; } = "Disconnected";
        public bool IsPlcConnected { get; set; }
        public DateTime LastUpdate { get; set; } = DateTime.UtcNow;
        public short ErrorCode { get; set; }

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
}
