using System;
using System.Threading;
using System.Threading.Tasks;
using IndustrialDataLogger.Models.Entities;
using IndustrialDataLogger.Services;
using Microsoft.AspNetCore.Mvc;

namespace IndustrialDataLogger.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class TagsController : ControllerBase
    {
        private readonly ITagConfigService _tagConfigService;

        public TagsController(ITagConfigService tagConfigService)
        {
            _tagConfigService = tagConfigService;
        }

        [HttpGet]
        public async Task<IActionResult> GetTags(CancellationToken cancellationToken)
        {
            var tags = await _tagConfigService.GetTagsAsync(cancellationToken);
            return Ok(tags);
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetTagById(long id, CancellationToken cancellationToken)
        {
            var tag = await _tagConfigService.GetTagByIdAsync(id, cancellationToken);
            if (tag == null) return NotFound(new { message = $"ID {id} olan değişken bulunamadı." });
            return Ok(tag);
        }

        [HttpPost]
        public async Task<IActionResult> AddTag([FromBody] PlcTagConfig tag, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(tag.TagName))
            {
                return BadRequest(new { message = "Değişken adı (TagName) zorunludur." });
            }

            var created = await _tagConfigService.AddTagAsync(tag, cancellationToken);
            return CreatedAtAction(nameof(GetTagById), new { id = created.Id }, created);
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateTag(long id, [FromBody] PlcTagConfig tag, CancellationToken cancellationToken)
        {
            var updated = await _tagConfigService.UpdateTagAsync(id, tag, cancellationToken);
            if (updated == null) return NotFound(new { message = $"ID {id} olan değişken bulunamadı." });
            return Ok(updated);
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteTag(long id, CancellationToken cancellationToken)
        {
            var deleted = await _tagConfigService.DeleteTagAsync(id, cancellationToken);
            if (!deleted) return NotFound(new { message = $"ID {id} olan değişken bulunamadı." });
            return Ok(new { success = true, message = "Değişken başarıyla silindi." });
        }

        [HttpPost("import-tia")]
        public async Task<IActionResult> ImportTiaPortal([FromBody] TiaImportRequest request, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(request.SourceText))
            {
                return BadRequest(new { message = "İçe aktarılacak TIA Portal metni boş olamaz." });
            }

            int count = await _tagConfigService.ImportFromTiaPortalTextAsync(request.SourceText, request.DbNumber, cancellationToken);
            return Ok(new
            {
                success = true,
                message = $"{count} adet TIA Portal değişkeni başarıyla içe aktarıldı/güncellendi.",
                importedCount = count
            });
        }
    }

    public class TiaImportRequest
    {
        public string SourceText { get; set; } = string.Empty;
        public int DbNumber { get; set; } = 1;
    }
}
