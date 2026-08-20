using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using IndustrialDataLogger.Enums;

namespace IndustrialDataLogger.Models.Entities
{
    [Table("alarmlogs")]
    public class AlarmLog
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public long Id { get; set; }

        [Required]
        [MaxLength(100)]
        public string AlarmType { get; set; } = string.Empty;

        [Required]
        public AlarmSeverity Severity { get; set; } = AlarmSeverity.Warning;

        [Required]
        public AlarmStatus Status { get; set; } = AlarmStatus.Active;

        [Required]
        [MaxLength(255)]
        public string Message { get; set; } = string.Empty;

        public double? TriggeredValue { get; set; }

        public double? ThresholdValue { get; set; }

        [Required]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? ResolvedAt { get; set; }

        public DateTime? AcknowledgedAt { get; set; }
    }
}
