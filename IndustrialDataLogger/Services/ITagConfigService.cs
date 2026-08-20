using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using IndustrialDataLogger.Models.Entities;

namespace IndustrialDataLogger.Services
{
    public interface ITagConfigService
    {
        Task<IReadOnlyList<PlcTagConfig>> GetTagsAsync(CancellationToken cancellationToken = default);
        Task<PlcTagConfig?> GetTagByIdAsync(long id, CancellationToken cancellationToken = default);
        Task<PlcTagConfig> AddTagAsync(PlcTagConfig tag, CancellationToken cancellationToken = default);
        Task<PlcTagConfig?> UpdateTagAsync(long id, PlcTagConfig tag, CancellationToken cancellationToken = default);
        Task<bool> DeleteTagAsync(long id, CancellationToken cancellationToken = default);
        Task<int> ImportFromTiaPortalTextAsync(string sourceText, int defaultDbNumber = 1, CancellationToken cancellationToken = default);
        Task EnsureDefaultTagsSeededAsync(CancellationToken cancellationToken = default);
    }
}
