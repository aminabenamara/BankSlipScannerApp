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

        // ═══════════════════════════════════════════
        // DÉTECTER TYPE A1 ou A2
        // ═══════════════════════════════════════════
        public Task<string> DetectPdfTypeAsync(byte[] pdfBytes)
        {
            try
            {
                using var pdf = PdfDocument.Open(pdfBytes);
                var sb = new StringBuilder();
                foreach (var page in pdf.GetPages())
                    sb.Append(page.Text);
                var text = sb.ToString().Trim();
                return Task.FromResult(text.Length > 50 ? "A1" : "A2");
            }
            catch { return Task.FromResult("A2"); }
        }

        // ═══════════════════════════════════════════
        // GET RAW TEXT
        // ═══════════════════════════════════════════
        public Task<string> GetRawTextAsync(byte[] pdfBytes)
        {
            try
            {
                using var pdf = PdfDocument.Open(pdfBytes);
                var sb = new StringBuilder();
                foreach (var page in pdf.GetPages())
                    sb.AppendLine(page.Text);
                var text = sb.ToString().Trim();
                if (text.Length > 50) return Task.FromResult(text);
            }
            catch { }
            return Task.FromResult(ExtractTextA2_OCR(pdfBytes));
        }

        // ═══════════════════════════════════════════
        // TRAITEMENT COMPLET
        // ═══════════════════════════════════════════
        public async Task<BankSlipResultDto> ProcessPdfAsync(byte[] pdfBytes, string fileName)
        {
            var pdfType = await DetectPdfTypeAsync(pdfBytes);
            string rawText;

            if (pdfType == "A1")
                rawText = ExtractTextA1(pdfBytes);
            else
            {
                rawText = ExtractTextA2_OCR(pdfBytes);
                if (string.IsNullOrWhiteSpace(rawText))
                    return new BankSlipResultDto
                    {
                        Success = false,
                        PdfType = "A2",
                        FileName = fileName,
                        Message = "PDF scanné : OCR n'a pas pu extraire le texte."
                    };
            }

            var result = ParseBankSlip(rawText, fileName);
            result.PdfType = pdfType;
            result.FileName = fileName;
            result.RawText = rawText;

            await SaveToDatabase(result);
            return result;
        }

        // ═══════════════════════════════════════════
        // EXTRACTION A1 — PdfPig
        // ═══════════════════════════════════════════
        private string ExtractTextA1(byte[] pdfBytes)
        {
            var sb = new StringBuilder();
            using var pdf = PdfDocument.Open(pdfBytes);
            foreach (var page in pdf.GetPages())
                sb.AppendLine(page.Text);
            return sb.ToString();
        }

        // ═══════════════════════════════════════════
        // EXTRACTION A2 — Tesseract OCR
        // ═══════════════════════════════════════════
        private string ExtractTextA2_OCR(byte[] pdfBytes)
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
            catch (Exception ex)
            {
                throw new Exception($"Erreur OCR : {ex.Message}");
            }
        }

        // ═══════════════════════════════════════════
        // PARSING PRINCIPAL
        // ═══════════════════════════════════════════
        private BankSlipResultDto ParseBankSlip(string text, string fileName)
        {
            var result = new BankSlipResultDto();

            // Texte original (lignes) pour les transactions
            // Texte flat (une seule ligne) pour les champs
            var flat = Flatten(text);

            result.Banque = DetectBanque(flat, fileName);
            result.IBAN = ExtractIBAN(flat);
            result.RIB = ExtractRIB(flat);
            result.Compte = ExtractCompte(flat);
            result.Agence = ExtractAgence(flat);
            result.Devise = ExtractDevise(flat);
            result.Client = ExtractClient(flat);
            result.SoldeDepart = ExtractSoldeDepart(flat);
            result.SoldeFinal = ExtractSoldeFinal(flat);
            result.DateDebut = ExtractDateDebut(flat);
            result.DateFin = ExtractDateFin(flat);

            if (!IsBankStatement(flat))
            {
                result.Success = false;
                result.Message = "Ce document ne semble pas être un relevé bancaire.";
                return result;
            }

            result.Transactions = ParseTransactions(text);
            result.Success = true;
            result.Message = $"Relevé {result.Banque} traité. {result.Transactions.Count} transaction(s).";
            return result;
        }

        // ═══════════════════════════════════════════
        // FLATTEN
        // ═══════════════════════════════════════════
        private string Flatten(string text)
        {
            var f = text.Replace("\r\n", " ").Replace("\r", " ").Replace("\n", " ");
            return Regex.Replace(f, @"\s{2,}", " ");
        }

        // ═══════════════════════════════════════════
        // DÉTECTER LA BANQUE
        // Toutes les banques tunisiennes
        // ═══════════════════════════════════════════
        private string DetectBanque(string text, string fileName)
        {
            var t = text.ToLower();
            var f = fileName.ToLower();

            // ── Banques tunisiennes ────────────────────
            if (t.Contains("zitouna") || f.Contains("zitouna")) return "Zitouna";
            if (t.Contains("attijari") || f.Contains("attijari")
                || f.Contains("bordereu")) return "Attijariwafa";
            if ((t.Contains("stb") && !t.Contains("établi"))
                || t.Contains("société tunisienne de banque")
                || f.Contains("stb")) return "STB";
            if (t.Contains("biat") || f.Contains("biat")) return "BIAT";
            if (t.Contains("bna") || f.Contains("bna")) return "BNA";
            if (t.Contains("amen bank") || f.Contains("amen")) return "Amen Bank";
            if (t.Contains("arab tunisian") || t.Contains("atb")
                || f.Contains("atb")) return "ATB";
            if (t.Contains("banque de l'habitat") || t.Contains("bh bank")
                || f.Contains("bh")) return "BH Bank";
            if (t.Contains("uib") || t.Contains("union internationale")
                || f.Contains("uib")) return "UIB";
            if (t.Contains("btk") || t.Contains("tuniso-koweit")
                || f.Contains("btk")) return "BTK";
            if (t.Contains("banque de tunisie") && !t.Contains("stb")
                || f.Contains("bt.pdf")) return "BT";
            if (t.Contains("citibank") || f.Contains("citi")) return "Citibank";
            if (t.Contains("qnb") || t.Contains("qatar national")
                || f.Contains("qnb")) return "QNB";
            if (t.Contains("abc") || t.Contains("arab banking")
                || f.Contains("abc")) return "ABC";
            if (t.Contains("stusid") || f.Contains("stusid")) return "Stusid Bank";
            if (t.Contains("bts") || t.Contains("tunisie valeurs")
                || f.Contains("bts")) return "BTS";

            return "Banque Inconnue";
        }

        // ═══════════════════════════════════════════
        // EXTRACTION IBAN
        // Tous les formats possibles
        // ═══════════════════════════════════════════
        private string? ExtractIBAN(string text)
        {
            var patterns = new[]
            {
                // Standard : IBAN TN59...
                @"IBAN[\s:]*([A-Z]{2}\d{2}[\d\s]{10,30})",
                // OCR lit "BAN" au lieu de "IBAN"
                @"\bBAN[\s:]+([A-Z]{2}[\s]?\d{2}[\d\s]{10,30})",
                // TN59 directement dans le texte
                @"\b(TN\d{2}[\s\-]?\d{4}[\s\-]?\d{4}[\s\-]?\d{4}[\s\-]?\d{4}[\s\-]?\d{0,6})\b",
                // OCR: "BAN TNS9 25165..."
                @"BAN\s+TN[S\s]?\d[\d\s]{15,25}",
            };

            foreach (var p in patterns)
            {
                var m = Regex.Match(text, p, RegexOptions.IgnoreCase);
                if (!m.Success) continue;

                string val;
                if (p.Contains("BAN TN"))
                    val = "TN" + Regex.Replace(m.Value.Replace("BAN", "").Replace("TNS", ""), @"\s+", "");
                else
                    val = Regex.Replace(m.Groups[1].Value.Trim(), @"\s+", "");

                if (val.Length >= 15) return val;
            }
            return null;
        }

        // ═══════════════════════════════════════════
        // EXTRACTION RIB
        // ═══════════════════════════════════════════
        private string? ExtractRIB(string text)
        {
            var patterns = new[]
            {
                @"(?:RIB|R\.I\.B\.?)[\s:]+(\d[\d\s]{15,25})",
                @"\b(\d{20})\b",
                @"\b(\d{2}\s\d{3}\s\d{13}\s\d{2})\b",
            };
            foreach (var p in patterns)
            {
                var m = Regex.Match(text, p, RegexOptions.IgnoreCase);
                if (m.Success)
                {
                    var rib = Regex.Replace(m.Groups[1].Value.Trim(), @"\s+", "");
                    if (rib.Length >= 15) return rib;
                }
            }
            return null;
        }

        // ═══════════════════════════════════════════
        // EXTRACTION COMPTE
        // Tous les labels possibles
        // ═══════════════════════════════════════════
        private string? ExtractCompte(string text)
        {
            var patterns = new[]
            {
                // Français
                @"(?:N[°o\.]\s*[Cc]ompte|[Cc]ompte\s*N[°o\.]|COMPTE\s*N[°o\.])[\s:]+([A-Z0-9][\w\s\-]{1,30})",
                @"(?:Num[eé]ro\s*(?:de\s*)?[Cc]ompte|NUM\.?\s*COMPTE)[\s:]+([A-Z0-9][\w\s\-]{1,25})",
                @"\b[Cc]ompte\s*:[\s]*([A-Z0-9][\w\s\-]{1,25})",
                // Arabe
                @"(?:رقم\s*الحساب|حساب\s*رقم)[\s:]+([A-Z0-9][\w\s\-]{1,25})",
                // Anglais
                @"(?:Account\s*N[o°\.]|Account\s*Number)[\s:]+([A-Z0-9][\w\s\-]{1,25})",
            };
            foreach (var p in patterns)
            {
                var m = Regex.Match(text, p, RegexOptions.IgnoreCase);
                if (m.Success)
                {
                    var compte = m.Groups[1].Value.Trim();
                    // Couper au premier mot clé suivant
                    compte = Regex.Split(compte,
                        @"\b(?:IBAN|RIB|Agence|Devise|Client|Solde|Date|Banque)\b")[0].Trim();
                    if (compte.Length > 1) return compte;
                }
            }
            return null;
        }

        // ═══════════════════════════════════════════
        // EXTRACTION AGENCE
        // ═══════════════════════════════════════════
        private string? ExtractAgence(string text)
        {
            var patterns = new[]
            {
                // Avec tiret/dash : "Agence — SOUSSE"
                @"[Aa]gence\s*[:\-—–]+\s*([A-Z0-9][A-ZÀ-Ÿa-zà-ÿ0-9\s\-\.]{2,40})",
                // Standard : "Agence : SOUSSE"
                @"[Aa]gence\s*:?\s+([A-Z0-9][A-ZÀ-Ÿa-zà-ÿ0-9\s\-\.]{2,40})",
                // Arabe
                @"(?:وكالة|فرع)[\s:]+([^\n\r]{2,40})",
            };
            foreach (var p in patterns)
            {
                var m = Regex.Match(text, p, RegexOptions.IgnoreCase);
                if (m.Success)
                {
                    var agence = m.Groups[1].Value.Trim();
                    // Couper au prochain champ
                    agence = Regex.Split(agence,
                        @"\b(?:IBAN|RIB|Compte|Devise|Client|Solde|Date|Banque|Titulaire)\b",
                        RegexOptions.IgnoreCase)[0].Trim();
                    // Supprimer les mots parasites OCR à la fin (1-2 lettres)
                    agence = Regex.Replace(agence, @"\s+[a-zA-Z]{1,2}$", "").Trim();
                    if (agence.Length > 2) return agence;
                }
            }
            return null;
        }

        // ═══════════════════════════════════════════
        // EXTRACTION DEVISE
        // ═══════════════════════════════════════════
        private string? ExtractDevise(string text)
        {
            // Avec label
            var m = Regex.Match(text,
                @"(?:Devise|DEVISE|العملة|Monnaie|Currency|Libellé)\s*[:\s\u0600-\u06FF]*\s*(TND|EUR|USD|MAD|DZD|GBP|CHF|JPY)",
                RegexOptions.IgnoreCase);
            if (m.Success) return m.Groups[1].Value.Trim();

            // Sans label — chercher la devise dans le texte
            m = Regex.Match(text, @"\b(TND|EUR|USD|MAD|DZD|GBP)\b");
            return m.Success ? m.Groups[1].Value : null;
        }

        // ═══════════════════════════════════════════
        // EXTRACTION CLIENT
        // ═══════════════════════════════════════════
        private string? ExtractClient(string text)
        {
            var patterns = new[]
            {
                // Français
                @"(?:Client|Titulaire|Nom\s+(?:du\s+)?[Cc]lient|Bénéficiaire|Titulaire\s+du\s+compte)[\s:]+([A-ZÀ-Ÿa-zà-ÿ][A-ZÀ-Ÿa-zà-ÿ\s\-\.]{2,50})",
                // Civilité
                @"(?:M\.\s|Mme\.\s|Mr\.\s|Mlle\.\s)([A-ZÀ-Ÿ][A-ZÀ-Ÿa-zà-ÿ\s\-\.]{2,40})",
                // Arabe
                @"(?:السيد|السيدة|الزبون|العميل)[\s:]+([^\d\n\r]{3,50})",
            };
            foreach (var p in patterns)
            {
                var m = Regex.Match(text, p, RegexOptions.IgnoreCase);
                if (m.Success)
                {
                    var client = m.Groups[1].Value.Trim();
                    client = Regex.Split(client,
                        @"\b(?:IBAN|RIB|Compte|Agence|Devise|Solde|Date|Adresse)\b",
                        RegexOptions.IgnoreCase)[0].Trim();
                    if (client.Length > 2) return client;
                }
            }
            return null;
        }

        // ═══════════════════════════════════════════
        // EXTRACTION SOLDE DÉPART — UNIVERSEL
        // STB:      "Solde initial:"
        // Zitouna:  "SOLDE DEBUT"
        // Attijari: "SOLDE DEPART"
        // BIAT:     "Ancien solde"
        // BNA:      "Report antérieur"
        // UIB:      "Solde précédent"
        // BH:       "Solde reporté"
        // ═══════════════════════════════════════════
        private string? ExtractSoldeDepart(string text)
        {
            var patterns = new[]
            {
                @"[Ss]olde\s+(?:initial|précédent|precédent|anterieur|antérieur|reporté|reporte|de\s+départ|de\s+depart)[\s:]*([+\-]?[\d\s]+[,\.]\d{1,3})",
                @"SOLDE\s+(?:INITIAL|PRECEDENT|ANTERIEUR|REPORTE|DEPART|DEBUT|D[ÉE]BUT|PR[ÉE]C[ÉE]DENT)[\s:]*([+\-]?[\d\s]+[,\.]\d{1,3})",
                @"(?:[Aa]ncien\s+[Ss]olde|ANCIEN\s+SOLDE)[\s:]*([+\-]?[\d\s]+[,\.]\d{1,3})",
                @"(?:[Rr]eport\s+[Aa]nt[eé]rieur|REPORT\s+ANT[ÉE]RIEUR|REPORT)[\s:]*([+\-]?[\d\s]+[,\.]\d{1,3})",
                @"(?:[Pp]revious\s+[Bb]alance|[Oo]pening\s+[Bb]alance)[\s:]*([+\-]?[\d\s]+[,\.]\d{1,3})",
                // Arabe
                @"(?:الرصيد\s+السابق|رصيد\s+أول\s+المدة|الرصيد\s+المرحل)[\s:]*([+\-]?[\d\s]+[,\.]\d{1,3})",
            };
            foreach (var p in patterns)
            {
                var m = Regex.Match(text, p, RegexOptions.IgnoreCase);
                if (m.Success) return CleanMontant(m.Groups[1].Value);
            }
            return null;
        }

        // ═══════════════════════════════════════════
        // EXTRACTION SOLDE FINAL — UNIVERSEL
        // STB:      "Solde final:"
        // Zitouna:  "Solde au 31/03/2025"
        // Attijari: "SOLDE FINAL"
        // BIAT:     "Nouveau solde"
        // BNA:      "Solde arrêté"
        // UIB:      "Solde actuel"
        // ═══════════════════════════════════════════
        private string? ExtractSoldeFinal(string text)
        {
            var patterns = new[]
            {
                // Zitouna : "Solde au 31/03/2025 : 123"
                @"[Ss]olde\s+au\s+\d{2}[\/\-\.]\d{2}[\/\-\.]\d{4}[\s:]*([+\-]?[\d\s]+[,\.]\d{1,3})",
                @"[Ss]olde\s+(?:final|actuel|arrêté|arrete|de\s+clôture|de\s+cloture|nouveau|clôture|cloture)[\s:]*([+\-]?[\d\s]+[,\.]\d{1,3})",
                @"SOLDE\s+(?:FINAL|ACTUEL|ARR[EÊ]T[ÉE]|CLOTURE|CL[ÔO]TURE|NOUVEAU|FIN)[\s:]*([+\-]?[\d\s]+[,\.]\d{1,3})",
                @"(?:[Nn]ouveau\s+[Ss]olde|NOUVEAU\s+SOLDE)[\s:]*([+\-]?[\d\s]+[,\.]\d{1,3})",
                @"(?:[Cc]losing\s+[Bb]alance|[Nn]ew\s+[Bb]alance)[\s:]*([+\-]?[\d\s]+[,\.]\d{1,3})",
                // Arabe
                @"(?:الرصيد\s+الحالي|رصيد\s+آخر\s+المدة|الرصيد\s+الجديد)[\s:]*([+\-]?[\d\s]+[,\.]\d{1,3})",
            };
            foreach (var p in patterns)
            {
                var m = Regex.Match(text, p, RegexOptions.IgnoreCase);
                if (m.Success) return CleanMontant(m.Groups[1].Value);
            }
            return null;
        }

        // ═══════════════════════════════════════════
        // EXTRACTION DATE DÉBUT — UNIVERSEL
        // STB:      "Edité le 01/10/2020"
        // Zitouna:  "Date du 01/03/2025 au"
        // Attijari: "Du 01/01/2025"
        // BIAT:     "Période du 01/01/2025"
        // BNA:      "Relevé du 01/01/2025"
        // ═══════════════════════════════════════════
        private string? ExtractDateDebut(string text)
        {
            var patterns = new[]
            {
                // STB : "Edité le DATE"
                @"[Ee]dit[eé]\s+le\s+(\d{2}[\/\-\.]\d{2}[\/\-\.]\d{2,4})",
                // Zitouna : "Date du DATE au DATE"
                @"[Dd]ate\s+du\s+(\d{2}[\/\-\.]\d{2}[\/\-\.]\d{4})\s+au",
                // Standard
                @"[Pp][eé]riode\s+du\s+(\d{2}[\/\-\.]\d{2}[\/\-\.]\d{2,4})",
                @"\bDu\s*:?\s*(\d{2}[\/\-\.]\d{2}[\/\-\.]\d{2,4})",
                @"[Rr]elev[eé]\s+du\s+(\d{2}[\/\-\.]\d{2}[\/\-\.]\d{2,4})",
                @"[Dd]ate\s+de\s+d[eé]but[\s:]+(\d{2}[\/\-\.]\d{2}[\/\-\.]\d{2,4})",
                @"[Dd]ate\s+du\s+[Rr]elev[eé]\s*:?\s*(\d{2}[\/\-\.]\d{2}[\/\-\.]\d{2,4})",
                @"[Ff]rom\s*:?\s*(\d{2}[\/\-\.]\d{2}[\/\-\.]\d{2,4})",
                // Arabe
                @"(?:من\s+تاريخ|بداية\s+الفترة|من)[\s:]*(\d{2}[\/\-\.]\d{2}[\/\-\.]\d{2,4})",
            };
            foreach (var p in patterns)
            {
                var m = Regex.Match(text, p, RegexOptions.IgnoreCase);
                if (m.Success) return m.Groups[1].Value.Trim();
            }
            return null;
        }

        // ═══════════════════════════════════════════
        // EXTRACTION DATE FIN — UNIVERSEL
        // ═══════════════════════════════════════════
        private string? ExtractDateFin(string text)
        {
            var patterns = new[]
            {
                // Zitouna/Attijari : "Date du X au Y"
                @"[Dd]ate\s+du\s+\d{2}[\/\-\.]\d{2}[\/\-\.]\d{4}\s+au\s+(\d{2}[\/\-\.]\d{2}[\/\-\.]\d{4})",
                // Standard "au DATE"
                @"\bau\s+(\d{2}[\/\-\.]\d{2}[\/\-\.]\d{4})\b",
                // Standard
                @"[Pp][eé]riode\s+du\s+\d{2}[\/\-\.]\d{2}[\/\-\.]\d{4}\s+au\s+(\d{2}[\/\-\.]\d{2}[\/\-\.]\d{4})",
                @"[Dd]ate\s+de\s+fin[\s:]+(\d{2}[\/\-\.]\d{2}[\/\-\.]\d{2,4})",
                @"\bTo\s*:?\s*(\d{2}[\/\-\.]\d{2}[\/\-\.]\d{2,4})",
                @"[Jj]usqu['']au\s+(\d{2}[\/\-\.]\d{2}[\/\-\.]\d{2,4})",
                // Arabe
                @"(?:إلى\s+تاريخ|نهاية\s+الفترة|إلى)[\s:]*(\d{2}[\/\-\.]\d{2}[\/\-\.]\d{2,4})",
            };
            foreach (var p in patterns)
            {
                var m = Regex.Match(text, p, RegexOptions.IgnoreCase);
                if (m.Success) return m.Groups[1].Value.Trim();
            }
            return null;
        }

        // ═══════════════════════════════════════════
        // VÉRIFICATION RELEVÉ BANCAIRE
        // ═══════════════════════════════════════════
        private bool IsBankStatement(string text)
        {
            var keywords = new[]
            {
                "RELEVE", "COMPTE", "SOLDE", "DEBIT", "CREDIT",
                "IBAN", "RIB", "VIREMENT", "PAIEMENT", "VERSEMENT",
                "relevé", "compte", "solde", "bancaire", "banque",
                "transaction", "mouvement", "opération", "agence",
                "initial", "final", "COMM", "EFFET", "FRAIS",
                "رصيد", "حساب", "بنك"
            };
            return keywords.Count(k =>
                text.Contains(k, StringComparison.OrdinalIgnoreCase)) >= 3;
        }

        // ═══════════════════════════════════════════
        // PARSING TRANSACTIONS — UNIVERSEL
        // 4 patterns pour couvrir tous les formats
        // ═══════════════════════════════════════════
        private List<BankTransactionDto> ParseTransactions(string text)
        {
            var transactions = new List<BankTransactionDto>();
            var lines = text.Split('\n');

            // Sauter les lignes d'en-tête (avant le tableau)
            bool tableStarted = false;

            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                if (string.IsNullOrWhiteSpace(line)) continue;

                // Détecter le début du tableau de transactions
                if (!tableStarted)
                {
                    if (Regex.IsMatch(line,
                        @"(?:Date|Op[eé]ration|Libell[eé]|D[eé]bit|Cr[eé]dit|Montant)",
                        RegexOptions.IgnoreCase))
                    {
                        tableStarted = true;
                        continue;
                    }
                }

                // ── Pattern 1 — Complet avec DateValeur ───
                // DATE LIBELLE DATE_VALEUR DEBIT CREDIT
                var m = Regex.Match(line,
                    @"^(\d{2}[\/\-\.]\d{2}[\/\-\.]\d{2,4})\s+(.{3,80}?)\s+(\d{2}[\/\-\.]\d{2}[\/\-\.]\d{2,4})\s+([\d\s,\.]*)\s*([\d\s,\.]*)$");
                if (m.Success)
                {
                    var debit = ParseMontant(m.Groups[4].Value);
                    var credit = ParseMontant(m.Groups[5].Value);
                    if (debit > 0 || credit > 0)
                    {
                        transactions.Add(new BankTransactionDto
                        {
                            Date = m.Groups[1].Value.Trim(),
                            Libelle = m.Groups[2].Value.Trim(),
                            DateValeur = m.Groups[3].Value.Trim(),
                            Debit = debit > 0 ? debit : null,
                            Credit = credit > 0 ? credit : null
                        });
                        continue;
                    }
                }

                // ── Pattern 2 — Simple : DATE LIBELLE DEBIT CREDIT ──
                m = Regex.Match(line,
                    @"^(\d{2}[\/\-\.]\d{2}[\/\-\.]\d{2,4})\s+(.{3,80}?)\s+([\d\s,\.]+)\s*([\d\s,\.]*)$");
                if (m.Success)
                {
                    var debit = ParseMontant(m.Groups[3].Value);
                    var credit = ParseMontant(m.Groups[4].Value);
                    if (debit > 0 || credit > 0)
                    {
                        transactions.Add(new BankTransactionDto
                        {
                            Date = m.Groups[1].Value.Trim(),
                            Libelle = m.Groups[2].Value.Trim(),
                            Debit = debit > 0 ? debit : null,
                            Credit = credit > 0 ? credit : null
                        });
                        continue;
                    }
                }

                // ── Pattern 3 — Libellé sur ligne suivante ──
                // Ligne i   : DATE (seule)
                // Ligne i+1 : LIBELLE
                // Ligne i+2 : DATE_VALEUR DEBIT CREDIT
                if (Regex.IsMatch(line, @"^\d{2}[\/\-\.]\d{2}[\/\-\.]\d{4}$")
                    && i + 2 < lines.Length)
                {
                    var libelle = lines[i + 1].Trim();
                    var nextLine = lines[i + 2].Trim();
                    var mNext = Regex.Match(nextLine,
                        @"^(\d{2}[\/\-\.]\d{2}[\/\-\.]\d{4})?\s*([\d\s,\.]+)\s*([\d\s,\.]*)$");

                    if (mNext.Success
                        && !string.IsNullOrWhiteSpace(libelle)
                        && !Regex.IsMatch(libelle, @"^\d{2}[\/\-\.]"))
                    {
                        var debit = ParseMontant(mNext.Groups[2].Value);
                        var credit = ParseMontant(mNext.Groups[3].Value);
                        if (debit > 0 || credit > 0)
                        {
                            transactions.Add(new BankTransactionDto
                            {
                                Date = line,
                                Libelle = libelle,
                                DateValeur = mNext.Groups[1].Value.Trim(),
                                Debit = debit > 0 ? debit : null,
                                Credit = credit > 0 ? credit : null
                            });
                            i += 2;
                            continue;
                        }
                    }
                }

                // ── Pattern 4 — Attijariwafa ──
                // CODE DATE LIBELLE DATE_VALEUR DEBIT CREDIT
                m = Regex.Match(line,
                    @"^(\d{3,6})\s+(\d{2}[\/\-\.]\d{2}[\/\-\.]\d{2,4})\s+(.{3,60}?)\s+(\d{2}[\/\-\.]\d{2}[\/\-\.]\d{2,4})\s+([\d\s,\.]*)\s*([\d\s,\.]*)$");
                if (m.Success)
                {
                    var debit = ParseMontant(m.Groups[5].Value);
                    var credit = ParseMontant(m.Groups[6].Value);
                    if (debit > 0 || credit > 0)
                    {
                        transactions.Add(new BankTransactionDto
                        {
                            Date = m.Groups[2].Value.Trim(),
                            Libelle = m.Groups[3].Value.Trim(),
                            DateValeur = m.Groups[4].Value.Trim(),
                            Debit = debit > 0 ? debit : null,
                            Credit = credit > 0 ? credit : null
                        });
                    }
                }
            }

            return transactions;
        }

        // ═══════════════════════════════════════════
        // SAUVEGARDER EN BASE
        // ═══════════════════════════════════════════
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
                var transactions = result.Transactions.Select(t => new PdfTransaction
                {
                    PdfUploadId = pdfUpload.Id,
                    Date = t.Date,
                    DateValeur = t.DateValeur,
                    Libelle = t.Libelle,
                    Debit = t.Debit,
                    Credit = t.Credit
                }).ToList();

                _context.PdfTransactions.AddRange(transactions);
                await _context.SaveChangesAsync();
            }
        }

        // ═══════════════════════════════════════════
        // HELPERS
        // ═══════════════════════════════════════════
        private string CleanMontant(string value)
            => value.Trim().Replace(" ", "");

        private decimal ParseMontant(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return 0;
            var cleaned = Regex.Replace(value.Trim(), @"\s+", "").Replace(",", ".");
            var parts = cleaned.Split('.');
            if (parts.Length > 2)
                cleaned = string.Join("", parts.Take(parts.Length - 1)) + "." + parts.Last();
            return decimal.TryParse(cleaned,
                System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture,
                out var r) ? r : 0;
        }
    }
}