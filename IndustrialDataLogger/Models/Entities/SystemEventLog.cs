using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using IndustrialDataLogger.Enums;

namespace IndustrialDataLogger.Models.Entities
{
    [Table("systemeventlogs")]
    public class SystemEventLog
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public long Id { get; set; }

        [Required]
        [MaxLength(100)]
        public string EventType { get; set; } = string.Empty;

        [Required]
        public AlarmSeverity Severity { get; set; } = AlarmSeverity.Info;

        [Required]
        [MaxLength(500)]
        public string Description { get; set; } = string.Empty;

        [MaxLength(100)]
        public string Source { get; set; } = "System";

        [Required]
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }
}
