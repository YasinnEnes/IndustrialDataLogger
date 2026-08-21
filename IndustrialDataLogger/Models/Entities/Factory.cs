using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace IndustrialDataLogger.Models.Entities
{
    [Table("factories")]
    public class Factory
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Column("id")]
        public int Id { get; set; }

        [Required]
        [MaxLength(100)]
        [Column("name")]
        public string Name { get; set; } = "Gebze Ana Akıllı Üretim Kampüsü";

        [MaxLength(100)]
        [Column("location")]
        public string Location { get; set; } = "Kocaeli / Gebze OSB";

        [MaxLength(255)]
        [Column("description")]
        public string Description { get; set; } = "Endüstri 4.0 Dijital İkiz ve Veri Toplama Merkezi";

        [Column("isactive")]
        public bool IsActive { get; set; } = true;

        [Column("createdat")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Navigation Property: 1 Factory -> Many ProductionLines
        [JsonIgnore]
        public ICollection<ProductionLine> ProductionLines { get; set; } = new List<ProductionLine>();
    }
}
