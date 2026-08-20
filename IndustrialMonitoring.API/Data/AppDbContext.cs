using IndustrialMonitoring.API.Models;
using IndustrialMonitoring.API.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace IndustrialMonitoring.API.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        public DbSet<SensorData> SensorDataSet => Set<SensorData>();
        public DbSet<User> Users { get; set; } // GÜN 3: Kullanıcı tablosu eklendi

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // GÜN 2 - SensorData Konfigürasyonu
            modelBuilder.Entity<SensorData>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.SensorName).IsRequired().HasMaxLength(100);
                entity.Property(e => e.Unit).IsRequired().HasMaxLength(20);
                entity.Property(e => e.Status).IsRequired().HasMaxLength(50);
                entity.Property(e => e.Value).HasColumnType("decimal(18,4)");
            });

            // GÜN 3 - User Entity Konfigürasyonu (Username ve Email unique olmalı)
            modelBuilder.Entity<User>()
                .HasIndex(u => u.Username)
                .IsUnique();

            modelBuilder.Entity<User>()
                .HasIndex(u => u.Email)
                .IsUnique();
        }
    }
}