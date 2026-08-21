using System;
using System.Collections.Generic;

namespace IndustrialDataLogger.Models.DTOs
{
    /// <summary>
    /// Katlanabilir Varlık Ağacı Düğümü (Factory -> ProductionLine -> Machine)
    /// </summary>
    public class AssetTreeNodeDto
    {
        public string Id { get; set; } = string.Empty; // "f_1", "l_1", "m_1"
        public int EntityId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = "factory"; // "factory" | "line" | "machine"
        public string? Code { get; set; }
        public string Status { get; set; } = "online"; // "online" | "warning" | "critical" | "offline"
        public double? HealthScore { get; set; }
        public int ActiveAlarmCount { get; set; }
        public string? ExtraInfo { get; set; }
        public List<AssetTreeNodeDto> Children { get; set; } = new List<AssetTreeNodeDto>();
    }

    /// <summary>
    /// Global Fabrika Özet Metrikleri
    /// </summary>
    public class FactoryOverviewDto
    {
        public int TotalFactories { get; set; }
        public int TotalLines { get; set; }
        public int TotalMachines { get; set; }
        public int OnlineMachines { get; set; }
        public int OfflineMachines { get; set; }
        public double OnlineRatioPercentage { get; set; } // Örn: 100.0%
        public int TotalActiveAlarms { get; set; }
        public int CriticalAlarmsCount { get; set; }
        public int WarningAlarmsCount { get; set; }
        public double OverallFactoryHealthScore { get; set; } // 0 - 100%
        public string OverallHealthStatus { get; set; } = "Good"; // "Excellent" | "Good" | "Warning" | "Critical"
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public List<ProductionLineSummaryDto> LineSummaries { get; set; } = new List<ProductionLineSummaryDto>();
    }

    /// <summary>
    /// Üretim Hattı Detaylı Özet DTO
    /// </summary>
    public class ProductionLineSummaryDto
    {
        public int LineId { get; set; }
        public int FactoryId { get; set; }
        public string FactoryName { get; set; } = string.Empty;
        public string LineName { get; set; } = string.Empty;
        public string LineCode { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public int MachineCount { get; set; }
        public int OnlineMachineCount { get; set; }
        public double AverageHealthScore { get; set; }
        public int ActiveAlarmCount { get; set; }
        public string Status { get; set; } = "Normal";
    }
}
