using System.Text;
using System.Text.RegularExpressions;
using BankSlipScannerApp.Data;
using BankSlipScannerApp.DTOs;
using BankSlipScannerApp.Models;
using UglyToad.PdfPig;

namespace BankSlipScannerApp.Services
{
    public class PdfService : IPdfService
    {
        private readonly AppDbContext _context;

        public PdfService(AppDbContext context)
        {
            _context = context;
        }

        public Task<string> DetectPdfTypeAsync(byte[] pdfBytes)
        {
            try
            {
                using var pdf = PdfDocument.Open(pdfBytes);
                var sb = new StringBuilder();
                foreach (var page in pdf.GetPages()) sb.Append(page.Text);
                return Task.FromResult(sb.ToString().Trim().Length > 50 ? "A1" : "A2");
            }
            catch { return Task.FromResult("A2"); }
        }

        public Task<string> GetRawTextAsync(byte[] pdfBytes)
        {
            try
            {
                using var pdf = PdfDocument.Open(pdfBytes);
                var sb = new StringBuilder();
                foreach (var page in pdf.GetPages()) sb.AppendLine(page.Text);
                var text = sb.ToString().Trim();
                if (text.Length > 50) return Task.FromResult(text);
            }
            catch { }
            return Task.FromResult(ExtractTextOCR(pdfBytes));
        }

        public async Task<BankSlipResultDto> ProcessPdfAsync(byte[] pdfBytes, string fileName)
        {
            var pdfType = await DetectPdfTypeAsync(pdfBytes);
            string rawText;

            if (pdfType == "A1")
                rawText = ExtractTextA1(pdfBytes);
            else
            {
                rawText = ExtractTextOCR(pdfBytes);
                if (string.IsNullOrWhiteSpace(rawText))
                    return new BankSlipResultDto
                    {
                        Success = false,
                        PdfType = "A2",
                        FileName = fileName,
                        Message = "OCR échoué."
                    };
            }

            var result = ParseBankSlip(rawText, fileName);
            result.PdfType = pdfType;
            result.FileName = fileName;
            result.RawText = rawText;

            await SaveToDatabase(result);
            return result;
        }

        // ═══════════════════════════════════════════════════════
        // EXTRACTION TEXTE
        // ═══════════════════════════════════════════════════════
        private string ExtractTextA1(byte[] pdfBytes)
        {
            var sb = new StringBuilder();
            using var pdf = PdfDocument.Open(pdfBytes);
            foreach (var page in pdf.GetPages()) sb.AppendLine(page.Text);
            return sb.ToString();
        }

        private string ExtractTextOCR(byte[] pdfBytes)
        {
            try
            {
                using var pdfStream = new MemoryStream(pdfBytes);
                var images = PDFtoImage.Conversion.ToImages(pdfStream);
                var sb = new StringBuilder();
                using var engine = new Tesseract.TesseractEngine(
                    "./tessdata", "fra+ara", Tesseract.EngineMode.Default);
                engine.SetVariable("preserve_interword_spaces", "1");
                foreach (var image in images)
                {
                    using var ms = new MemoryStream();
                    using var skImage = SkiaSharp.SKImage.FromBitmap(image);
                    using var data = skImage.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
                    data.SaveTo(ms);
                    using var pix = Tesseract.Pix.LoadFromMemory(ms.ToArray());
                    using var page = engine.Process(pix);
                    sb.AppendLine(page.GetText());
                }
                return sb.ToString();
            }
            catch (Exception ex) { throw new Exception($"Erreur OCR : {ex.Message}"); }
        }

        // ═══════════════════════════════════════════════════════
        // PARSING PRINCIPAL
        // ═══════════════════════════════════════════════════════
        private BankSlipResultDto ParseBankSlip(string rawText, string fileName)
        {
            var flat = Flatten(rawText);

            var result = new BankSlipResultDto
            {
                // ─────────────────────────────────────────────────
                // IMPORTANT : PdfPig colle les mots sans espaces !
                // "Titulaire :Karim Mansouri ChaabaneBanque :ZITOUNA"
                // → \s* (0 ou plusieurs espaces) entre valeur et label
                // ─────────────────────────────────────────────────

                // Client : entre "Titulaire :" et "Banque :"
                Client = Match(flat, @"Titulaire\s*:\s*(.+?)\s*Banque\s*:"),

                // Banque : lettres MAJ après "Banque :"
                // "Banque :ZITOUNAAdresse" → [A-Z]+ capture "ZITOUNA" seulement
                Banque = DetectBanque(flat, fileName),

                // Agence : entre "Agence" et "RIB :"
                // "Agence Agence Zaouiet SousseRIB :" → ":" absent après Agence !
                Agence = Match(flat, @"Agence\s*:?\s*(.+?)\s*RIB\s*:"),

                // RIB : entre "RIB :" et "Devise :"
                // "RIB :25 165 0000097774906 34Devise :"
                RIB = Match(flat, @"RIB\s*:\s*(.+?)\s*Devise\s*:"),

                // Devise : exactement 3 lettres MAJ après "Devise :"
                // "Devise :TNDIBAN :" → [A-Z]{3} capture "TND" seulement
                Devise = Match(flat, @"Devise\s*:\s*([A-Z]{3})"),

                // IBAN : entre "IBAN :" et "Période :"
                // "IBAN : TN59 2516 5000 0097 7749 0634Période :"
                IBAN = Match(flat, @"IBAN\s*:\s*(.+?)\s*P[eé]riode\s*:"),

                // Dates : dans "Période :Du 01/03/2025 au 31/03/2025"
                DateDebut = Match(flat, @"P[eé]riode\s*:\s*Du\s+(\d{2}/\d{2}/\d{4})\s+au"),
                DateFin = Match(flat, @"P[eé]riode\s*:\s*Du\s+\d{2}/\d{2}/\d{4}\s+au\s+(\d{2}/\d{2}/\d{4})"),

                SoldeDepart = ExtractSoldeDepart(flat),
                SoldeFinal = ExtractSoldeFinal(flat),
            };

            result.Transactions = ParseTransactions(rawText);
            result.Success = true;
            result.Message = $"Relevé {result.Banque} traité — {result.Transactions.Count} transaction(s).";
            return result;
        }

        // ═══════════════════════════════════════════════════════
        // FLATTEN
        // ═══════════════════════════════════════════════════════
        private string Flatten(string text)
        {
            var f = text.Replace("\r\n", " ").Replace("\r", " ").Replace("\n", " ");
            return Regex.Replace(f, @"\s{2,}", " ").Trim();
        }

        private string? Match(string flat, string pattern)
        {
            var m = Regex.Match(flat, pattern, RegexOptions.IgnoreCase);
            return m.Success ? m.Groups[1].Value.Trim() : null;
        }

        // ═══════════════════════════════════════════════════════
        // DÉTECTER BANQUE
        // ═══════════════════════════════════════════════════════
        private string DetectBanque(string flat, string fileName)
        {
            // "Banque :ZITOUNAAdresse" → [A-Z]+ = "ZITOUNA" (s'arrête aux minuscules)
            var m = Regex.Match(flat, @"Banque\s*:\s*([A-Z]+)");
            if (m.Success)
            {
                switch (m.Groups[1].Value)
                {
                    case "STB": return "STB";
                    case "BIAT": return "BIAT";
                    case "ZITOUNA": return "Zitouna";
                    case "ATTIJARI": return "Attijariwafa";
                    case "BNA": return "BNA";
                    case "AMEN": return "Amen Bank";
                    case "ATB": return "ATB";
                    case "BH": return "BH Bank";
                    case "UIB": return "UIB";
                    case "BT": return "BT";
                }
            }

            var t = flat.ToLower();
            if (t.Contains("société tunisienne de banque")) return "STB";
            if (t.Contains("banque internationale arabe")) return "BIAT";
            if (t.Contains("banque zitouna")) return "Zitouna";
            if (t.Contains("attijari bank")) return "Attijariwafa";
            if (t.Contains("banque nationale agricole")) return "BNA";
            if (t.Contains("amen bank")) return "Amen Bank";
            if (t.Contains("arab tunisian bank")) return "ATB";
            if (t.Contains("bh bank")) return "BH Bank";
            if (t.Contains("union internationale de banques")) return "UIB";
            if (t.Contains("banque de tunisie")) return "BT";

            var f = fileName.ToLower();
            if (f.Contains("stb")) return "STB";
            if (f.Contains("biat")) return "BIAT";
            if (f.Contains("zitouna")) return "Zitouna";
            if (f.Contains("attijari")) return "Attijariwafa";
            if (f.Contains("bna")) return "BNA";
            if (f.Contains("amen")) return "Amen Bank";
            if (f.Contains("atb")) return "ATB";
            if (f.Contains("bh")) return "BH Bank";
            if (f.Contains("uib")) return "UIB";
            if (f.Contains("_bt")) return "BT";

            return "Banque Inconnue";
        }

        // ═══════════════════════════════════════════════════════
        // SOLDE DÉPART — testé ✅ sur rawText exact Swagger
        // ═══════════════════════════════════════════════════════
        private string? ExtractSoldeDepart(string flat)
        {
            var montant = @"([\d][\d\s]*,\d{3})";
            var patterns = new[]
            {
                @"Solde\s+initial\s*:\s*"          + montant,  // STB
                @"Ancien\s+solde\s*:\s*"            + montant,  // BIAT
                @"SOLDE\s+DEBUT\s*:\s*"             + montant,  // Zitouna ✅
                @"SOLDE\s+DEPART\s*:\s*"            + montant,  // Attijariwafa
                @"Report\s+ant[eé]rieur\s*:\s*"    + montant,  // BNA
                @"Solde\s+pr[eé]c[eé]dent\s*:\s*"  + montant,  // Amen + UIB
                @"Solde\s+de\s+d[eé]part\s*:\s*"   + montant,  // ATB + BT
                @"Solde\s+report[eé]\s*:\s*"        + montant,  // BH
            };
            foreach (var p in patterns)
            {
                var m = Regex.Match(flat, p, RegexOptions.IgnoreCase);
                if (m.Success) return NettoyerMontant(m.Groups[1].Value);
            }
            return null;
        }

        // ═══════════════════════════════════════════════════════
        // SOLDE FINAL — testé ✅ sur rawText exact Swagger
        // Zitouna : "Solde au 31/03/ 2025 :12 227,120"
        //            → espace dans date (\s*) + ":" présent ici
        // ═══════════════════════════════════════════════════════
        private string? ExtractSoldeFinal(string flat)
        {
            var montant = @"([\d][\d\s]*,\d{3})";
            var patterns = new[]
            {
                @"Solde\s+final\s*:\s*"                                        + montant, // STB
                @"Nouveau\s+solde\s*:\s*"                                      + montant, // BIAT + BH
                @"Solde\s+au\s+\d{2}/\d{2}/\s*\d{4}\s*:\s*"                  + montant, // Zitouna ✅
                @"SOLDE\s+FINAL\s*:\s*"                                        + montant, // Attijariwafa
                @"Solde\s+arr[êe]t[eé]\s+au\s+\d{2}/\d{2}/\s*\d{4}\s*:\s*"  + montant, // BNA
                @"Solde\s+actuel\s*:\s*"                                       + montant, // Amen + UIB
                @"Solde\s+de\s+cl[ôo]ture\s*:\s*"                             + montant, // ATB + BT
            };
            foreach (var p in patterns)
            {
                var m = Regex.Match(flat, p, RegexOptions.IgnoreCase);
                if (m.Success) return NettoyerMontant(m.Groups[1].Value);
            }
            return null;
        }

        // ═══════════════════════════════════════════════════════
        // PARSER TRANSACTIONS — testé ✅ sur rawText exact Swagger
        // PdfPig colle : "05/03/2025COMM REGLEMENT...05/03/2025250,000"
        // → \s* entre date et libellé (0 espaces possible)
        // ═══════════════════════════════════════════════════════
        private List<BankTransactionDto> ParseTransactions(string rawText)
        {
            var transactions = new List<BankTransactionDto>();
            var lines = rawText.Split('\n');
            bool tableStarted = false;

            foreach (var lineRaw in lines)
            {
                var line = lineRaw.Trim();
                if (string.IsNullOrWhiteSpace(line)) continue;

                if (!tableStarted)
                {
                    if (Regex.IsMatch(line, @"D[eé]bit", RegexOptions.IgnoreCase) &&
                        Regex.IsMatch(line, @"Cr[eé]dit", RegexOptions.IgnoreCase))
                        tableStarted = true;
                    continue;
                }

                if (Regex.IsMatch(line, @"^[Cc]e\s+relev[eé]")) break;

                // Pattern testé ✅ sur rawText exact :
                // \s* = 0 ou plusieurs espaces (PdfPig colle les mots)
                // ,\s*\d{3} = gérer "2 000, 000" (espace après virgule)
                var m = Regex.Match(line,
                    @"^(\d{2}/\d{2}/\d{4})\s*(.+?)\s*(\d{2}/\d{2}/\d{4})\s*([\d][\d\s]*,\s*\d{3})\s*$");

                if (!m.Success) continue;

                var libelle = m.Groups[2].Value.Trim();
                var montant = ParseMontant(m.Groups[4].Value);

                decimal? debit = null;
                decimal? credit = null;
                if (EstCredit(libelle)) credit = montant;
                else debit = montant;

                transactions.Add(new BankTransactionDto
                {
                    Date = m.Groups[1].Value,
                    Libelle = libelle,
                    DateValeur = m.Groups[3].Value,
                    Debit = debit,
                    Credit = credit
                });
            }

            return transactions;
        }

        private bool EstCredit(string libelle)
        {
            var l = libelle.ToUpper();
            var credits = new[]
            {
                "VIREMENT SALAIRE", "SALAIRE ",       "VIREMENT RECU",
                "VERSEMENT ESPECES","VERSEMENT CHEQUE","INTERETS CREDITEURS",
                "IN TERETS CREDITEURS",               // PdfPig coupe parfois le mot
                "REGULARISATION INTERETS",
                "SUBVENTION", "DIVIDENDES", "LOCATION ETE", "PENSION",
            };
            foreach (var c in credits)
                if (l.Contains(c)) return true;
            return false;
        }

        private async Task SaveToDatabase(BankSlipResultDto result)
        {
            var pdfUpload = new PdfUpload
            {
                FileName = result.FileName,
                PdfType = result.PdfType,
                IBAN = result.IBAN,
                RIB = result.RIB,
                Compte = result.Compte,
                Banque = result.Banque,
                Agence = result.Agence,
                Devise = result.Devise,
                Client = result.Client,
                SoldeDepart = result.SoldeDepart,
                SoldeFinal = result.SoldeFinal,
                DateDebut = result.DateDebut,
                DateFin = result.DateFin,
                NbTransactions = result.Transactions?.Count ?? 0,
                Success = result.Success,
                Message = result.Message,
                CreatedAt = DateTime.UtcNow
            };
            _context.PdfUploads.Add(pdfUpload);
            await _context.SaveChangesAsync();

            if (result.Transactions?.Any() == true)
            {
                _context.PdfTransactions.AddRange(result.Transactions.Select(t => new PdfTransaction
                {
                    PdfUploadId = pdfUpload.Id,
                    Date = t.Date,
                    DateValeur = t.DateValeur,
                    Libelle = t.Libelle,
                    Debit = t.Debit,
                    Credit = t.Credit
                }));
                await _context.SaveChangesAsync();
            }
        }

        private string NettoyerMontant(string val)
            => val.Trim().Replace(" ", "").Replace(",", ".");

        private decimal ParseMontant(string val)
        {
            var clean = Regex.Replace(val.Trim(), @"\s", "").Replace(",", ".");
            return decimal.TryParse(clean,
                System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture,
                out var r) ? r : 0;
        }
    }
}