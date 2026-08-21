using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace IndustrialDataLogger.Models.Entities
{
    [Table("productionlines")]
    public class ProductionLine
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Column("id")]
        public int Id { get; set; }

        [Required]
        [Column("factoryid")]
        public int FactoryId { get; set; }

        [Required]
        [MaxLength(100)]
        [Column("name")]
        public string Name { get; set; } = "Montaj & İşleme Hattı A";

        [Required]
        [MaxLength(50)]
        [Column("linecode")]
        public string LineCode { get; set; } = "LINE-A";

        [MaxLength(255)]
        [Column("description")]
        public string Description { get; set; } = "Enjeksiyon kalıplama ve CNC işleme istasyonları";

        [Column("isactive")]
        public bool IsActive { get; set; } = true;

        [Column("createdat")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Navigation Properties
        [ForeignKey(nameof(FactoryId))]
        [JsonIgnore]
        public Factory? Factory { get; set; }

        [JsonIgnore]
        public ICollection<Machine> Machines { get; set; } = new List<Machine>();
    }
}
