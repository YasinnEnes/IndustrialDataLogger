using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using IndustrialDataLogger.Models.DTOs;

namespace IndustrialDataLogger.Services
{
    public interface IHierarchyService
    {
        /// <summary>
        /// Tüm Fabrika -> Üretim Hattı -> Makine ağacını ve anlık durum rozetlerini döndürür.
        /// </summary>
        Task<List<AssetTreeNodeDto>> GetAssetTreeAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Fabrika genel özetini (Toplam makine, online oranları, aktif alarmlar ve ortalama fabrika sağlık skoru) hesaplar.
        /// </summary>
        Task<FactoryOverviewDto> GetFactoryOverviewAsync(int? factoryId = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// Belirli bir üretim hattının makine ve sağlık özetini döndürür.
        /// </summary>
        Task<ProductionLineSummaryDto?> GetProductionLineSummaryAsync(int lineId, CancellationToken cancellationToken = default);
    }
}
