using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using IndustrialDataLogger.Enums;

namespace IndustrialDataLogger.Models.Entities
{
    [Table("maintenancetasks")]
    public class MaintenanceTask
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Column("id")]
        public int Id { get; set; }

        [Column("machineid")]
        public int MachineId { get; set; }

        [ForeignKey(nameof(MachineId))]
        public Machine? Machine { get; set; }

        [Required]
        [MaxLength(100)]
        [Column("component")]
        public string Component { get; set; } = "Genel Mekanik";

        [Required]
        [MaxLength(500)]
        [Column("reason")]
        public string Reason { get; set; } = string.Empty;

        [Required]
        [Column("priority")]
        public MaintenancePriority Priority { get; set; } = MaintenancePriority.Medium;

        [Required]
        [Column("status")]
        public MaintenanceStatus Status { get; set; } = MaintenanceStatus.Open;

        [MaxLength(100)]
        [Column("assignedto")]
        public string? AssignedTo { get; set; }

        [Required]
        [Column("createdat")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [Column("resolvedat")]
        public DateTime? ResolvedAt { get; set; }

        [MaxLength(1000)]
        [Column("resolutionnotes")]
        public string? ResolutionNotes { get; set; }

        [Column("alarmlogid")]
        public long? AlarmLogId { get; set; }

        [ForeignKey(nameof(AlarmLogId))]
        public AlarmLog? AlarmLog { get; set; }
    }
}
