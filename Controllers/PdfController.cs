using BankSlipScannerApp.DTOs;
using BankSlipScannerApp.Services;
using Microsoft.AspNetCore.Mvc;

namespace BankSlipScannerApp.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class PdfController : ControllerBase
    {
        private readonly IPdfService _pdfService;

        public PdfController(IPdfService pdfService)
        {
            _pdfService = pdfService;
        }

        // ═══════════════════════════════════════════════════
        // POST /api/pdf/upload
        // Upload PDF → extraction complète des données
        // ═══════════════════════════════════════════════════
        [HttpPost("upload")]
        public async Task<IActionResult> Upload(IFormFile file)
        {
            // 1. Vérifier fichier présent
            if (file == null || file.Length == 0)
                return BadRequest(new { success = false, message = "Aucun fichier envoyé." });

            // 2. Vérifier que c'est un PDF
            var ext = Path.GetExtension(file.FileName).ToLower();
            if (ext != ".pdf" && file.ContentType != "application/pdf")
                return BadRequest(new { success = false, message = "Le fichier doit être un PDF." });

            // 3. Vérifier taille max 10 MB
            if (file.Length > 10 * 1024 * 1024)
                return BadRequest(new { success = false, message = "Fichier trop grand (max 10 MB)." });

            try
            {
                // 4. Lire les bytes du fichier
                using var stream = new MemoryStream();
                await file.CopyToAsync(stream);
                var pdfBytes = stream.ToArray();

                // 5. Traiter le PDF
                var result = await _pdfService.ProcessPdfAsync(pdfBytes, file.FileName);

                if (!result.Success)
                    return BadRequest(result);

                return Ok(result);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    success = false,
                    message = $"Erreur serveur : {ex.Message}"
                });
            }
        }

        // ═══════════════════════════════════════════════════
        // POST /api/pdf/detect-type
        // Détecter seulement A1 ou A2
        // ═══════════════════════════════════════════════════
        [HttpPost("detect-type")]
        public async Task<IActionResult> DetectType(IFormFile file)
        {
            if (file == null || file.Length == 0)
                return BadRequest(new { success = false, message = "Aucun fichier envoyé." });

            try
            {
                using var stream = new MemoryStream();
                await file.CopyToAsync(stream);
                var pdfBytes = stream.ToArray();

                var pdfType = await _pdfService.DetectPdfTypeAsync(pdfBytes);

                return Ok(new PdfTypeResultDto
                {
                    Success = true,
                    FileName = file.FileName,
                    PdfType = pdfType,
                    Message = pdfType == "A1"
                        ? "PDF natif — texte extractible directement."
                        : "PDF scanné — OCR nécessaire."
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        [HttpPost("rawtext")]
        public async Task<IActionResult> GetRawText(IFormFile file)
        {
            if (file == null || file.Length == 0)
                return BadRequest("Fichier manquant.");

            using var ms = new MemoryStream();
            await file.CopyToAsync(ms);
            var pdfBytes = ms.ToArray();

            var rawText = await _pdfService.GetRawTextAsync(pdfBytes);
            return Ok(new { fileName = file.FileName, rawText = rawText });
        }
    }
}