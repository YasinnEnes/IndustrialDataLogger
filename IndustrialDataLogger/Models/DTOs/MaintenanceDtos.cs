using System;
using IndustrialDataLogger.Enums;

namespace IndustrialDataLogger.Models.DTOs
{
    public class CreateMaintenanceTaskDto
    {
        public int MachineId { get; set; } = 1;
        public string Component { get; set; } = "Genel Mekanik";
        public string Reason { get; set; } = string.Empty;
        public MaintenancePriority Priority { get; set; } = MaintenancePriority.Medium;
        public string? AssignedTo { get; set; }
        public long? AlarmLogId { get; set; }
    }

    public class UpdateMaintenanceTaskStatusDto
    {
        public MaintenanceStatus Status { get; set; } = MaintenanceStatus.InProgress;
        public string? AssignedTo { get; set; }
        public string? ResolutionNotes { get; set; }
    }

    public class MaintenanceTaskResponseDto
    {
        public int Id { get; set; }
        public int MachineId { get; set; }
        public string MachineCode { get; set; } = string.Empty;
        public string MachineName { get; set; } = string.Empty;
        public string Component { get; set; } = string.Empty;
        public string Reason { get; set; } = string.Empty;
        public MaintenancePriority Priority { get; set; }
        public string PriorityName => Priority.ToString();
        public MaintenanceStatus Status { get; set; }
        public string StatusName => Status.ToString();
        public string? AssignedTo { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? ResolvedAt { get; set; }
        public string? ResolutionNotes { get; set; }
        public long? AlarmLogId { get; set; }
        public double? ResolutionDurationMinutes => ResolvedAt.HasValue ? (ResolvedAt.Value - CreatedAt).TotalMinutes : null;
    }

    public class MaintenanceStatsDto
    {
        public int TotalTasks { get; set; }
        public int OpenCount { get; set; }
        public int InProgressCount { get; set; }
        public int ResolvedCount { get; set; }
        public int CriticalPriorityCount { get; set; }
        public double? AvgResolutionTimeMinutes { get; set; }
    }
}
