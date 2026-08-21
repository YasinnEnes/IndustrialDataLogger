using System.Threading;
using System.Threading.Tasks;
using IndustrialDataLogger.Services;
using Microsoft.AspNetCore.Mvc;

namespace IndustrialDataLogger.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class HierarchyController : ControllerBase
    {
        private readonly IHierarchyService _hierarchyService;

        public HierarchyController(IHierarchyService hierarchyService)
        {
            _hierarchyService = hierarchyService;
        }

        /// <summary>
        /// Fabrika -> Üretim Hattı -> Makine ağaç yapısını ve durum rozetlerini döndürür.
        /// </summary>
        [HttpGet("tree")]
        public async Task<IActionResult> GetAssetTree(CancellationToken cancellationToken)
        {
            var tree = await _hierarchyService.GetAssetTreeAsync(cancellationToken);
            return Ok(tree);
        }

        /// <summary>
        /// Global Fabrika Özet Metrikleri (Toplam makine, online/offline oranları, aktif alarmlar ve ortalama sağlık).
        /// </summary>
        [HttpGet("overview")]
        public async Task<IActionResult> GetFactoryOverview([FromQuery] int? factoryId, CancellationToken cancellationToken)
        {
            var overview = await _hierarchyService.GetFactoryOverviewAsync(factoryId, cancellationToken);
            return Ok(overview);
        }

        /// <summary>
        /// Belirli bir üretim hattının makine ve sağlık durumu özeti.
        /// </summary>
        [HttpGet("lines/{id:int}/summary")]
        public async Task<IActionResult> GetLineSummary(int id, CancellationToken cancellationToken)
        {
            var summary = await _hierarchyService.GetProductionLineSummaryAsync(id, cancellationToken);
            if (summary == null)
            {
                return NotFound(new { message = $"ID #{id} olan üretim hattı bulunamadı." });
            }
            return Ok(summary);
        }
    }
}
