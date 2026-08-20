using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace IndustrialDataLogger.Models.Entities
{
    [Table("sensordata")]
    public class SensorDataLog
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public long Id { get; set; }

        [Required]
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;

        [Required]
        public double Temperature { get; set; }

        [Required]
        public double Pressure { get; set; }

        [Required]
        public bool MachineStatus { get; set; }

        public short ErrorCode { get; set; } = 0;
    }
}
