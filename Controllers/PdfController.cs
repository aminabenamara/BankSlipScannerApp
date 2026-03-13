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
        // ═══════════════════════════════════════════════════
        [HttpPost("upload")]
        public async Task<IActionResult> Upload(IFormFile file)
        {
            if (file == null || file.Length == 0)
                return BadRequest(new { success = false, message = "Aucun fichier envoyé." });

            var ext = Path.GetExtension(file.FileName).ToLower();
            if (ext != ".pdf" && file.ContentType != "application/pdf")
                return BadRequest(new { success = false, message = "Le fichier doit être un PDF." });

            if (file.Length > 10 * 1024 * 1024)
                return BadRequest(new { success = false, message = "Fichier trop grand (max 10 MB)." });

            try
            {
                using var stream = new MemoryStream();
                await file.CopyToAsync(stream);
                var pdfBytes = stream.ToArray();

                var result = await _pdfService.ProcessPdfAsync(pdfBytes, file.FileName);

                if (!result.Success)
                    return BadRequest(result);

                return Ok(result);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = $"Erreur serveur : {ex.Message}" });
            }
        }

        // ═══════════════════════════════════════════════════
        // POST /api/pdf/detect-type
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

        // ═══════════════════════════════════════════════════
        // POST /api/pdf/rawtext
        // ═══════════════════════════════════════════════════
        [HttpPost("rawtext")]
        public async Task<IActionResult> GetRawText(IFormFile file)
        {
            if (file == null || file.Length == 0)
                return BadRequest("Fichier manquant.");

            using var ms = new MemoryStream();
            await file.CopyToAsync(ms);
            var pdfBytes = ms.ToArray();

            var rawText = await _pdfService.GetRawTextAsync(pdfBytes);
            return Ok(new { fileName = file.FileName, rawText });
        }

        // ═══════════════════════════════════════════════════
        // POST /api/pdf/debug-raw          ← NOUVEAU
        // Voir EXACTEMENT ce que PdfPig lit + test Regex
        // Uploader n'importe quel PDF → voir pourquoi NULL
        // ═══════════════════════════════════════════════════
        [HttpPost("debug-raw")]
        public async Task<IActionResult> DebugRaw(IFormFile file)
        {
            if (file == null || file.Length == 0)
                return BadRequest("Fichier manquant.");

            using var ms = new MemoryStream();
            await file.CopyToAsync(ms);
            var bytes = ms.ToArray();

            try
            {
                // ── 1. Lire texte brut PdfPig ──────────────
                using var pdf = UglyToad.PdfPig.PdfDocument.Open(bytes);
                var sbRaw = new System.Text.StringBuilder();
                foreach (var page in pdf.GetPages())
                    sbRaw.AppendLine(page.Text);
                var rawText = sbRaw.ToString();

                // ── 2. Lignes numérotées ───────────────────
                var lines = rawText.Split('\n');
                var sbLines = new System.Text.StringBuilder();
                for (int i = 0; i < lines.Length; i++)
                    sbLines.AppendLine($"L{i:00}: [{lines[i].TrimEnd()}]");

                // ── 3. Flat text ───────────────────────────
                var flat = System.Text.RegularExpressions.Regex.Replace(
                    rawText.Replace("\r\n", " ").Replace("\r", " ").Replace("\n", " "),
                    @"\s{2,}", " ").Trim();

                // ── 4. Test chaque Regex ───────────────────
                var results = new Dictionary<string, string?>();
                var regexTests = new Dictionary<string, string>
                {
                    ["Client"] = @"Titulaire\s*:\s*(.+?)\s+Banque\s*:",
                    ["Banque"] = @"Banque\s*:\s*(\w+)",
                    ["Agence"] = @"Agence\s*:\s*(.+?)\s+RIB\s*:",
                    ["RIB"] = @"RIB\s*:\s*(.+?)\s+Devise\s*:",
                    ["Devise"] = @"Devise\s*:\s*(\w+)",
                    ["IBAN"] = @"IBAN\s*:\s*(.+?)\s+P[eé]riode\s*:",
                    ["DateDebut"] = @"P[eé]riode\s*:\s*Du\s+(\d{2}/\d{2}/\d{4})\s+au",
                    ["DateFin"] = @"P[eé]riode\s*:\s*Du\s+\d{2}/\d{2}/\d{4}\s+au\s+(\d{2}/\d{2}/\d{4})",
                };
                foreach (var kv in regexTests)
                {
                    var m = System.Text.RegularExpressions.Regex.Match(
                        flat, kv.Value, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    results[kv.Key] = m.Success ? m.Groups[1].Value.Trim() : "❌ NULL";
                }

                // ── 5. Test soldes ─────────────────────────
                var soldesDepart = new[]
                {
                    @"Solde\s+initial\s*:\s*([\d][\d\s]*,\d{3})",
                    @"Ancien\s+solde\s*:\s*([\d][\d\s]*,\d{3})",
                    @"SOLDE\s+DEBUT\s*:\s*([\d][\d\s]*,\d{3})",
                    @"SOLDE\s+DEPART\s*:\s*([\d][\d\s]*,\d{3})",
                    @"Report\s+ant[eé]rieur\s*:\s*([\d][\d\s]*,\d{3})",
                    @"Solde\s+pr[eé]c[eé]dent\s*:\s*([\d][\d\s]*,\d{3})",
                    @"Solde\s+de\s+d[eé]part\s*:\s*([\d][\d\s]*,\d{3})",
                    @"Solde\s+report[eé]\s*:\s*([\d][\d\s]*,\d{3})",
                };
                var soldesFinal = new[]
                {
                    @"Solde\s+final\s*:\s*([\d][\d\s]*,\d{3})",
                    @"Nouveau\s+solde\s*:\s*([\d][\d\s]*,\d{3})",
                    @"Solde\s+au\s+\d{2}/\d{2}/\d{4}\s*:\s*([\d][\d\s]*,\d{3})",
                    @"SOLDE\s+FINAL\s*:\s*([\d][\d\s]*,\d{3})",
                    @"Solde\s+arr[êe]t[eé]\s+au\s+\d{2}/\d{2}/\d{4}\s*:\s*([\d][\d\s]*,\d{3})",
                    @"Solde\s+actuel\s*:\s*([\d][\d\s]*,\d{3})",
                    @"Solde\s+de\s+cl[ôo]ture\s*:\s*([\d][\d\s]*,\d{3})",
                };

                string? sd = null;
                foreach (var p in soldesDepart)
                {
                    var m = System.Text.RegularExpressions.Regex.Match(
                        flat, p, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    if (m.Success) { sd = m.Groups[1].Value.Trim(); break; }
                }
                string? sf = null;
                foreach (var p in soldesFinal)
                {
                    var m = System.Text.RegularExpressions.Regex.Match(
                        flat, p, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    if (m.Success) { sf = m.Groups[1].Value.Trim(); break; }
                }
                results["SoldeDepart"] = sd ?? " NULL";
                results["SoldeFinal"] = sf ?? " NULL";

                return Ok(new
                {
                    fileName = file.FileName,
                    lignesNumerotees = sbLines.ToString(),
                    flatText = flat.Substring(0, Math.Min(flat.Length, 1500)),
                    regexResults = results
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = ex.Message });
            }
        }
    }
}