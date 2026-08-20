using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace IndustrialDataLogger.Models.Entities
{
    [Table("plctagconfigs")]
    public class PlcTagConfig
    {
        [Key]
        [Column("id")]
        public long Id { get; set; }

        [Required]
        [MaxLength(100)]
        [Column("tagname")]
        public string TagName { get; set; } = string.Empty;

        [Column("dbnumber")]
        public int DbNumber { get; set; } = 1;

        [Column("byteoffset")]
        public int ByteOffset { get; set; } = 0;

        [Column("bitoffset")]
        public int BitOffset { get; set; } = 0;

        [Required]
        [MaxLength(20)]
        [Column("datatype")]
        public string DataType { get; set; } = "REAL"; // "REAL", "BOOL", "INT", "DINT", "STRING"

        [MaxLength(50)]
        [Column("unit")]
        public string Unit { get; set; } = string.Empty; // "°C", "bar", "RPM", "adet", "ms"

        [MaxLength(255)]
        [Column("description")]
        public string Description { get; set; } = string.Empty;

        [Column("iswritable")]
        public bool IsWritable { get; set; } = true;

        [Column("ismonitored")]
        public bool IsMonitored { get; set; } = true;

        [Column("createdat")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [Column("updatedat")]
        public DateTime? UpdatedAt { get; set; }

        [NotMapped]
        public string AddressString
        {
            get
            {
                if (DataType.Equals("BOOL", StringComparison.OrdinalIgnoreCase))
                    return $"DB{DbNumber}.DBX{ByteOffset}.{BitOffset}";
                if (DataType.Equals("INT", StringComparison.OrdinalIgnoreCase))
                    return $"DB{DbNumber}.DBW{ByteOffset}";
                return $"DB{DbNumber}.DBD{ByteOffset}";
            }
        }
    }
}
