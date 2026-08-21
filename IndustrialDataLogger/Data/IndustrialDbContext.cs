using IndustrialDataLogger.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace IndustrialDataLogger.Data
{
    public class IndustrialDbContext : DbContext
    {
        public IndustrialDbContext(DbContextOptions<IndustrialDbContext> options) : base(options)
        {
        }

        public DbSet<Factory> Factories { get; set; } = null!;
        public DbSet<ProductionLine> ProductionLines { get; set; } = null!;
        public DbSet<Machine> Machines { get; set; } = null!;
        public DbSet<SensorDataLog> SensorDataLogs { get; set; } = null!;
        public DbSet<AlarmLog> Alarms { get; set; } = null!;
        public DbSet<AlarmRule> AlarmRules { get; set; } = null!;
        public DbSet<SystemEventLog> SystemEvents { get; set; } = null!;
        public DbSet<PlcTagConfig> PlcTags { get; set; } = null!;
        public DbSet<MaintenanceTask> MaintenanceTasks { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // 0.1 Factory Tablosu
            modelBuilder.Entity<Factory>(entity =>
            {
                entity.ToTable("factories");
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Id).HasColumnName("id");
                entity.Property(e => e.Name).HasColumnName("name").IsRequired().HasMaxLength(100);
                entity.Property(e => e.Location).HasColumnName("location").HasMaxLength(100);
                entity.Property(e => e.Description).HasColumnName("description").HasMaxLength(255);
                entity.Property(e => e.IsActive).HasColumnName("isactive").HasDefaultValue(true);
                entity.Property(e => e.CreatedAt).HasColumnName("createdat").IsRequired();

                entity.HasMany(e => e.ProductionLines)
                      .WithOne(p => p.Factory)
                      .HasForeignKey(p => p.FactoryId)
                      .OnDelete(DeleteBehavior.Cascade);
            });

            // 0.2 ProductionLine Tablosu
            modelBuilder.Entity<ProductionLine>(entity =>
            {
                entity.ToTable("productionlines");
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Id).HasColumnName("id");
                entity.Property(e => e.FactoryId).HasColumnName("factoryid");
                entity.Property(e => e.Name).HasColumnName("name").IsRequired().HasMaxLength(100);
                entity.Property(e => e.LineCode).HasColumnName("linecode").IsRequired().HasMaxLength(50);
                entity.Property(e => e.Description).HasColumnName("description").HasMaxLength(255);
                entity.Property(e => e.IsActive).HasColumnName("isactive").HasDefaultValue(true);
                entity.Property(e => e.CreatedAt).HasColumnName("createdat").IsRequired();

                entity.HasIndex(e => e.LineCode).HasDatabaseName("IX_productionlines_linecode");
                entity.HasIndex(e => e.FactoryId).HasDatabaseName("IX_productionlines_factoryid");

                entity.HasMany(e => e.Machines)
                      .WithOne(m => m.ProductionLine)
                      .HasForeignKey(m => m.ProductionLineId)
                      .OnDelete(DeleteBehavior.SetNull);
            });

            // 1. Machine Tablosu & İndeksleri
            modelBuilder.Entity<Machine>(entity =>
            {
                entity.ToTable("machines");
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Id).HasColumnName("id");
                entity.Property(e => e.ProductionLineId).HasColumnName("productionlineid");
                entity.Property(e => e.MachineCode).HasColumnName("machinecode").IsRequired().HasMaxLength(50);
                entity.Property(e => e.Name).HasColumnName("name").IsRequired().HasMaxLength(100);
                entity.Property(e => e.Type).HasColumnName("type").IsRequired().HasMaxLength(50);
                entity.Property(e => e.PlcIp).HasColumnName("plcip").IsRequired().HasMaxLength(50);
                entity.Property(e => e.IsActive).HasColumnName("isactive").HasDefaultValue(true);
                entity.Property(e => e.Description).HasColumnName("description").HasMaxLength(255);
                entity.Property(e => e.CreatedAt).HasColumnName("createdat").IsRequired();
                entity.Property(e => e.UpdatedAt).HasColumnName("updatedat");

                entity.HasIndex(e => e.MachineCode)
                      .IsUnique()
                      .HasDatabaseName("IX_machines_machinecode");

                entity.HasIndex(e => e.ProductionLineId)
                      .HasDatabaseName("IX_machines_productionlineid");

                // Relationships
                entity.HasMany(e => e.SensorDataLogs)
                      .WithOne(s => s.Machine)
                      .HasForeignKey(s => s.MachineId)
                      .OnDelete(DeleteBehavior.Cascade);

                entity.HasMany(e => e.AlarmLogs)
                      .WithOne(a => a.Machine)
                      .HasForeignKey(a => a.MachineId)
                      .OnDelete(DeleteBehavior.Cascade);

                entity.HasMany(e => e.PlcTagConfigs)
                      .WithOne(t => t.Machine)
                      .HasForeignKey(t => t.MachineId)
                      .OnDelete(DeleteBehavior.Cascade);

                entity.HasMany(e => e.AlarmRules)
                      .WithOne(r => r.Machine)
                      .HasForeignKey(r => r.MachineId)
                      .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<PlcTagConfig>(entity =>
            {
                entity.ToTable("plctagconfigs");
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Id).HasColumnName("id");
                entity.Property(e => e.MachineId).HasColumnName("machineid").HasDefaultValue(1);
                entity.Property(e => e.TagName).HasColumnName("tagname").IsRequired().HasMaxLength(100);
                entity.Property(e => e.DbNumber).HasColumnName("dbnumber").IsRequired();
                entity.Property(e => e.ByteOffset).HasColumnName("byteoffset").IsRequired();
                entity.Property(e => e.BitOffset).HasColumnName("bitoffset").IsRequired();
                entity.Property(e => e.DataType).HasColumnName("datatype").IsRequired().HasMaxLength(20);
                entity.Property(e => e.Unit).HasColumnName("unit").HasMaxLength(50);
                entity.Property(e => e.Description).HasColumnName("description").HasMaxLength(255);
                entity.Property(e => e.IsWritable).HasColumnName("iswritable");
                entity.Property(e => e.IsMonitored).HasColumnName("ismonitored");
                entity.Property(e => e.CreatedAt).HasColumnName("createdat").IsRequired();
                entity.Property(e => e.UpdatedAt).HasColumnName("updatedat");

                entity.HasIndex(e => e.MachineId)
                      .HasDatabaseName("IX_plctagconfigs_machineid");
            });

            modelBuilder.Entity<SensorDataLog>(entity =>
            {
                entity.ToTable("sensordata");

                entity.HasKey(e => e.Id);

                // Sprint 2.5: Zaman serisi (Time-series) sorguları için Timestamp indekslemesi
                entity.HasIndex(e => e.Timestamp)
                      .HasDatabaseName("IX_sensordata_timestamp");

                entity.HasIndex(e => e.MachineId)
                      .HasDatabaseName("IX_sensordata_machineid");

                entity.Property(e => e.Id).HasColumnName("id");
                entity.Property(e => e.MachineId).HasColumnName("machineid").HasDefaultValue(1);
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

                entity.HasIndex(e => e.MachineId)
                      .HasDatabaseName("IX_alarmlogs_machineid");

                entity.Property(e => e.Id).HasColumnName("id");
                entity.Property(e => e.MachineId).HasColumnName("machineid").HasDefaultValue(1);
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

            // GÜN 3: SystemEventLog tablosu ve indeksleri
            modelBuilder.Entity<SystemEventLog>(entity =>
            {
                entity.ToTable("systemeventlogs");

                entity.HasKey(e => e.Id);

                entity.HasIndex(e => e.Timestamp)
                      .HasDatabaseName("IX_systemeventlogs_timestamp");

                entity.HasIndex(e => e.MachineId)
                      .HasDatabaseName("IX_systemeventlogs_machineid");

                entity.Property(e => e.Id).HasColumnName("id");
                entity.Property(e => e.MachineId).HasColumnName("machineid");
                entity.Property(e => e.EventType).HasColumnName("eventtype").IsRequired().HasMaxLength(100);
                entity.Property(e => e.Severity).HasColumnName("severity").IsRequired();
                entity.Property(e => e.Description).HasColumnName("description").IsRequired().HasMaxLength(500);
                entity.Property(e => e.Source).HasColumnName("source").HasMaxLength(100);
                entity.Property(e => e.Timestamp).HasColumnName("timestamp").IsRequired();
            });

            // GÜN 4 / Sprint 3.6: AlarmRule tablosu ve indeksleri (Konfigüre Edilebilir Kural Motoru)
            modelBuilder.Entity<AlarmRule>(entity =>
            {
                entity.ToTable("alarmrules");

                entity.HasKey(e => e.Id);

                entity.HasIndex(e => e.MachineId)
                      .HasDatabaseName("IX_alarmrules_machineid");

                entity.HasIndex(e => e.Metric)
                      .HasDatabaseName("IX_alarmrules_metric");

                entity.HasIndex(e => e.Enabled)
                      .HasDatabaseName("IX_alarmrules_enabled");

                entity.Property(e => e.Id).HasColumnName("id");
                entity.Property(e => e.MachineId).HasColumnName("machineid");
                entity.Property(e => e.RuleName).HasColumnName("rulename").IsRequired().HasMaxLength(100);
                entity.Property(e => e.Metric).HasColumnName("metric").IsRequired().HasMaxLength(50);
                entity.Property(e => e.Operator).HasColumnName("operator").IsRequired();
                entity.Property(e => e.Threshold).HasColumnName("threshold").IsRequired();
                entity.Property(e => e.Severity).HasColumnName("severity").IsRequired();
                entity.Property(e => e.AlarmType).HasColumnName("alarmtype").IsRequired().HasMaxLength(100);
                entity.Property(e => e.Enabled).HasColumnName("enabled").HasDefaultValue(true);
                entity.Property(e => e.CreatedAt).HasColumnName("createdat").IsRequired();
                entity.Property(e => e.UpdatedAt).HasColumnName("updatedat");
            });

            // 7. MaintenanceTask Tablosu & İndeksleri
            modelBuilder.Entity<MaintenanceTask>(entity =>
            {
                entity.ToTable("maintenancetasks");
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Id).HasColumnName("id");
                entity.Property(e => e.MachineId).HasColumnName("machineid");
                entity.Property(e => e.Component).HasColumnName("component").IsRequired().HasMaxLength(100);
                entity.Property(e => e.Reason).HasColumnName("reason").IsRequired().HasMaxLength(500);
                entity.Property(e => e.Priority).HasColumnName("priority").IsRequired();
                entity.Property(e => e.Status).HasColumnName("status").IsRequired();
                entity.Property(e => e.AssignedTo).HasColumnName("assignedto").HasMaxLength(100);
                entity.Property(e => e.CreatedAt).HasColumnName("createdat").IsRequired();
                entity.Property(e => e.ResolvedAt).HasColumnName("resolvedat");
                entity.Property(e => e.ResolutionNotes).HasColumnName("resolutionnotes").HasMaxLength(1000);
                entity.Property(e => e.AlarmLogId).HasColumnName("alarmlogid");

                entity.HasIndex(e => e.MachineId).HasDatabaseName("IX_maintenancetasks_machineid");
                entity.HasIndex(e => e.Status).HasDatabaseName("IX_maintenancetasks_status");
                entity.HasIndex(e => e.Priority).HasDatabaseName("IX_maintenancetasks_priority");

                entity.HasOne(e => e.Machine)
                      .WithMany()
                      .HasForeignKey(e => e.MachineId)
                      .OnDelete(DeleteBehavior.Cascade);
            });
        }
    }
}
