using System;
using System.ComponentModel.DataAnnotations;
using IndustrialDataLogger.Enums;

namespace IndustrialDataLogger.Models.DTOs
{
    public class CreateAlarmRuleDto
    {
        public int? MachineId { get; set; }

        [Required]
        [MaxLength(100)]
        public string RuleName { get; set; } = string.Empty;

        [Required]
        [MaxLength(50)]
        public string Metric { get; set; } = "Temperature";

        public ComparisonOperator Operator { get; set; } = ComparisonOperator.GreaterThan;

        [Required]
        public double Threshold { get; set; }

        public AlarmSeverity Severity { get; set; } = AlarmSeverity.Warning;

        [Required]
        [MaxLength(100)]
        public string AlarmType { get; set; } = string.Empty;

        [MaxLength(255)]
        public string MessageTemplate { get; set; } = "Eşik aşıldı: {value} (Eşik: {threshold})";

        public bool Enabled { get; set; } = true;
    }

    public class UpdateAlarmRuleDto
    {
        public int? MachineId { get; set; }

        [MaxLength(100)]
        public string? RuleName { get; set; }

        [MaxLength(50)]
        public string? Metric { get; set; }

        public ComparisonOperator? Operator { get; set; }

        public double? Threshold { get; set; }

        public AlarmSeverity? Severity { get; set; }

        [MaxLength(100)]
        public string? AlarmType { get; set; }

        [MaxLength(255)]
        public string? MessageTemplate { get; set; }

        public bool? Enabled { get; set; }
    }

    public class AlarmRuleResponseDto
    {
        public int Id { get; set; }
        public int? MachineId { get; set; }
        public string? MachineName { get; set; }
        public string RuleName { get; set; } = string.Empty;
        public string Metric { get; set; } = string.Empty;
        public string Operator { get; set; } = string.Empty;
        public double Threshold { get; set; }
        public string Severity { get; set; } = string.Empty;
        public string AlarmType { get; set; } = string.Empty;
        public string MessageTemplate { get; set; } = string.Empty;
        public bool Enabled { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }
}
