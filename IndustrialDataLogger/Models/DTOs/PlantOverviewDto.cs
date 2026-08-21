using System;
using System.Collections.Generic;

namespace IndustrialDataLogger.Models.DTOs
{
    public class PlantOverviewDto
    {
        public string PlantName { get; set; } = "Industrial Smart Factory Line 1";
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public int TotalMachines { get; set; }
        public int ActiveMachines { get; set; }
        public int RunningMachines { get; set; }
        public int FaultedMachines { get; set; }
        public int OfflineMachines { get; set; }
        public double AverageHealthScore { get; set; }
        public double AverageOee { get; set; }
        public int TotalActiveAlarms { get; set; }
        public List<DigitalTwinStateDto> Machines { get; set; } = new List<DigitalTwinStateDto>();
    }
}
