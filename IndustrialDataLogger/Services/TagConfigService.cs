using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using IndustrialDataLogger.Data;
using IndustrialDataLogger.Models.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace IndustrialDataLogger.Services
{
    public class TagConfigService : ITagConfigService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<TagConfigService> _logger;

        public TagConfigService(IServiceScopeFactory scopeFactory, ILogger<TagConfigService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        public async Task<IReadOnlyList<PlcTagConfig>> GetTagsAsync(CancellationToken cancellationToken = default)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IndustrialDbContext>();
            await EnsureDefaultTagsSeededInternalAsync(db, cancellationToken);
            return await db.PlcTags.AsNoTracking().OrderBy(t => t.DbNumber).ThenBy(t => t.ByteOffset).ThenBy(t => t.BitOffset).ToListAsync(cancellationToken);
        }

        public async Task<PlcTagConfig?> GetTagByIdAsync(long id, CancellationToken cancellationToken = default)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IndustrialDbContext>();
            return await db.PlcTags.FindAsync(new object[] { id }, cancellationToken);
        }

        public async Task<PlcTagConfig> AddTagAsync(PlcTagConfig tag, CancellationToken cancellationToken = default)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IndustrialDbContext>();
            tag.CreatedAt = DateTime.UtcNow;
            tag.DataType = NormalizeDataType(tag.DataType);
            db.PlcTags.Add(tag);
            await db.SaveChangesAsync(cancellationToken);
            return tag;
        }

        public async Task<PlcTagConfig?> UpdateTagAsync(long id, PlcTagConfig tag, CancellationToken cancellationToken = default)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IndustrialDbContext>();
            var existing = await db.PlcTags.FindAsync(new object[] { id }, cancellationToken);
            if (existing == null) return null;

            existing.TagName = tag.TagName;
            existing.DbNumber = tag.DbNumber;
            existing.ByteOffset = tag.ByteOffset;
            existing.BitOffset = tag.BitOffset;
            existing.DataType = NormalizeDataType(tag.DataType);
            existing.Unit = tag.Unit;
            existing.Description = tag.Description;
            existing.IsWritable = tag.IsWritable;
            existing.IsMonitored = tag.IsMonitored;
            existing.UpdatedAt = DateTime.UtcNow;

            await db.SaveChangesAsync(cancellationToken);
            return existing;
        }

        public async Task<bool> DeleteTagAsync(long id, CancellationToken cancellationToken = default)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IndustrialDbContext>();
            var existing = await db.PlcTags.FindAsync(new object[] { id }, cancellationToken);
            if (existing == null) return false;

            db.PlcTags.Remove(existing);
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }

        public async Task EnsureDefaultTagsSeededAsync(CancellationToken cancellationToken = default)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IndustrialDbContext>();
            await EnsureDefaultTagsSeededInternalAsync(db, cancellationToken);
        }

        private async Task EnsureDefaultTagsSeededInternalAsync(IndustrialDbContext db, CancellationToken cancellationToken)
        {
            if (!await db.PlcTags.AnyAsync(cancellationToken))
            {
                var defaults = new List<PlcTagConfig>
                {
                    new PlcTagConfig
                    {
                        TagName = "Temperature",
                        DbNumber = 1,
                        ByteOffset = 0,
                        BitOffset = 0,
                        DataType = "REAL",
                        Unit = "°C",
                        Description = "Sıcaklık Sensörü (TIA Portal DB1.DBD0)",
                        IsWritable = true,
                        IsMonitored = true,
                        CreatedAt = DateTime.UtcNow
                    },
                    new PlcTagConfig
                    {
                        TagName = "Pressure",
                        DbNumber = 1,
                        ByteOffset = 4,
                        BitOffset = 0,
                        DataType = "REAL",
                        Unit = "bar",
                        Description = "Basınç Sensörü (TIA Portal DB1.DBD4)",
                        IsWritable = true,
                        IsMonitored = true,
                        CreatedAt = DateTime.UtcNow
                    },
                    new PlcTagConfig
                    {
                        TagName = "MachineStatus",
                        DbNumber = 1,
                        ByteOffset = 8,
                        BitOffset = 0,
                        DataType = "BOOL",
                        Unit = "",
                        Description = "Makine Çalışma Durumu (TIA Portal DB1.DBX8.0)",
                        IsWritable = true,
                        IsMonitored = true,
                        CreatedAt = DateTime.UtcNow
                    }
                };

                db.PlcTags.AddRange(defaults);
                await db.SaveChangesAsync(cancellationToken);
                _logger.LogInformation("TIA Portal DB1 varsayılan değişkenleri (Temperature, Pressure, MachineStatus) plctagconfigs tablosuna eklendi.");
            }
        }

        public async Task<int> ImportFromTiaPortalTextAsync(string sourceText, int defaultDbNumber = 1, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(sourceText)) return 0;

            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IndustrialDbContext>();

            var lines = sourceText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            int addedCount = 0;

            // Örnek formatlar:
            // 1) Temperature Real 0.0
            // 2) "Temperature" : Real;
            // 3) Temperature | Real | 0.0
            // 4) MachineStatus Bool 8.0

            var rowRegex = new Regex(@"(?:\""([a-zA-Z0-9_]+)\""|([a-zA-Z0-9_]+))\s*[:\|\t\s]+\s*(Real|Bool|Int|DInt|String|Word|DWord|Byte)(?:\s*[:\|\t\s]+\s*([0-9]+(?:\.[0-9]+)?))?", RegexOptions.IgnoreCase);

            foreach (var rawLine in lines)
            {
                var line = rawLine.Trim();
                if (string.IsNullOrEmpty(line) || line.StartsWith("//") || line.StartsWith("DATA_BLOCK") || line.StartsWith("STRUCT") || line.StartsWith("END_STRUCT"))
                    continue;

                var match = rowRegex.Match(line);
                if (match.Success)
                {
                    string tagName = !string.IsNullOrEmpty(match.Groups[1].Value) ? match.Groups[1].Value : match.Groups[2].Value;
                    string dataType = NormalizeDataType(match.Groups[3].Value);
                    string offsetStr = match.Groups[4].Value;

                    int byteOffset = 0;
                    int bitOffset = 0;

                    if (!string.IsNullOrEmpty(offsetStr))
                    {
                        var parts = offsetStr.Split('.');
                        int.TryParse(parts[0], out byteOffset);
                        if (parts.Length > 1) int.TryParse(parts[1], out bitOffset);
                    }

                    // Eğer veritabanında aynı isim veya adreste varsa güncelle, yoksa ekle
                    var existing = await db.PlcTags.FirstOrDefaultAsync(t => t.DbNumber == defaultDbNumber && t.TagName.ToLower() == tagName.ToLower(), cancellationToken);
                    if (existing != null)
                    {
                        existing.ByteOffset = byteOffset;
                        existing.BitOffset = bitOffset;
                        existing.DataType = dataType;
                        existing.UpdatedAt = DateTime.UtcNow;
                    }
                    else
                    {
                        db.PlcTags.Add(new PlcTagConfig
                        {
                            TagName = tagName,
                            DbNumber = defaultDbNumber,
                            ByteOffset = byteOffset,
                            BitOffset = bitOffset,
                            DataType = dataType,
                            Unit = GuessUnit(tagName, dataType),
                            Description = $"TIA Portal DB{defaultDbNumber} İçe Aktarıldı",
                            IsWritable = true,
                            IsMonitored = true,
                            CreatedAt = DateTime.UtcNow
                        });
                        addedCount++;
                    }
                }
            }

            await db.SaveChangesAsync(cancellationToken);
            return addedCount;
        }

        private static string NormalizeDataType(string rawType)
        {
            var upper = rawType?.Trim().ToUpperInvariant() ?? "REAL";
            if (upper.Contains("REAL") || upper.Contains("FLOAT")) return "REAL";
            if (upper.Contains("BOOL") || upper.Contains("BIT")) return "BOOL";
            if (upper.Contains("DINT") || upper.Contains("DWORD") || upper.Contains("INT32")) return "DINT";
            if (upper.Contains("INT") || upper.Contains("WORD") || upper.Contains("INT16") || upper.Contains("SHORT")) return "INT";
            if (upper.Contains("STRING") || upper.Contains("CHAR")) return "STRING";
            return "REAL";
        }

        private static string GuessUnit(string tagName, string dataType)
        {
            var lower = tagName.ToLower();
            if (lower.Contains("temp") || lower.Contains("sicak")) return "°C";
            if (lower.Contains("press") || lower.Contains("basinc")) return "bar";
            if (lower.Contains("speed") || lower.Contains("hiz") || lower.Contains("rpm")) return "RPM";
            if (lower.Contains("level") || lower.Contains("seviye")) return "%";
            if (lower.Contains("count") || lower.Contains("adet") || lower.Contains("sayac")) return "adet";
            if (lower.Contains("time") || lower.Contains("sure")) return "s";
            return "";
        }
    }
}
