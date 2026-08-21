using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace IndustrialDataLogger.Models.Entities
{
    [Table("machines")]
    public class Machine
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Column("id")]
        public int Id { get; set; }

        [Required]
        [MaxLength(50)]
        [Column("machinecode")]
        public string MachineCode { get; set; } = "PLC-S7-1200-UNIT-01";

        [Required]
        [MaxLength(100)]
        [Column("name")]
        public string Name { get; set; } = "Siemens S7-1200 Akıllı Üretim Ünitesi";

        [Required]
        [MaxLength(50)]
        [Column("type")]
        public string Type { get; set; } = "InjectionMolding"; // "InjectionMolding", "CNC", "Packaging", "Extruder", "RoboticCell"

        [Required]
        [MaxLength(50)]
        [Column("plcip")]
        public string PlcIp { get; set; } = "192.168.0.1";

        [Column("isactive")]
        public bool IsActive { get; set; } = true;

        [Column("productionlineid")]
        public int? ProductionLineId { get; set; }

        [Column("description")]
        [MaxLength(255)]
        public string Description { get; set; } = string.Empty;

        [Column("createdat")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [Column("updatedat")]
        public DateTime? UpdatedAt { get; set; }

        // Navigation Properties
        [ForeignKey(nameof(ProductionLineId))]
        [JsonIgnore]
        public ProductionLine? ProductionLine { get; set; }

        [JsonIgnore]
        public ICollection<SensorDataLog> SensorDataLogs { get; set; } = new List<SensorDataLog>();

        [JsonIgnore]
        public ICollection<AlarmLog> AlarmLogs { get; set; } = new List<AlarmLog>();

        [JsonIgnore]
        public ICollection<PlcTagConfig> PlcTagConfigs { get; set; } = new List<PlcTagConfig>();

        [JsonIgnore]
        public ICollection<AlarmRule> AlarmRules { get; set; } = new List<AlarmRule>();

        [JsonIgnore]
        public ICollection<MaintenanceTask> MaintenanceTasks { get; set; } = new List<MaintenanceTask>();
    }
}
