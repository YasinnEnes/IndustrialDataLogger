using IndustrialDataLogger.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace IndustrialDataLogger.Data
{
    public class IndustrialDbContext : DbContext
    {
        public IndustrialDbContext(DbContextOptions<IndustrialDbContext> options) : base(options)
        {
        }

        public DbSet<SensorDataLog> SensorDataLogs { get; set; } = null!;
        public DbSet<AlarmLog> Alarms { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<SensorDataLog>(entity =>
            {
                entity.ToTable("sensordata");

                entity.HasKey(e => e.Id);

                // Sprint 2.5: Zaman serisi (Time-series) sorguları için Timestamp indekslemesi
                entity.HasIndex(e => e.Timestamp)
                      .HasDatabaseName("IX_sensordata_timestamp");

                entity.Property(e => e.Id).HasColumnName("id");
                entity.Property(e => e.Timestamp).HasColumnName("timestamp").IsRequired();
                entity.Property(e => e.Temperature).HasColumnName("temperature").IsRequired();
                entity.Property(e => e.Pressure).HasColumnName("pressure").IsRequired();
                entity.Property(e => e.MachineStatus).HasColumnName("machinestatus").IsRequired();
                entity.Property(e => e.ErrorCode).HasColumnName("errorcode").HasDefaultValue((short)0);
            });

            // GÜN 4: AlarmLog tablosu ve indeksleri
            modelBuilder.Entity<AlarmLog>(entity =>
            {
                entity.ToTable("alarmlogs");

                entity.HasKey(e => e.Id);

                entity.HasIndex(e => e.CreatedAt)
                      .HasDatabaseName("IX_alarmlogs_createdat");

                entity.HasIndex(e => e.Status)
                      .HasDatabaseName("IX_alarmlogs_status");

                entity.Property(e => e.Id).HasColumnName("id");
                entity.Property(e => e.AlarmType).HasColumnName("alarmtype").IsRequired().HasMaxLength(100);
                entity.Property(e => e.Severity).HasColumnName("severity").IsRequired();
                entity.Property(e => e.Status).HasColumnName("status").IsRequired();
                entity.Property(e => e.Message).HasColumnName("message").IsRequired().HasMaxLength(255);
                entity.Property(e => e.TriggeredValue).HasColumnName("triggeredvalue");
                entity.Property(e => e.ThresholdValue).HasColumnName("thresholdvalue");
                entity.Property(e => e.CreatedAt).HasColumnName("createdat").IsRequired();
                entity.Property(e => e.ResolvedAt).HasColumnName("resolvedat");
                entity.Property(e => e.AcknowledgedAt).HasColumnName("acknowledgedat");
            });
        }
    }
}
