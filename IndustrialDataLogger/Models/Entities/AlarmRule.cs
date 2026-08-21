using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using IndustrialDataLogger.Enums;

namespace IndustrialDataLogger.Models.Entities
{
    [Table("alarmrules")]
    public class AlarmRule
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        [Column("machineid")]
        public int? MachineId { get; set; }

        [ForeignKey(nameof(MachineId))]
        public Machine? Machine { get; set; }

        [Required]
        [MaxLength(100)]
        public string RuleName { get; set; } = string.Empty;

        [Required]
        [MaxLength(50)]
        public string Metric { get; set; } = "Temperature";

        [Required]
        public ComparisonOperator Operator { get; set; } = ComparisonOperator.GreaterThan;

        [Required]
        public double Threshold { get; set; }

        [Required]
        public AlarmSeverity Severity { get; set; } = AlarmSeverity.Warning;

        [Required]
        [MaxLength(100)]
        public string AlarmType { get; set; } = string.Empty;

        [Required]
        [MaxLength(255)]
        public string MessageTemplate { get; set; } = "Eşik değeri aşıldı: {value} (Eşik: {threshold})";

        public bool Enabled { get; set; } = true;

        [Required]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? UpdatedAt { get; set; }
    }
}
